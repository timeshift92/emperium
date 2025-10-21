using Imperium.Domain.Agents;
using Imperium.Infrastructure;
using Imperium.Domain.Models;
using Imperium.Api.Services;
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
        var logisticsQueue = scopeServices.GetRequiredService<LogisticsQueueService>();

        // look at last 20 weather snapshots
    var snaps = await db.WeatherSnapshots.OrderByDescending(s => s.Timestamp).Take(20).ToListAsync();
        if (snaps.Count == 0) return;

        var avgPrecip = snaps.Average(s => s.PrecipitationMm);

        // base prices (global baseline)
        var eco = scopeServices.GetService<Microsoft.Extensions.Options.IOptions<Imperium.Api.EconomyOptions>>()?.Value
                  ?? new Imperium.Api.EconomyOptions();
        var state = scopeServices.GetRequiredService<Imperium.Api.EconomyStateService>();
        var items = state.GetItems();
        var globalPrices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            var def = state.GetDefinition(it);
            globalPrices[it] = def?.BasePrice ?? (it switch { "grain" => 10m, "wine" => 15m, "oil" => 8m, _ => 5m });
        }

        // adjust grain price: drought (avg < 1) -> +30%, heavy rain (>5) -> -10%
        if (globalPrices.ContainsKey("grain"))
        {
            if (avgPrecip < 1.0) globalPrices["grain"] = Math.Round(globalPrices["grain"] * 1.3m, 2);
            else if (avgPrecip > 5.0) globalPrices["grain"] = Math.Round(globalPrices["grain"] * 0.9m, 2);
        }

        // Create per-location price snapshots (if locations exist)
    var locations = await db.Locations.ToListAsync();
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
                // small variation per location + dynamic shocks
                var locPrices = globalPrices.ToDictionary(kv => kv.Key, kv => kv.Value);
                var rand = Random.Shared;
                // apply small random variation and weather effect for grain
                if (locPrices.ContainsKey("grain"))
                    locPrices["grain"] = Math.Round(locPrices["grain"] * (1m + (decimal)((avgPrecip - 3) / 30.0) + (decimal)(rand.NextDouble() - 0.5) * 0.1m), 2);
                // small independent variation for other items
                foreach (var k in locPrices.Keys.ToList())
                {
                    if (k == "grain") continue;
                    locPrices[k] = Math.Round(locPrices[k] * (1m + (decimal)(rand.NextDouble() - 0.5) * 0.08m), 2);
                }
                // apply shocks
                foreach (var k in locPrices.Keys.ToList())
                {
                    var mul = state.GetEffectiveMultiplier(k);
                    if (mul != 1m) locPrices[k] = Math.Round(locPrices[k] * mul, 2);
                }
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
    await db.SaveChangesAsync();
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
    var characters = await db.Characters.OrderBy(c => c.Name).Take(10).ToListAsync();
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
            await db.SaveChangesAsync();
        }

        if (perLocationPrices.Count > 1)
        {
            var priced = perLocationPrices
                .Where(kv => kv.Key != Guid.Empty)
                .Select(kv => new { LocationId = kv.Key, Price = kv.Value.GetValueOrDefault("grain", 0m) })
                .Where(x => x.Price > 0m)
                .ToList();
            if (priced.Count >= 2)
            {
                var maxEntry = priced.OrderByDescending(x => x.Price).First();
                var minEntry = priced.OrderBy(x => x.Price).First();
                var spread = maxEntry.Price - minEntry.Price;
                if (spread > 3.0m)
                {
                    var job = logisticsQueue.Enqueue(minEntry.LocationId, maxEntry.LocationId, "grain", volume: 15m, expectedProfit: spread);
                    metrics.Increment("logistics.jobs.enqueued");
                    var tj = new GameEvent
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = DateTime.UtcNow,
                        Type = "transport_job",
                        Location = "global",
                        PayloadJson = JsonSerializer.Serialize(new
                        {
                            jobId = job.Id,
                            from = job.FromLocationId,
                            to = job.ToLocationId,
                            item = job.Item,
                            volume = job.Volume,
                            profit = job.ExpectedProfit,
                            descriptionRu = "Транспортная задача: перевезти товар для получения прибыли"
                        })
                    };
                    _ = dispatcher.EnqueueAsync(tj);
                }
            }
        }

        // --- Simple order flow and matching (per location, item=grain) ---
        foreach (var kv in perLocationPrices)
        {
            var locId = kv.Key == Guid.Empty ? (Guid?)null : kv.Key;
            // iterate items
            foreach (var item in eco.Items)
            {
                var basePrice = kv.Value.GetValueOrDefault(item, 10m);
            var rand = Random.Shared;

            // Ensure some baseline inventory for random sellers (pick few characters)
            var chars = await db.Characters.OrderBy(c => EF.Functions.Random()).Take(5).ToListAsync(ct);
            foreach (var ch in chars)
            {
                var inv = await db.Inventories.FirstOrDefaultAsync(i => i.OwnerId == ch.Id && i.Item == item && i.LocationId == (locId ?? i.LocationId), ct);
                if (inv == null)
                {
                    inv = new Imperium.Domain.Models.Inventory { Id = Guid.NewGuid(), OwnerId = ch.Id, OwnerType = "character", LocationId = locId, Item = item, Quantity = rand.Next(5, 25) };
                    db.Inventories.Add(inv);
                }
            }
            await db.SaveChangesAsync(ct);

            // Place a few random orders
            for (int i = 0; i < 3; i++)
            {
                var buyer = chars[rand.Next(chars.Count)].Id;
                var seller = chars[rand.Next(chars.Count)].Id;

                var buy = new Imperium.Domain.Models.MarketOrder
                {
                    Id = Guid.NewGuid(), OwnerId = buyer, OwnerType = "character", LocationId = locId, Item = item, Side = "buy",
                    Price = Math.Round(basePrice * (1m + (decimal)(rand.NextDouble() * 0.05)), 2), Quantity = rand.Next(2, 6), Remaining = 0, Status = "open"
                };
                buy.Remaining = buy.Quantity;
                db.MarketOrders.Add(buy);

                var sellQty = rand.Next(2, 6);
                var sell = new Imperium.Domain.Models.MarketOrder
                {
                    Id = Guid.NewGuid(), OwnerId = seller, OwnerType = "character", LocationId = locId, Item = item, Side = "sell",
                    Price = Math.Round(basePrice * (1m - (decimal)(rand.NextDouble() * 0.05)), 2), Quantity = sellQty, Remaining = sellQty, Status = "open"
                };
                db.MarketOrders.Add(sell);
            }
            await db.SaveChangesAsync(ct);

            // Ensure at least one crossing pair per location (guaranteed trade opportunity)
            if (chars.Count >= 2)
            {
                var buyer = chars[0].Id;
                var seller = chars[1].Id;

                // top bid slightly above base, ask slightly below base, quantities aligned
                var gBid = new Imperium.Domain.Models.MarketOrder
                {
                    Id = Guid.NewGuid(), OwnerId = buyer, OwnerType = "character", LocationId = locId, Item = item, Side = "buy",
                    Price = Math.Round(basePrice * 1.02m, 2), Quantity = 5m, Remaining = 5m, Status = "open"
                };
                db.MarketOrders.Add(gBid);

                // ensure seller has inventory
                var sInv = await db.Inventories.FirstOrDefaultAsync(i => i.OwnerId == seller && i.Item == item && i.LocationId == (locId ?? i.LocationId), ct);
                if (sInv == null)
                {
                    sInv = new Imperium.Domain.Models.Inventory { Id = Guid.NewGuid(), OwnerId = seller, OwnerType = "character", LocationId = locId, Item = item, Quantity = 10m };
                    db.Inventories.Add(sInv);
                }
                else if (sInv.Quantity < 5m)
                {
                    sInv.Quantity = 5m;
                }

                var gAsk = new Imperium.Domain.Models.MarketOrder
                {
                    Id = Guid.NewGuid(), OwnerId = seller, OwnerType = "character", LocationId = locId, Item = item, Side = "sell",
                    Price = Math.Round(basePrice * 0.98m, 2), Quantity = 5m, Remaining = 5m, Status = "open"
                };
                db.MarketOrders.Add(gAsk);

                await db.SaveChangesAsync(ct);
            }

            // Match orders (best bid vs best ask)
            while (true)
            {
                var bids = await db.MarketOrders.Where(o => o.LocationId == locId && o.Item == item && o.Side == "buy" && o.Status == "open")
                    .ToListAsync(ct);
                var asks = await db.MarketOrders.Where(o => o.LocationId == locId && o.Item == item && o.Side == "sell" && o.Status == "open")
                    .ToListAsync(ct);

                // SQLite can have issues ordering by decimal on the server side; order on client side
                var bestBid = bids.OrderByDescending(o => o.Price).ThenBy(o => o.CreatedAt).FirstOrDefault();
                var bestAsk = asks.OrderBy(o => o.Price).ThenBy(o => o.CreatedAt).FirstOrDefault();
                if (bestBid == null || bestAsk == null) break;
                if (bestBid.Price < bestAsk.Price) break;

                // Seller must have inventory
                var sellerInv = await db.Inventories.FirstOrDefaultAsync(i => i.OwnerId == bestAsk.OwnerId && i.Item == item && i.LocationId == (locId ?? i.LocationId), ct);
                var available = sellerInv?.Quantity ?? 0m;
                if (available <= 0)
                {
                    bestAsk.Status = "cancelled";
                    await db.SaveChangesAsync(ct);
                    continue;
                }

                var tradeQty = Math.Min(bestBid.Remaining, Math.Min(bestAsk.Remaining, available));
                if (tradeQty <= 0) break;

                var price = Math.Round((bestBid.Price + bestAsk.Price) / 2m, 2);

                // Enforce buyer funds
                var buyerChar = await db.Characters.FindAsync(new object?[] { bestBid.OwnerId }, ct);
                var sellerChar = await db.Characters.FindAsync(new object?[] { bestAsk.OwnerId }, ct);
                var buyerHouse = buyerChar == null ? await db.Households.FindAsync(new object?[] { bestBid.OwnerId }, ct) : null;
                var sellerHouse = sellerChar == null ? await db.Households.FindAsync(new object?[] { bestAsk.OwnerId }, ct) : null;
                var affordableQty = tradeQty;
                // Prefer reserved funds from bid; top up from wallet if needed
                var availableFunds = bestBid.ReservedFunds + (buyerChar?.Money ?? buyerHouse?.Wealth ?? 0m);
                var maxQtyByFunds = price > 0 ? Math.Floor((availableFunds / price) * 100m) / 100m : tradeQty;
                if (maxQtyByFunds <= 0)
                {
                    bestBid.Status = "cancelled";
                    // release reserved funds
                    if (bestBid.ReservedFunds > 0 && buyerChar != null) { buyerChar.Money += bestBid.ReservedFunds; bestBid.ReservedFunds = 0; }
                    await db.SaveChangesAsync(ct);
                    continue;
                }
                affordableQty = Math.Min(affordableQty, maxQtyByFunds);

                // Apply final quantity
                bestBid.Remaining -= affordableQty;
                bestAsk.Remaining -= affordableQty;
                if (bestBid.Remaining <= 0)
                {
                    bestBid.Status = "filled";
                    metrics?.Add("economy.orders.active", -1);
                    metrics?.Increment("economy.orders.filled");
                }
                else
                {
                    bestBid.Status = "partial";
                }
                if (bestAsk.Remaining <= 0)
                {
                    bestAsk.Status = "filled";
                    metrics?.Add("economy.orders.active", -1);
                    metrics?.Increment("economy.orders.filled");
                }
                else
                {
                    bestAsk.Status = "partial";
                }

                // Move inventory seller -> buyer
                // For reserved qty on ask, inventory is already reduced in order placement; adjust remaining reservation
                if (bestAsk.ReservedQty > 0)
                {
                    bestAsk.ReservedQty -= affordableQty;
                    if (bestAsk.ReservedQty < 0) bestAsk.ReservedQty = 0;
                }
                else
                {
                    sellerInv!.Quantity -= affordableQty;
                }
                var buyerInv = await db.Inventories.FirstOrDefaultAsync(i => i.OwnerId == bestBid.OwnerId && i.Item == item && i.LocationId == (locId ?? i.LocationId), ct);
                if (buyerInv == null)
                {
                    buyerInv = new Imperium.Domain.Models.Inventory { Id = Guid.NewGuid(), OwnerId = bestBid.OwnerId, OwnerType = "character", LocationId = locId, Item = item, Quantity = 0 };
                    db.Inventories.Add(buyerInv);
                }
                buyerInv.Quantity += affordableQty;

                // Transfer funds buyer -> seller
                var total = Math.Round(price * affordableQty, 2);
                // Deduct from reserved first, then wallet
                var fromReserved = Math.Min(total, bestBid.ReservedFunds);
                bestBid.ReservedFunds -= fromReserved;
                var remain = total - fromReserved;
                if (remain > 0)
                {
                    if (buyerChar != null) buyerChar.Money -= remain; else if (buyerHouse != null) buyerHouse.Wealth -= remain;
                }
                if (sellerChar != null) sellerChar.Money += total; else if (sellerHouse != null) sellerHouse.Wealth += total;

                // simple trade fee → city treasury (1%)
                var fee = Math.Round(total * 0.01m, 2);
                if (fee > 0)
                {
                    if (buyerChar != null) buyerChar.Money -= fee; else if (buyerHouse != null) buyerHouse.Wealth -= fee;
                    // credit to location treasury if present
                    if (locId.HasValue)
                    {
                        var city = await db.Locations.FindAsync(new object?[] { locId.Value }, ct);
                        if (city != null) city.Treasury += fee;
                    }
                }

                var trade = new Imperium.Domain.Models.Trade
                {
                    Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, LocationId = locId, Item = item, Price = price, Quantity = affordableQty,
                    BuyOrderId = bestBid.Id, SellOrderId = bestAsk.Id, BuyerId = bestBid.OwnerId, SellerId = bestAsk.OwnerId
                };
                db.Trades.Add(trade);
                await db.SaveChangesAsync(ct);

                // Audit event
                var tradeEv = new GameEvent
                {
                    Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Type = "trade_executed", Location = locId?.ToString() ?? "global",
                    PayloadJson = JsonSerializer.Serialize(new { item, price, qty = affordableQty, bid = bestBid.Id, ask = bestAsk.Id, buyer = bestBid.OwnerId, seller = bestAsk.OwnerId })
                };
                _ = dispatcher.EnqueueAsync(tradeEv);
                metrics.Increment("economy.trades");
            }
            }

            // Sweep expired orders: refund reserved funds/qty
            var now = DateTime.UtcNow;
            var expired = await db.MarketOrders.Where(o => o.LocationId == locId && o.Status == "open" && o.ExpiresAt != null && o.ExpiresAt < now).ToListAsync(ct);
            var cancelledCount = 0;
            foreach (var ord in expired)
            {
                ord.Status = "cancelled";
                ord.UpdatedAt = now;
                if (ord.Side == "buy" && ord.ReservedFunds > 0)
                {
                    var buyer = await db.Characters.FindAsync(new object?[] { ord.OwnerId }, ct);
                    if (buyer != null) buyer.Money += ord.ReservedFunds;
                    ord.ReservedFunds = 0;
                }
                if (ord.Side == "sell" && ord.ReservedQty > 0)
                {
                    var inv = await db.Inventories.FirstOrDefaultAsync(i => i.OwnerId == ord.OwnerId && i.OwnerType == ord.OwnerType && i.Item == ord.Item && i.LocationId == (locId ?? i.LocationId), ct);
                    if (inv == null)
                    {
                        inv = new Imperium.Domain.Models.Inventory { Id = Guid.NewGuid(), OwnerId = ord.OwnerId, OwnerType = ord.OwnerType, LocationId = locId, Item = ord.Item, Quantity = 0 };
                        db.Inventories.Add(inv);
                    }
                    inv.Quantity += ord.ReservedQty;
                    ord.ReservedQty = 0;
                }
                cancelledCount++;
            }
            if (cancelledCount > 0)
            {
                await db.SaveChangesAsync(ct);
                metrics?.Add("economy.orders.active", -cancelledCount);
                metrics?.Add("economy.orders.cancelled", cancelledCount);
            }
        }
    }
}
