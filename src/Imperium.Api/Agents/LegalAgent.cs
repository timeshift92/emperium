using Imperium.Domain.Agents;
using Imperium.Infrastructure;
using Imperium.Domain.Models;
using Imperium.Llm;
using Imperium.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Imperium.Api.Agents;

public class LegalAgent : IWorldAgent
{
    public string Name => "LegalAI";

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var metrics = scopeServices.GetRequiredService<Imperium.Api.MetricsService>();
        var dispatcher = scopeServices.GetRequiredService<IEventDispatcher>();
        var llm = scopeServices.GetService<Imperium.Llm.ILlmClient>();

        // process unresolved disputes from last N minutes
        var threshold = DateTime.UtcNow.AddMinutes(-5);
    var disputes = await db.GameEvents.Where(e => e.Type == "ownership_dispute" && e.Timestamp >= threshold).ToListAsync();
        foreach (var d in disputes)
        {
            try
            {
                // Try LLM-assisted ruling when available. Use role prefix so RoleLlmRouter resolves model from configuration (e.g. "Council").
                if (llm != null)
                {
                    var rolePrefix = "[role:Council]\n";
                    var promptBody = $"Resolve the following ownership dispute and return a single compact JSON object ONLY with fields: winner (GUID string or null) and reason (string). Dispute: {d.PayloadJson}";
                    var prompt = rolePrefix + promptBody;

                    var raw = await llm.SendPromptAsync(prompt, ct);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(raw);
                            var root = doc.RootElement;

                            Guid? winner = null;
                            if (root.TryGetProperty("winner", out var w))
                            {
                                if (w.ValueKind == JsonValueKind.String)
                                {
                                    var s = w.GetString();
                                    if (!string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out var g)) winner = g;
                                }
                                else if (w.ValueKind == JsonValueKind.Null)
                                {
                                    winner = null;
                                }
                            }

                            var reason = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : "llm_decision";

                            var ev = new GameEvent { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Type = "legal_ruling", Location = d.Location, PayloadJson = JsonSerializer.Serialize(new { winner, reason }) };
                            await dispatcher.EnqueueAsync(ev);
                            metrics.Increment("legal.rulings.llm");
                            continue;
                        }
                        catch
                        {
                            // fall back to simple logic below
                        }
                    }
                }

                // Simple fallback decision: random pick between involved owner and null (no change)
                var rand = Random.Shared;
                Guid? winnerId = null;
                if (rand.NextDouble() < 0.5)
                {
                    try
                    {
                        var pj = JsonDocument.Parse(d.PayloadJson);
                        if (pj.RootElement.TryGetProperty("ownerId", out var o))
                        {
                            if (o.ValueKind == JsonValueKind.String)
                            {
                                var s = o.GetString();
                                if (!string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out var g)) winnerId = g;
                            }
                        }
                    }
                    catch { }
                }

                var evFallback = new GameEvent { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Type = "legal_ruling", Location = d.Location, PayloadJson = JsonSerializer.Serialize(new { winner = winnerId, reason = "fallback_random" }) };
                await dispatcher.EnqueueAsync(evFallback);
                metrics.Increment("legal.rulings.fallback");
            }
            catch
            {
                // log and continue
                metrics.Increment("legal.errors");
            }
        }

    }
}
