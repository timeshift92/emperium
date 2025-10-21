using Imperium.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Imperium.Infrastructure.Setup;

public static class CivilizationGenesisService
{
    public static async Task InitializeAsync(ImperiumDbContext db, CancellationToken ct = default)
    {
        // Avoid duplicate creation beyond the desired number (we want up to 3 city states total)
        var existingCount = await db.Factions.CountAsync(f => f.Type == "city_state", ct);
        var toCreate = Math.Max(0, 3 - existingCount);
        if (toCreate == 0)
        {
            return;
        }

    // Select candidate tribes: prefer those with locations (and higher population)
    var tribes = await db.Factions.Where(f => f.Type == "tribe").ToListAsync(ct);
        if (tribes.Count == 0)
        {
            return; // nothing to transform
        }

    // Choose up to 'toCreate' tribes to promote
    var selected = tribes.Take(toCreate).ToList();
        var createdCities = new List<string>();

    foreach (var t in selected)
        {
            // Create or find a location related by name (best-effort)
            var loc = await db.Locations.FirstOrDefaultAsync(l => l.Name.Contains(t.Name.Split(' ').LastOrDefault() ?? t.Name), ct)
                ?? await db.Locations.FirstOrDefaultAsync(l => l.Name == t.Name, ct)
                ?? await db.Locations.FirstOrDefaultAsync(ct);

            if (loc != null)
            {
                // Increase population
                loc.Population += Math.Max(200, loc.Population / 10);
            }

            // Create a city-state faction
            var cityName = t.Name.Replace("Племя", "Город").Replace("племя", "Город");
            if (string.IsNullOrWhiteSpace(cityName)) cityName = t.Name + " Город";

            var city = new Faction { Id = Guid.NewGuid(), Name = cityName, Type = "city_state", LocationId = loc?.Id };
            db.Factions.Add(city);

            // Add richer economy snapshot: reserves, prices, taxes and initial trade routes
            var reserves = new Dictionary<string, decimal> { { "grain", 200m }, { "metal", 30m }, { "wood", 120m } };
            var prices = new Dictionary<string, decimal> { { "grain", 1.0m }, { "metal", 5.0m }, { "wood", 0.8m } };
            var taxes = new Dictionary<string, decimal> { { "grain", 0.10m }, { "market_fee", 0.02m } };
            // initial trade routes: connect to nearest neighbor locations if available
            var locId = loc?.Id;
            var available = await db.Locations.Where(l => l.Id != locId).ToListAsync(ct);
            var routes = new List<Guid>();
            if (available.Count > 0)
            {
                // choose up to 2 nearest by simple coordinate distance (if coordinates available)
                var candidates = available.Where(a => a.Latitude.HasValue && a.Longitude.HasValue).ToList();
                if (candidates.Count > 0 && loc?.Latitude.HasValue == true && loc.Longitude.HasValue)
                {
                    var sorted = candidates.OrderBy(a => Math.Pow((a.Latitude!.Value - loc.Latitude!.Value), 2) + Math.Pow((a.Longitude!.Value - loc.Longitude!.Value), 2)).Take(2).Select(x => x.Id);
                    routes.AddRange(sorted);
                }
                else
                {
                    routes.Add(available[0].Id);
                }
            }
            else if (loc != null)
            {
                // Fallback: create a self-route to represent internal trade logistics
                routes.Add(loc.Id);
            }

            var econ = new EconomySnapshot
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                ResourcesJson = JsonSerializer.Serialize(reserves),
                PricesJson = JsonSerializer.Serialize(prices),
                TaxesJson = JsonSerializer.Serialize(taxes),
                Treasury = 120m
            };
            db.EconomySnapshots.Add(econ);

            // Persist trade routes as first-class entities
            foreach (var toId in routes)
            {
                db.TradeRoutes.Add(new TradeRoute
                {
                    Id = Guid.NewGuid(),
                    FromLocationId = loc!.Id,
                    ToLocationId = toId,
                    OwnerFactionId = city.Id,
                    Toll = 0.02m,
                    Transport = "caravan"
                });
            }

            // Buildings
            var buildings = new[] { "market", "forge", "temple", "walls" };
            foreach (var b in buildings)
            {
                db.Buildings.Add(new Building { Id = Guid.NewGuid(), LocationId = loc?.Id, Kind = b });
            }

            // Characters: Governor, High Priest, Master Artisan
            var governor = new Character { Id = Guid.NewGuid(), Name = $"Губернатор {cityName}", LocationName = loc?.Name ?? "", Status = "ruler" };
            var priest = new Character { Id = Guid.NewGuid(), Name = $"Верховный Жрец {cityName}", LocationName = loc?.Name ?? "", Status = "priest" };
            var artisan = new Character { Id = Guid.NewGuid(), Name = $"Мастери {cityName}", LocationName = loc?.Name ?? "", Status = "artisan" };
            db.Characters.AddRange(governor, priest, artisan);

            // Knowledge fields
            var fields = new[] { "Письменность", "Гончарное дело", "Архитектура" };
            foreach (var f in fields)
            {
                if (!await db.KnowledgeFields.AnyAsync(k => k.Name == f, ct))
                {
                    db.KnowledgeFields.Add(new KnowledgeField { Id = Guid.NewGuid(), Name = f });
                }
            }

            // Rumors
            db.Rumors.Add(new Rumor { Id = Guid.NewGuid(), Content = $"Говорят, кузнецы {cityName} нашли секрет закалки железа." });
            db.Rumors.Add(new Rumor { Id = Guid.NewGuid(), Content = $"Поговорка гласит, что на рынке {cityName} лучшие гончары в округе." });

            createdCities.Add(cityName);
        }

        // Chronicle
        var worldYear = await db.WorldTimes.Select(w => w.Year).FirstOrDefaultAsync(ct);
        var chron = new WorldChronicle
        {
            Id = Guid.NewGuid(),
            Year = worldYear > 0 ? worldYear : 1,
            Summary = "Начало эпохи городов. Племена преобразились, воздвигли стены и начали ремесла и торговлю."
        };
        db.WorldChronicles.Add(chron);

        await db.SaveChangesAsync(ct);

        Console.WriteLine("Эпоха ранних цивилизаций успешно создана.");
    }
}
