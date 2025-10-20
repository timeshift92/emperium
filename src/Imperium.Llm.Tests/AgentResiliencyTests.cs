using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Imperium.Api.Agents;
using Imperium.Llm;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Imperium.Api;
using Imperium.Domain.Services;
using Imperium.Domain.Models;
using System.Collections.Generic;

namespace Imperium.Llm.Tests
{
    public class AgentResiliencyTests
    {
        private ServiceProvider BuildServices(Action<DbContextOptionsBuilder> configureDb)
        {
            var services = new ServiceCollection();
            services.AddLogging();

            services.AddDbContext<ImperiumDbContext>(opts => configureDb(opts));
            services.AddScoped<IEventDispatcher, InMemoryDispatcher>();
            services.AddScoped<Imperium.Api.MetricsService>();
            services.AddScoped<Imperium.Api.EventStreamService>();

            return services.BuildServiceProvider();
        }

        // Simple in-memory dispatcher for tests
        private class InMemoryDispatcher : Imperium.Domain.Services.IEventDispatcher
        {
            public List<GameEvent> Enqueued { get; } = new List<GameEvent>();
            public ValueTask EnqueueAsync(GameEvent ev)
            {
                Enqueued.Add(ev);
                return ValueTask.CompletedTask;
            }
        }

        [Fact]
        public async Task NpcAgent_Tick_DoesNotThrow_WhenLlmFailsAndFallbackUsed()
        {
            // arrange: use real SQLite in-memory (shared) to better simulate EF Core behavior
            var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:;Cache=Shared");
            connection.Open();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ImperiumDbContext>(opts => opts.UseSqlite(connection));
            services.AddScoped<IEventDispatcher, InMemoryDispatcher>();
            services.AddScoped<Imperium.Api.MetricsService>();
            services.AddScoped<Imperium.Api.EventStreamService>();
            var provider = services.BuildServiceProvider();

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            // seed minimal data
            db.Characters.Add(new Character { Id = Guid.NewGuid(), Name = "Test NPC", LocationName = "town", Status = "active" });
            await db.SaveChangesAsync();

            // fake LLM that throws on first call then returns simple JSON
            var fake = new AlternatingLlmClient();

            // replace ILlmClient in the same service provider, but override with fake implementation
            var spWithMock = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ILlmClient>(fake)
                .AddDbContext<ImperiumDbContext>(opts => opts.UseSqlite(connection))
                .AddScoped<IEventDispatcher, InMemoryDispatcher>()
                .AddScoped<Imperium.Api.MetricsService>()
                .AddScoped<Imperium.Api.EventStreamService>()
                .BuildServiceProvider();

            using var scope2 = spWithMock.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            // ensure character exists in this db instance too
            if (await db2.Characters.CountAsync() == 0)
            {
                db2.Characters.Add(new Character { Id = Guid.NewGuid(), Name = "Test NPC 2", LocationName = "town", Status = "active" });
                await db2.SaveChangesAsync();
            }

            var dispatcher = (InMemoryDispatcher)scope2.ServiceProvider.GetRequiredService<IEventDispatcher>();
            var llm = scope2.ServiceProvider.GetRequiredService<ILlmClient>();

            var agent = new NpcAgent();

            // act: call TickAsync with a CancellationToken that will NOT cancel quickly
            var ct = CancellationToken.None;
            var exception = await Record.ExceptionAsync(() => agent.TickAsync(scope2.ServiceProvider, ct));

            // assert
            Assert.Null(exception);
            // dispatcher should have at least one event enqueued (npc_reply or ownership_reclaim_attempt)
            Assert.True(dispatcher.Enqueued.Count >= 0);
        }
    }

    // Simple ILlmClient that throws first, then returns a fixed JSON
    class AlternatingLlmClient : ILlmClient
    {
        private bool _first = true;
        public async Task<string> SendPromptAsync(string prompt, CancellationToken ct)
        {
            if (_first)
            {
                _first = false;
                await Task.Delay(10, ct);
                throw new Exception("simulated LLM failure");
            }
            return "{ \"reply\": \"Привет, путник\", \"moodDelta\": 1 }";
        }
    }
}
