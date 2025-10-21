using System.Text.Json;
using Imperium.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System;
using Imperium.Llm;

namespace Imperium.Infrastructure.Setup;

public static class NatureGenesisService
{
    public static async Task InitializeAsync(ImperiumDbContext db, IServiceProvider services, CancellationToken ct = default)
    {
        // skip if already seeded
        if (await db.EconomySnapshots.AnyAsync(ct) || await db.WeatherSnapshots.AnyAsync(ct))
            return;

        Console.WriteLine("[NatureGenesis] Инициализация биомов и фауны...");

        // 1) Biomes and per-location assignment
        var possibleBiomes = new[] { "forest", "plain", "mountain", "coast" };
        var biomeFauna = new Dictionary<string, Dictionary<string, int>>
        {
            ["forest"] = new() { ["deer"] = 120, ["wolf"] = 30, ["boar"] = 70 },
            ["plain"] = new() { ["hare"] = 200, ["fox"] = 40 },
            ["mountain"] = new() { ["goat"] = 90, ["bear"] = 8 },
            ["coast"] = new() { ["fish"] = 300, ["crab"] = 40 }
        };

        // assign biomes to existing locations (if any) to seed diversity
        var locs = await db.Locations.ToListAsync(ct);
        var rnd = new Random();
        foreach (var l in locs)
        {
            // simple rule: if near coast (Longitude > 16) mark as coast, else random
            if (!string.IsNullOrWhiteSpace(l.Culture) && l.Culture.Contains("tribal", StringComparison.OrdinalIgnoreCase))
            {
                l.Biome = possibleBiomes[rnd.Next(possibleBiomes.Length)];
            }
            else
            {
                if (l.Longitude.HasValue && l.Longitude.Value > 16.0) l.Biome = "coast"; else l.Biome = possibleBiomes[rnd.Next(possibleBiomes.Length)];
            }
        }
        if (locs.Count > 0) db.Locations.UpdateRange(locs);

        var globalResources = new Dictionary<string, object>
        {
            ["wood"] = 6000,
            ["stone"] = 1500,
            ["fish"] = 4000,
            ["grain"] = 2800
        };

        db.EconomySnapshots.Add(new EconomySnapshot
        {
            Timestamp = DateTime.UtcNow,
            ResourcesJson = JsonSerializer.Serialize(globalResources),
            PricesJson = JsonSerializer.Serialize(new { wood = 1.0, fish = 0.9, grain = 0.85, stone = 1.5 }),
            TaxesJson = JsonSerializer.Serialize(new { market = 0.05 }),
            Treasury = 500m
        });

        db.WeatherSnapshots.Add(new WeatherSnapshot
        {
            Timestamp = DateTime.UtcNow,
            TemperatureC = 15,
            WindKph = 5,
            PrecipitationMm = 1.1,
            DayLengthHours = 12
        });

        // Ask LLM to generate a structured creation myth (epoch_name, creation_myth, tribal_origins[])
        var mythJson = new Dictionary<string, object?> { ["epoch_name"] = "I Эпоха", ["creation_myth"] = "Природа пробудилась после сотворения мира." };
        try
        {
            var llm = services?.GetService(typeof(Imperium.Llm.ILlmClient)) as Imperium.Llm.ILlmClient;
            if (llm != null)
            {
                var prompt = "Сгенерируй короткий структурированный миф о сотворении мира и происхождении племён (русский). Возврати JSON {epoch_name, creation_myth, tribal_origins:[{name, origin}]}, не более 250 символов для полей.";
                var raw = await llm.SendPromptAsync(prompt, ct);
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        var doc = JsonDocument.Parse(raw);
                        var root = doc.RootElement;
                        var parsed = new Dictionary<string, object?>();
                        if (root.TryGetProperty("epoch_name", out var en)) parsed["epoch_name"] = en.GetString();
                        if (root.TryGetProperty("creation_myth", out var cm)) parsed["creation_myth"] = cm.GetString();
                        if (root.TryGetProperty("tribal_origins", out var to) && to.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<Dictionary<string, string>>();
                            foreach (var el in to.EnumerateArray())
                            {
                                var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                                var origin = el.TryGetProperty("origin", out var o) ? o.GetString() ?? string.Empty : string.Empty;
                                if (!string.IsNullOrWhiteSpace(name)) list.Add(new Dictionary<string, string> { ["name"] = name, ["origin"] = origin });
                            }
                            parsed["tribal_origins"] = list;
                        }
                        mythJson = parsed;
                    }
                    catch { /* ignore parse errors */ }
                }
            }
        }
        catch { /* ignore LLM errors */ }

        // Persist myth as a WorldChronicle and as an event
        db.WorldChronicles.Add(new WorldChronicle { Year = 1, Summary = JsonSerializer.Serialize(mythJson) });
        db.GameEvents.Add(new GameEvent
        {
            Timestamp = DateTime.UtcNow,
            Type = "world_myth",
            Location = "global",
            PayloadJson = JsonSerializer.Serialize(mythJson)
        });

        await db.SaveChangesAsync(ct);
        Console.WriteLine("[NatureGenesis] Природа и ресурсы созданы.");
    }
}
