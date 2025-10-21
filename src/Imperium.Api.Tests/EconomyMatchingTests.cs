using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Imperium.Api;
using Imperium.Api.Agents;
using Imperium.Domain.Models;
using Imperium.Domain.Services;
using Imperium.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Imperium.Api.Tests;

public class EconomyMatchingTests
{
    private static ServiceProvider BuildProvider(SqliteConnection conn)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ImperiumDbContext>(opts => opts.UseSqlite(conn));
        services.AddSingleton<IEventDispatcher, TestEventDispatcher>();
        services.AddSingleton<MetricsService>();
        services.AddSingleton<EventStreamService>();
        services.AddSingleton(new EconomyStateService(new[] { "grain" }));
        services.Configure<EconomyOptions>(opts =>
        {
            opts.Items = new[] { "grain" };
        });
        services.AddScoped<EconomyAgent>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EconomyAgent_MatchesOrders_EmitsTradeEvent()
    {
        using var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared");
        conn.Open();
        var provider = BuildProvider(conn);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        await db.Database.EnsureCreatedAsync();

        var location = new Location { Id = Guid.NewGuid(), Name = "Roma", Latitude = 0.3, Longitude = 0.4, Treasury = 10m };
        db.Locations.Add(location);
        db.WeatherSnapshots.Add(new WeatherSnapshot
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Condition = "clear",
            TemperatureC = 24,
            WindKph = 5,
            PrecipitationMm = 2
        });

        var buyer = new Character { Id = Guid.NewGuid(), Name = "Buyer", Money = 150m };
        var seller = new Character { Id = Guid.NewGuid(), Name = "Seller", Money = 10m };
        db.Characters.AddRange(buyer, seller);

        db.Inventories.Add(new Inventory
        {
            Id = Guid.NewGuid(),
            OwnerId = seller.Id,
            OwnerType = "character",
            Item = "grain",
            Quantity = 20m,
            LocationId = location.Id
        });

        var now = DateTime.UtcNow;
        var buyOrder = new MarketOrder
        {
            Id = Guid.NewGuid(),
            OwnerId = buyer.Id,
            OwnerType = "character",
            LocationId = location.Id,
            Item = "grain",
            Side = "buy",
            Price = 10m,
            Quantity = 5m,
            Remaining = 5m,
            ReservedFunds = 50m,
            Status = "open",
            CreatedAt = now,
            UpdatedAt = now
        };
        var sellOrder = new MarketOrder
        {
            Id = Guid.NewGuid(),
            OwnerId = seller.Id,
            OwnerType = "character",
            LocationId = location.Id,
            Item = "grain",
            Side = "sell",
            Price = 9m,
            Quantity = 5m,
            Remaining = 5m,
            ReservedQty = 5m,
            Status = "open",
            CreatedAt = now,
            UpdatedAt = now
        };

        db.MarketOrders.AddRange(buyOrder, sellOrder);
        await db.SaveChangesAsync();

        var agent = scope.ServiceProvider.GetRequiredService<EconomyAgent>();
        await agent.TickAsync(scope.ServiceProvider, CancellationToken.None);

        await db.Entry(buyOrder).ReloadAsync();
        await db.Entry(sellOrder).ReloadAsync();

        Assert.Equal("filled", buyOrder.Status);
        Assert.Equal("filled", sellOrder.Status);
        Assert.Equal(0m, buyOrder.Remaining);
        Assert.Equal(0m, sellOrder.Remaining);
        Assert.Equal(0m, buyOrder.ReservedFunds);
        Assert.Equal(0m, sellOrder.ReservedQty);

        var updatedBuyer = await db.Characters.FindAsync(buyer.Id);
        var updatedSeller = await db.Characters.FindAsync(seller.Id);
        Assert.NotNull(updatedBuyer);
        Assert.NotNull(updatedSeller);
        Assert.True(updatedBuyer!.Money <= 150m);
        Assert.True(updatedSeller!.Money > 10m);

        var trades = await db.Trades.ToListAsync();
        Assert.Single(trades);
        Assert.Equal(5m, trades[0].Quantity);

        var tradeEvents = await db.GameEvents.Where(e => e.Type == "trade_executed").ToListAsync();
        Assert.Single(tradeEvents);

        var payload = JsonSerializer.Deserialize<JsonElement>(tradeEvents[0].PayloadJson);
        Assert.Equal(buyOrder.Id, payload.GetProperty("bid").GetGuid());
        Assert.Equal(sellOrder.Id, payload.GetProperty("ask").GetGuid());

        var updatedLocation = await db.Locations.FindAsync(location.Id);
        Assert.NotNull(updatedLocation);
        Assert.True(updatedLocation!.Treasury > 10m);
    }

    [Fact]
    public async Task EconomyAgent_ExpiresOrders_RefundsReservations()
    {
        using var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared");
        conn.Open();
        var provider = BuildProvider(conn);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        await db.Database.EnsureCreatedAsync();

        var location = new Location { Id = Guid.NewGuid(), Name = "Athenae", Treasury = 5m };
        db.Locations.Add(location);
        db.WeatherSnapshots.Add(new WeatherSnapshot { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Condition = "clear", TemperatureC = 20, WindKph = 2, PrecipitationMm = 1 });

        var buyer = new Character { Id = Guid.NewGuid(), Name = "Patron", Money = 0m };
        var seller = new Character { Id = Guid.NewGuid(), Name = "Trader", Money = 0m };
        db.Characters.AddRange(buyer, seller);

        db.Inventories.Add(new Inventory { Id = Guid.NewGuid(), OwnerId = seller.Id, OwnerType = "character", Item = "grain", Quantity = 10m, LocationId = location.Id });
        await db.SaveChangesAsync();

        var past = DateTime.UtcNow.AddMinutes(-30);
        db.MarketOrders.AddRange(
            new MarketOrder
            {
                Id = Guid.NewGuid(),
                OwnerId = buyer.Id,
                OwnerType = "character",
                LocationId = location.Id,
                Item = "grain",
                Side = "buy",
                Price = 5m,
                Quantity = 4m,
                Remaining = 4m,
                ReservedFunds = 20m,
                Status = "open",
                CreatedAt = past,
                UpdatedAt = past,
                ExpiresAt = past
            },
            new MarketOrder
            {
                Id = Guid.NewGuid(),
                OwnerId = seller.Id,
                OwnerType = "character",
                LocationId = location.Id,
                Item = "grain",
                Side = "sell",
                Price = 12m,
                Quantity = 4m,
                Remaining = 4m,
                ReservedQty = 4m,
                Status = "open",
                CreatedAt = past,
                UpdatedAt = past,
                ExpiresAt = past
            }
        );
        await db.SaveChangesAsync();

        var agent = scope.ServiceProvider.GetRequiredService<EconomyAgent>();
        await agent.TickAsync(scope.ServiceProvider, CancellationToken.None);

        var orders = await db.MarketOrders.ToListAsync();
        Assert.All(orders, o => Assert.Equal("cancelled", o.Status));
        Assert.All(orders, o => Assert.Equal(0m, o.ReservedFunds));
        Assert.All(orders, o => Assert.Equal(0m, o.ReservedQty));

        var reloadedBuyer = await db.Characters.FindAsync(buyer.Id);
        var reloadedSeller = await db.Characters.FindAsync(seller.Id);
        Assert.NotNull(reloadedBuyer);
        Assert.NotNull(reloadedSeller);
        Assert.True(reloadedBuyer!.Money >= 20m);

        var sellerInventory = await db.Inventories.FirstAsync(i => i.OwnerId == seller.Id);
        Assert.Equal(10m, sellerInventory.Quantity);
    }
}
