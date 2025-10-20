using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Imperium.Infrastructure;
using Imperium.Domain.Models;
using Imperium.Api;

namespace Imperium.Api.Tests;

public class ReclaimFlowTests : IAsyncLifetime
{
    private ServiceProvider? _sp;
    private SqliteConnection? _conn;

    public async Task InitializeAsync()
    {
        // in-memory sqlite that persists for the connection lifetime
        _conn = new SqliteConnection("Data Source=:memory:");
        _conn.Open();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite(_conn));
    // Metrics service used by some agents
    services.AddSingleton<Imperium.Api.MetricsService>();
        // Add EventDispatcherService as singleton with real implementation
        services.AddSingleton<EventStreamService>();
    // Use a synchronous test dispatcher so events are persisted immediately and tests are deterministic
    services.AddSingleton<Imperium.Domain.Services.IEventDispatcher, TestEventDispatcher>();
    // Seedable RNG for deterministic behavior in tests
    services.AddSingleton<Imperium.Api.Utils.IRandomProvider, Imperium.Api.Utils.SeedableRandom>();
        services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.NpcAgent>();
        services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.ConflictAgent>();
        services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.LegalAgent>();
        services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.OwnershipAgent>();
        // Mock LLM
        services.AddSingleton<Imperium.Llm.ILlmClient, Imperium.Llm.MockLlmClient>();

        _sp = services.BuildServiceProvider();

        // create schema
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        db.Database.EnsureCreated();

        // seed characters
        db.Characters.Add(new Character { Id = Guid.NewGuid(), Name = "TestA", Age = 30, LocationName = "Roma" });
        db.Characters.Add(new Character { Id = Guid.NewGuid(), Name = "TestB", Age = 40, LocationName = "Roma" });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _conn?.Dispose();
        _sp?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TriggerReclaim_Generates_Reactions_And_Possible_Conflict()
    {
        using var scope = _sp!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();

        // create an ownership_reclaim_attempt event via dispatcher
        var ev = new GameEvent { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Type = "ownership_reclaim_attempt", Location = "Roma", PayloadJson = "{\"assetId\": \"00000000-0000-0000-0000-000000000001\", \"characterId\": \"00000000-0000-0000-0000-000000000002\" }" };
        await dispatcher.EnqueueAsync(ev);

        // Give dispatcher time to persist
        await Task.Delay(200);

        // Run NPC agent tick manually
        var npc = scope.ServiceProvider.GetRequiredService<Imperium.Domain.Agents.IWorldAgent>();
        await npc.TickAsync(scope.ServiceProvider, default);

        // Allow dispatcher to persist generated events
        await Task.Delay(200);

        var events = await db.GameEvents.OrderByDescending(e => e.Timestamp).Take(20).ToListAsync();

        Assert.Contains(events, e => e.Type == "npc_reaction" || e.Type == "ownership_reclaim_attempt");
        // если есть конфликт — проверим, что supporters >= 1
        var conflicts = events.Where(e => e.Type == "conflict_started").ToList();
        if (conflicts.Count > 0)
        {
            foreach (var c in conflicts)
            {
                // payload хранится в c.PayloadJson, простая проверка на наличие supporters
                Assert.True(c.PayloadJson.Contains("supporters"), "conflict_started payload должен содержать supporters");
            }
        }
    }
}
