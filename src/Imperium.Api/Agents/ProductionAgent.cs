using Imperium.Domain.Agents;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Imperium.Api.Agents;

public class ProductionAgent : IWorldAgent
{
    public string Name => "ProductionAI";

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var metrics = scopeServices.GetRequiredService<Imperium.Api.MetricsService>();
        var eco = scopeServices.GetService<Microsoft.Extensions.Options.IOptions<Imperium.Api.EconomyOptions>>()?.Value
                  ?? new Imperium.Api.EconomyOptions();

        // Use last weather snapshot to modulate output
        var lastWeather = await db.WeatherSnapshots.OrderByDescending(w => w.Timestamp).FirstOrDefaultAsync(ct);
        var precip = lastWeather?.PrecipitationMm ?? 2.0;
        // simple factor: drought <1 -> 0.6, heavy >5 -> 1.2
        var factor = precip < 1 ? 0.6m : (precip > 5 ? 1.2m : 1.0m);

        // For each location, pick few characters and add per-item production
        var locations = await db.Locations.ToListAsync(ct);
        if (locations.Count == 0)
        {
            // no explicit locations — produce globally for few characters
            var chars = await db.Characters.OrderBy(c => EF.Functions.Random()).Take(10).ToListAsync(ct);
            foreach (var ch in chars)
            {
                foreach (var item in eco.Items)
                {
                    var basePer = eco.ProductionPerTick.TryGetValue(item, out var v) ? v : 0.5m;
                    var qty = Math.Max(0.1m, Math.Round(basePer * factor + (decimal)(Random.Shared.NextDouble() * 0.5), 2));
                    var inv = await db.Inventories.FirstOrDefaultAsync(i => i.OwnerId == ch.Id && i.Item == item && i.LocationId == null, ct);
                    if (inv == null)
                    {
                        inv = new Domain.Models.Inventory { Id = Guid.NewGuid(), OwnerId = ch.Id, OwnerType = "character", LocationId = null, Item = item, Quantity = 0m };
                        db.Inventories.Add(inv);
                    }
                    inv.Quantity += qty;
                }
            }
        }
        else
        {
            foreach (var loc in locations)
            {
                var chars = await db.Characters.Where(c => c.LocationName == loc.Name).OrderBy(c => EF.Functions.Random()).Take(6).ToListAsync(ct);
                foreach (var ch in chars)
                {
                    foreach (var item in eco.Items)
                    {
                        var basePer = eco.ProductionPerTick.TryGetValue(item, out var v) ? v : 0.5m;
                        var qty = Math.Max(0.1m, Math.Round(basePer * factor + (decimal)(Random.Shared.NextDouble() * 0.5), 2));
                        var inv = await db.Inventories.FirstOrDefaultAsync(i => i.OwnerId == ch.Id && i.Item == item && i.LocationId == loc.Id, ct);
                        if (inv == null)
                        {
                            inv = new Domain.Models.Inventory { Id = Guid.NewGuid(), OwnerId = ch.Id, OwnerType = "character", LocationId = loc.Id, Item = item, Quantity = 0m };
                            db.Inventories.Add(inv);
                        }
                        inv.Quantity += qty;
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
        metrics.Increment("economy.production.ticks");
    }
}
