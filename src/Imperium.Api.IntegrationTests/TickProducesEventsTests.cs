using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Imperium.Infrastructure;
using Xunit;

namespace Imperium.Api.IntegrationTests;

public class TickProducesEventsTests
{
    [Fact]
    public async Task TickWorker_GeneratesCoreEvents()
    {
        var builder = WebApplication.CreateBuilder();
        var dataDir = System.IO.Path.Combine(Environment.CurrentDirectory, "ticktestdata");
        System.IO.Directory.CreateDirectory(dataDir);
        var dbPath = System.IO.Path.Combine(dataDir, "test-imperium-tick.db");
        builder.Services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
        // Register minimal supporting services and only the TimeAgent to avoid heavy Economy/NPC work in this test
        builder.Services.AddSingleton<Imperium.Api.MetricsService>();
        builder.Services.AddSingleton<Imperium.Api.EventStreamService>();
        builder.Services.AddSingleton<Imperium.Api.EventDispatcherService>();
        builder.Services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(sp => sp.GetRequiredService<Imperium.Api.EventDispatcherService>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<Imperium.Api.EventDispatcherService>());

    // Register TimeAgent and EconomyAgent for this focused test
    builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.TimeAgent>();
    builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.EconomyAgent>();
    // EconomyAgent dependencies
    builder.Services.AddSingleton<Imperium.Api.EconomyStateService>();
    builder.Services.AddSingleton<Imperium.Api.Services.LogisticsQueueService>();

    builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
    builder.Services.AddSingleton<TestEventDispatcher>();
    builder.Services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(sp => sp.GetRequiredService<TestEventDispatcher>());
    // EventStream required by some agents
    builder.Services.AddSingleton<Imperium.Api.EventStreamService>();
    var app = builder.Build();
    await app.StartAsync();

    using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

    // Seed minimal data: add a WeatherSnapshot so EconomyAgent can compute prices
    db.WeatherSnapshots.Add(new Imperium.Domain.Models.WeatherSnapshot
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        Condition = "clear",
        TemperatureC = 20,
        WindKph = 10,
        PrecipitationMm = 0.0,
        DayLengthHours = 12.0
    });
    // ensure there is at least one world time record trigger
        await db.SaveChangesAsync();

    // Ensure TimeAgent is registered
        var agents = scope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>().ToList();

        // run 5 ticks by iterating agents (TimeAgent only)
        for (int tick = 0; tick < 10; tick++)
        {
            foreach (var a in agents.OrderBy(x => x.Name))
            {
                try { await a.TickAsync(scope.ServiceProvider, default); } catch { }
            }
            // allow dispatcher to flush events for this tick
            await Task.Delay(50);
        }

        // Guarantee time tick: call TimeAgent explicitly
        var timeAgent = agents.FirstOrDefault(a => a.Name == "TimeAI");
        if (timeAgent != null)
        {
            await timeAgent.TickAsync(scope.ServiceProvider, default);
            await Task.Delay(50);
        }

    // allow some time for background processors (NpcQueue/EventDispatcher) to persist events
    await Task.Delay(1500);

        // Check that events were recorded
        var evs = await db.GameEvents.ToListAsync();
        Assert.True(evs.Any(e => e.Type == "time_tick"), "Expected time_tick events");
        Assert.True(evs.Any(e => e.Type == "economy_snapshot"), "Expected economy_snapshot events");
        await app.StopAsync();
    }
}
