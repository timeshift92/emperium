using Imperium.Domain.Agents;
using Imperium.Infrastructure;
using Imperium.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Imperium.Api.Agents;

public class EconomyAgent : IWorldAgent
{
    public string Name => "EconomyAI";

    // simple reactive economics: adjust grain price based on recent precipitation
    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var metrics = scopeServices.GetRequiredService<Imperium.Api.MetricsService>();
        var stream = scopeServices.GetRequiredService<Imperium.Api.EventStreamService>();

        // look at last 20 weather snapshots
        var snaps = await db.WeatherSnapshots.OrderByDescending(s => s.Timestamp).Take(20).ToListAsync(ct);
        if (snaps.Count == 0) return;

        var avgPrecip = snaps.Average(s => s.PrecipitationMm);

        // base prices (global baseline)
        var globalPrices = new Dictionary<string, decimal>
        {
            ["grain"] = 10m,
            ["wine"] = 15m,
            ["oil"] = 8m
        };

        // adjust grain price: drought (avg < 1) -> +30%, heavy rain (>5) -> -10%
        if (avgPrecip < 1.0)
        {
            globalPrices["grain"] = Math.Round(globalPrices["grain"] * 1.3m, 2);
        }
        else if (avgPrecip > 5.0)
        {
            globalPrices["grain"] = Math.Round(globalPrices["grain"] * 0.9m, 2);
        }

        // Create per-location price snapshots (if locations exist)
        var locations = await db.Locations.ToListAsync(ct);
        var perLocationPrices = new Dictionary<Guid, Dictionary<string, decimal>>();
        if (locations.Count == 0)
        {
            // simulate a single global location
            perLocationPrices[Guid.Empty] = globalPrices.ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        else
        {
            foreach (var loc in locations)
            {
                // small variation per location
                var locPrices = globalPrices.ToDictionary(kv => kv.Key, kv => kv.Value);
                var rand = Random.Shared;
                // apply small random variation and weather effect
                locPrices["grain"] = Math.Round(locPrices["grain"] * (1m + (decimal)((avgPrecip - 3) / 30.0) + (decimal)(rand.NextDouble() - 0.5) * 0.1m), 2);
                perLocationPrices[loc.Id] = locPrices;
            }
        }

        var snapshot = new EconomySnapshot
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            PricesJson = JsonSerializer.Serialize(perLocationPrices),
            Treasury = 1000m
        };
        db.EconomySnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);
        metrics.Increment("economy.snapshots");
        var dispatcher = scopeServices.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();
        var ev = new GameEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Type = "economy_snapshot",
            Location = "global",
            PayloadJson = JsonSerializer.Serialize(new { snapshotId = snapshot.Id, avgPrecip, descriptionRu = "Снимок экономики: цены по регионам" })
        };
        _ = dispatcher.EnqueueAsync(ev);

        // Influence NPCs: mark some characters unhappy if grain price high
    var characters = await db.Characters.OrderBy(c => c.Name).Take(10).ToListAsync(ct);
        if (characters.Any())
        {
            var grainPriceGlobal = perLocationPrices.Values.First().GetValueOrDefault("grain", 10m);
            foreach (var ch in characters)
            {
                if (grainPriceGlobal > 12)
                {
                    ch.Status = "unhappy";
                }
                else if (grainPriceGlobal < 9)
                {
                    ch.Status = "content";
                }
            }
            await db.SaveChangesAsync(ct);
        }

        // Transport: if price diff between locations large enough, create transport job event
        if (perLocationPrices.Count > 1)
        {
            var maxPrice = perLocationPrices.Values.Max(p => p.GetValueOrDefault("grain", 0m));
            var minPrice = perLocationPrices.Values.Min(p => p.GetValueOrDefault("grain", 0m));
            if (maxPrice - minPrice > 3.0m)
            {
                var tj = new GameEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    Type = "transport_job",
                    Location = "global",
                    PayloadJson = JsonSerializer.Serialize(new { from = perLocationPrices.First().Key, to = perLocationPrices.Last().Key, profit = maxPrice - minPrice, descriptionRu = "Транспортная задача: перевезти зерно для получения прибыли" })
                };
                _ = dispatcher.EnqueueAsync(tj);
            }
        }
    }
}
