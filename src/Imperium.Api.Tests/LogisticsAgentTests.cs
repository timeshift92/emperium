using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Imperium.Api.Agents;
using Imperium.Api.Services;
using Imperium.Domain.Models;
using Imperium.Domain.Services;
using Imperium.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Imperium.Api.Tests;

public class LogisticsAgentTests
{
    private static ServiceProvider BuildProvider(SqliteConnection conn, LogisticsQueueService queue)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ImperiumDbContext>(opts => opts.UseSqlite(conn));
        services.AddSingleton<IEventDispatcher, TestEventDispatcher>();
        services.AddSingleton<Imperium.Api.MetricsService>();
        services.AddSingleton(queue);
        // Register EconomyStateService used by LogisticsQueueService
        services.AddSingleton(new Imperium.Api.EconomyStateService(new[] { "grain" }));
        services.AddScoped<LogisticsAgent>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task LogisticsAgent_CompletesJob_WhenTreasuryAvailable()
    {
        using var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared");
        conn.Open();

    var queue = new LogisticsQueueService(Options.Create(new LogisticsOptions { BaseCostPerUnit = 0.1m, DistanceMultiplier = 0m }), new Imperium.Api.EconomyStateService(new[] { "grain" }));
        var provider = BuildProvider(conn, queue);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        await db.Database.EnsureCreatedAsync();

        var from = new Location { Id = Guid.NewGuid(), Name = "Roma", Treasury = 50m };
        var to = new Location { Id = Guid.NewGuid(), Name = "Sicilia", Treasury = 10m };
        db.Locations.AddRange(from, to);
        await db.SaveChangesAsync();

        var job = queue.Enqueue(from.Id, to.Id, "grain", 10m, 6m);

        var agent = scope.ServiceProvider.GetRequiredService<LogisticsAgent>();
        await agent.TickAsync(scope.ServiceProvider, CancellationToken.None);

        var updatedFrom = await db.Locations.FindAsync(from.Id);
        var updatedTo = await db.Locations.FindAsync(to.Id);
        Assert.NotNull(updatedFrom);
        Assert.NotNull(updatedTo);
        Assert.True(updatedFrom!.Treasury < 50m);
        Assert.True(updatedTo!.Treasury > 10m);

        var snapshot = queue.Snapshot().First(j => j.Id == job.Id);
        Assert.Equal(LogisticsJobStatus.Completed, snapshot.Status);
        Assert.NotNull(snapshot.CompletedAt);

        var events = await db.GameEvents.Where(e => e.Type == "logistics_completed").ToListAsync();
        Assert.Single(events);
    }

    [Fact]
    public async Task LogisticsAgent_Waits_WhenTreasuryInsufficient()
    {
        using var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared");
        conn.Open();

    var queue = new LogisticsQueueService(Options.Create(new LogisticsOptions { BaseCostPerUnit = 1m, DistanceMultiplier = 0m }), new Imperium.Api.EconomyStateService(new[] { "grain" }));
        var provider = BuildProvider(conn, queue);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        await db.Database.EnsureCreatedAsync();

        var from = new Location { Id = Guid.NewGuid(), Name = "Roma", Treasury = 0.5m };
        db.Locations.Add(from);
        await db.SaveChangesAsync();

        var job = queue.Enqueue(from.Id, null, "grain", 20m, 5m);

        var agent = scope.ServiceProvider.GetRequiredService<LogisticsAgent>();
        await agent.TickAsync(scope.ServiceProvider, CancellationToken.None);

        var updated = await db.Locations.FindAsync(from.Id);
        Assert.NotNull(updated);
        Assert.Equal(0.5m, updated!.Treasury); // не списали

        var snapshot = queue.Snapshot().First(j => j.Id == job.Id);
        Assert.Equal(LogisticsJobStatus.WaitingFunds, snapshot.Status);
        Assert.True(snapshot.NextAttemptAt > DateTime.UtcNow);

        var events = await db.GameEvents.Where(e => e.Type == "logistics_completed").ToListAsync();
        Assert.Empty(events);
    }
}
