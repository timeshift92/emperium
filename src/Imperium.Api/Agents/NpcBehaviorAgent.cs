using System.Text.Json;
using Imperium.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Imperium.Domain.Models;

namespace Imperium.Api.Agents;

public class NpcBehaviorAgent : Imperium.Domain.Agents.IWorldAgent
{
    public string Name => "NpcBehaviorAI";
    private const int MaxNpcPerTick = 8;
    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<Imperium.Infrastructure.ImperiumDbContext>();
        var llm = scopeServices.GetRequiredService<ILlmClient>();
        var dispatcher = scopeServices.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();

        var worldTime = await db.WorldTimes.FirstOrDefaultAsync(ct);
        var weather = await db.WeatherSnapshots.OrderByDescending(w => w.Timestamp).FirstOrDefaultAsync(ct);

        var candidates = await db.NpcEssences
            .Join(db.Characters, ne => ne.CharacterId, c => c.Id, (ne, c) => new { Essence = ne, Character = c })
            .Where(x => x.Character.Status == "ok")
            .OrderByDescending(x => (x.Essence.Motivation * x.Essence.Energy))
            .Take(MaxNpcPerTick)
            .ToListAsync(ct);

        foreach (var pair in candidates)
        {
            if (ct.IsCancellationRequested) break;
            var essence = pair.Essence;
            var ch = pair.Character;

            if (worldTime != null)
            {
                if (worldTime.Hour >= 6 && worldTime.Hour < 12) essence.Energy = Math.Min(1.0, essence.Energy + 0.1);
                else if (worldTime.Hour >= 20) essence.Energy = Math.Max(0.0, essence.Energy - 0.2);
            }

            if (essence.Energy <= 0.3 || essence.Motivation <= 0.2) continue;

            var promptObj = new Dictionary<string, object?>
            {
                ["npc_name"] = ch.Name,
                ["location"] = ch.LocationName ?? "неизвестно",
                ["hour"] = worldTime?.Hour ?? 0,
                ["weather"] = weather?.Condition ?? "неизвестно",
                ["temperatureC"] = weather?.TemperatureC,
                ["mood"] = essence.Mood
            };

            var prompt = BuildPrompt(promptObj);

            string? responseText = null;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(6));
                responseText = await llm.SendPromptAsync(prompt, cts.Token);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(responseText)) continue;

            try
            {
                using var doc = JsonDocument.Parse(responseText);
                var root = doc.RootElement;
                var action = root.GetProperty("action").GetString() ?? "действует";
                var emotion = root.TryGetProperty("emotion", out var em) ? em.GetString() : null;
                var energyDelta = root.TryGetProperty("energyDelta", out var ed) && ed.ValueKind == JsonValueKind.Number ? ed.GetDouble() : 0.0;
                var motivationDelta = root.TryGetProperty("motivationDelta", out var md) && md.ValueKind == JsonValueKind.Number ? md.GetDouble() : 0.0;

                essence.LastAction = action;
                essence.Mood = emotion ?? essence.Mood;
                essence.Energy = Math.Clamp(essence.Energy + energyDelta, 0.0, 1.0);
                essence.Motivation = Math.Clamp(essence.Motivation + motivationDelta, 0.0, 1.0);

                db.NpcEssences.Update(essence);
                await db.SaveChangesAsync(ct);

                var ev = new GameEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    Type = "npc_action",
                    Location = ch.LocationName ?? "unknown",
                    PayloadJson = JsonSerializer.Serialize(new { characterId = ch.Id, action = action, mood = essence.Mood })
                };
                await dispatcher.EnqueueAsync(ev);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static string BuildPrompt(Dictionary<string, object?> ctx)
    {
        var contextJson = JsonSerializer.Serialize(ctx);
        return $"[role:NpcAI]\nДай в компактном JSON следующую структуру: {{ \"action\": string, \"emotion\": string, \"energyDelta\": number (optional), \"motivationDelta\": number (optional) }}.\nКонтекст: {contextJson}.\nПиши на русском, кириллица, кратко.";
    }
}
