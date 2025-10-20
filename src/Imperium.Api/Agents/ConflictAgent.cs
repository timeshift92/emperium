using Imperium.Domain.Agents;
using Imperium.Domain.Models;
using Imperium.Domain.Services;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Imperium.Api.Agents;

public class ConflictAgent : IWorldAgent
{
    public string Name => "ConflictAI";

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var metrics = scopeServices.GetRequiredService<Imperium.Api.MetricsService>();
        var dispatcher = scopeServices.GetRequiredService<IEventDispatcher>();
        var llm = scopeServices.GetService<Imperium.Llm.ILlmClient>();
        var logger = scopeServices.GetService<Microsoft.Extensions.Logging.ILogger<ConflictAgent>>();

        var threshold = DateTime.UtcNow.AddMinutes(-1);
        var attempts = await db.GameEvents
            .Where(e => e.Type == "ownership_reclaim_attempt" && e.Timestamp >= threshold)
            .ToListAsync();

    var randProvider = scopeServices.GetService<Imperium.Api.Utils.IRandomProvider>();
    var randDouble = randProvider?.NextDouble() ?? Random.Shared.NextDouble();
    // We need NextDouble and NextInt usage below — we'll call provider methods where necessary
    // Provide local lambda wrappers for NextDouble and NextInt
    Func<double> NextDouble = () => randProvider?.NextDouble() ?? Random.Shared.NextDouble();
    Func<int,int> NextInt = (max) => randProvider?.NextInt(max) ?? Random.Shared.Next(max);

        foreach (var attempt in attempts)
        {
            var reactions = await db.GameEvents
                .Where(e => e.Type == "npc_reaction" && e.PayloadJson.Contains(attempt.Id.ToString()))
                .ToListAsync();

            var supporters = 0; // integer score: support=+1, attempt=+2, oppose=-1
            foreach (var reaction in reactions)
            {
                try
                {
                    using var doc = JsonDocument.Parse(reaction.PayloadJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("action", out var actionProp) && actionProp.ValueKind == JsonValueKind.String)
                    {
                        var action = actionProp.GetString();
                        if (action == "attempt_reclaim") supporters += 2; // stronger signal
                        else if (action == "support_claimant") supporters += 1;
                        else if (action == "oppose_claimant") supporters -= 1;
                    }
                }
                catch
                {
                    // ignore malformed reaction payloads
                }
            }

            // Try to ask LLM to reason about whether this should escalate into conflict.
            // LLM is optional: if not configured or call fails, fall back to heuristic below.
            int llmDelta = 0;
            string? llmRecommendation = null;
            if (llm != null)
            {
                try
                {
                    // Build compact JSON context for LLM
                    var ctx = new
                    {
                        eventId = attempt.Id,
                        location = attempt.Location,
                        payload = TryParseJsonOrRawSafe(attempt.PayloadJson),
                        recentReactions = reactions.Select(r => TryParseJsonOrRawSafe(r.PayloadJson)).ToArray(),
                        supporters
                    };

                    var prompt = "[role:Conflict]";
                    var body = System.Text.Json.JsonSerializer.Serialize(new { context = ctx, instruction = "Return compact JSON: { \"supportersDelta\": int, \"recommendation\": \"start_conflict\"|\"no_conflict\" }" });

                    // combine prompt and body to one string (RoleLlmRouter expects a string prompt)
                    var raw = await llm.SendPromptAsync(prompt + "\n" + body, ct);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(raw);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("supportersDelta", out var sd) && sd.ValueKind == JsonValueKind.Number)
                                llmDelta = sd.GetInt32();
                            if (root.TryGetProperty("recommendation", out var rec) && rec.ValueKind == JsonValueKind.String)
                                llmRecommendation = rec.GetString();
                        }
                        catch (Exception ex)
                        {
                            logger?.LogDebug(ex, "ConflictAgent: LLM returned invalid JSON, falling back to heuristic");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "ConflictAgent: LLM call failed, using heuristic fallback");
                }
            }

            // Apply LLM delta if available
            if (llmDelta != 0) supporters += llmDelta;

            // Heuristic fallback / baseline chance influenced by supporters score
            var chance = 0.25 + supporters * 0.12;
            chance = Math.Clamp(chance, 0.05, 0.90);

            // If LLM explicitly recommended start_conflict, prefer that (additive)
            if (llmRecommendation == "start_conflict")
            {
                chance = Math.Max(chance, 0.75);
            }

            if (NextDouble() < chance)
            {
                var conflictEvent = new GameEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    Type = "conflict_started",
                    Location = attempt.Location,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        sourceEvent = attempt.Id,
                        detail = attempt.PayloadJson,
                        supporters,
                        llm = new { delta = llmDelta, recommendation = llmRecommendation }
                    })
                };

                await dispatcher.EnqueueAsync(conflictEvent);
                metrics.Increment("conflict.started");
                try { metrics.Add("conflict.supporters.total", supporters); } catch { }

                // Economy impact: small global uptick and metals demand
                try
                {
                    var state = scopeServices.GetRequiredService<Imperium.Api.EconomyStateService>();
                    state.SetShock("*", 1.03m, DateTime.UtcNow.AddMinutes(30));
                    // If metals exist, amplify
                    foreach (var item in new [] { "iron", "copper", "bronze", "steel", "weapons", "armor" })
                    {
                        state.SetShock(item, 1.10m, DateTime.UtcNow.AddMinutes(45));
                    }
                }
                catch { }
            }
        }
    }

    private static object? TryParseJsonOrRawSafe(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText())!;
        }
        catch { return json; }
    }
}
