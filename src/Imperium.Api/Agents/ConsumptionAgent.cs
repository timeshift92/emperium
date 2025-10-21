using Imperium.Domain.Agents;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Imperium.Api.Agents;

public class ConsumptionAgent : IWorldAgent
{
    public string Name => "ConsumptionAI";

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var metrics = scopeServices.GetRequiredService<Imperium.Api.MetricsService>();
        var dispatcher = scopeServices.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();
        var eco = scopeServices.GetService<Microsoft.Extensions.Options.IOptions<Imperium.Api.EconomyOptions>>()?.Value
                  ?? new Imperium.Api.EconomyOptions();
        var state = scopeServices.GetRequiredService<Imperium.Api.EconomyStateService>();

        // Determine simple base price from last economy snapshot (fallback 10)
        decimal basePrice = 10m;
        try
        {
            var snap = await db.EconomySnapshots.OrderByDescending(s => s.Timestamp).FirstOrDefaultAsync(ct);
            if (snap != null)
            {
                // if there is any location price, take the first
                var map = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, decimal>>>(snap.PricesJson ?? "{}")
                          ?? new Dictionary<string, Dictionary<string, decimal>>();
                var p = map.FirstOrDefault().Value;
                if (p != null && p.TryGetValue("grain", out var g)) basePrice = g;
            }
        }
        catch { }

        // Pick some characters and consume items; create orders when low/high
        var chars = await db.Characters.OrderBy(c => EF.Functions.Random()).Take(15).ToListAsync(ct);
        foreach (var ch in chars)
        {
            // Try resolve household of the character (simple string match)
            Imperium.Domain.Models.Household? hh = null;
            try
            {
                var chStr = ch.Id.ToString();
                hh = await db.Households.FirstOrDefaultAsync(h => h.MemberIdsJson.Contains(chStr), ct);
            }
            catch { }
            foreach (var item in state.GetItems())
            {
                var inv = await db.Inventories.Where(i => i.OwnerId == ch.Id && i.Item == item)
                                              .OrderByDescending(i => i.UpdatedAt)
                                              .FirstOrDefaultAsync(ct);
                var locId = inv?.LocationId;
                var def = state.GetDefinition(item);
                decimal baseCons;
                if (def?.ConsumptionPerTick != null)
                {
                    baseCons = def.ConsumptionPerTick;
                }
                else if (eco.ConsumptionPerTick.TryGetValue(item, out var c))
                {
                    baseCons = c;
                }
                else
                {
                    baseCons = 0.5m;
                }
                var consume = baseCons + (decimal)(Random.Shared.NextDouble() * (double)baseCons * 0.25);
                if (inv != null)
                {
                    inv.Quantity -= consume;
                    if (inv.Quantity < 0) inv.Quantity = 0;
                    inv.UpdatedAt = DateTime.UtcNow;
                }

                // If low inventory -> place buy order
                if ((inv?.Quantity ?? 0m) < baseCons)
                {
                    var qty = Math.Round(baseCons * 3m, 2);
                    var buy = new Domain.Models.MarketOrder
                    {
                        Id = Guid.NewGuid(), OwnerId = ch.Id, OwnerType = "character", LocationId = locId, Item = item, Side = "buy",
                        Price = Math.Round(basePrice * 1.05m, 2), Quantity = qty, Remaining = qty, Status = "open", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                        ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                    };
                    // Reserve funds if buyer has money
                    var total = Math.Round(buy.Price * buy.Quantity, 2);
                    var buyer = await db.Characters.FindAsync(new object?[] { ch.Id }, ct);
                    // Prefer household wallet when available
                    if (hh != null && hh.Wealth > 0)
                    {
                        var toReserve = Math.Min(total, hh.Wealth);
                        hh.Wealth -= toReserve;
                        buy.ReservedFunds = toReserve;
                        buy.OwnerId = hh.Id;
                        buy.OwnerType = "household";
                    }
                    else if (buyer != null && buyer.Money > 0)
                    {
                        var toReserve = Math.Min(total, buyer.Money);
                        buyer.Money -= toReserve;
                        buy.ReservedFunds = toReserve;
                    }
                    db.MarketOrders.Add(buy);
                }
                else if (inv!.Quantity > baseCons * 8m)
                {
                    // Surplus -> place sell order
                    var qty = Math.Min(baseCons * 4m, inv.Quantity - baseCons * 5m);
                    if (qty > 0)
                    {
                        var sell = new Domain.Models.MarketOrder
                        {
                            Id = Guid.NewGuid(), OwnerId = ch.Id, OwnerType = "character", LocationId = locId, Item = item, Side = "sell",
                            Price = Math.Round(basePrice * 0.95m, 2), Quantity = qty, Remaining = qty, Status = "open", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                        };
                        // Reserve inventory immediately to avoid double spend
                        if (inv.Quantity >= qty)
                        {
                            inv.Quantity -= qty;
                            sell.ReservedQty = qty;
                        }
                        else if (hh != null)
                        {
                            // Try reserve from household inventory when character lacks own
                            var hInv = await db.Inventories.FirstOrDefaultAsync(i => i.OwnerId == hh.Id && i.OwnerType == "household" && i.Item == item && i.LocationId == (locId ?? i.LocationId), ct);
                            if (hInv != null && hInv.Quantity >= qty)
                            {
                                hInv.Quantity -= qty;
                                sell.ReservedQty = qty;
                                sell.OwnerId = hh.Id;
                                sell.OwnerType = "household";
                            }
                            else
                            {
                                continue; // cannot sell
                            }
                        }
                        else continue;
                        db.MarketOrders.Add(sell);
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
        metrics.Increment("economy.consumption.ticks");
    }
}
