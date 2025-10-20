using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Imperium.Infrastructure;
using Imperium.Domain.Models;
using Imperium.Api;
using ReclaimRunner;

Console.WriteLine("Starting reclaim-runner...\n");

var conn = new SqliteConnection("Data Source=:memory:");
conn.Open();

var services = new ServiceCollection();
services.AddLogging();
services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite(conn));
services.AddSingleton<EventStreamService>();
// Use a synchronous TestEventDispatcher in the runner for deterministic tests
services.AddSingleton<Imperium.Domain.Services.IEventDispatcher, ReclaimRunner.TestEventDispatcher>();
// small metrics service used by agents
services.AddSingleton<Imperium.Api.MetricsService>();
services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.NpcAgent>();
services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.ConflictAgent>();
services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.LegalAgent>();
services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.OwnershipAgent>();
services.AddSingleton<Imperium.Llm.ILlmClient, Imperium.Llm.MockLlmClient>();

using var sp = services.BuildServiceProvider();

using (var scope = sp.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
    db.Database.EnsureCreated();

    // seed characters
    var a = new Character { Id = Guid.NewGuid(), Name = "RunnerA", Age = 30, LocationName = "Roma" };
    var b = new Character { Id = Guid.NewGuid(), Name = "RunnerB", Age = 40, LocationName = "Roma" };
    db.Characters.AddRange(a, b);
    db.SaveChanges();

    // seed NpcMemory with high attachment/greed for RunnerA to increase chance of reaction
    db.NpcMemories.Add(new Imperium.Domain.Models.NpcMemory { Id = Guid.NewGuid(), CharacterId = a.Id, Attachment = 0.9, Greed = 0.4, LastUpdated = DateTime.UtcNow, KnownAssets = new System.Collections.Generic.List<Guid>() });
    await db.SaveChangesAsync();

    var dispatcher = scope.ServiceProvider.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();

    // create initial reclaim
    var reclaim = new GameEvent { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Type = "ownership_reclaim_attempt", Location = "Roma", PayloadJson = "{\"assetId\": \"00000000-0000-0000-0000-000000000010\", \"characterId\": \"00000000-0000-0000-0000-000000000020\" }" };
    dispatcher.EnqueueAsync(reclaim).GetAwaiter().GetResult();

    // Give dispatcher a moment
    Task.Delay(200).GetAwaiter().GetResult();

    // Run multiple ticks to increase chance of reactions
    var agents = scope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>().ToList();
    for (int i = 0; i < 5; i++)
    {
        foreach (var agent in agents)
        {
            agent.TickAsync(scope.ServiceProvider, default).GetAwaiter().GetResult();
        }
        // allow dispatcher to persist events between ticks
        Task.Delay(500).GetAwaiter().GetResult();
    }

    var events = db.GameEvents.OrderByDescending(e => e.Timestamp).Take(50).ToList();
    Console.WriteLine($"Events persisted: {events.Count}\n");
    foreach (var e in events)
    {
        Console.WriteLine($"{e.Timestamp:O} {e.Type} {e.Location} {e.PayloadJson}");
    }
}

Console.WriteLine("Done.");
return 0;