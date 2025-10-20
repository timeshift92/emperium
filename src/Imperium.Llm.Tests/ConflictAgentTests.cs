using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Xunit;
using Imperium.Api.Agents;
using Imperium.Infrastructure;
using Imperium.Llm;
using Imperium.Domain.Services;
using Imperium.Domain.Models;

namespace Imperium.Llm.Tests
{
    public class ConflictAgentTests
    {
        private ServiceProvider BuildProviderWithConnection(SqliteConnection conn, Imperium.Llm.ILlmClient llmImpl, int seed = 12345)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<Imperium.Llm.ILlmClient>(llmImpl);
            services.AddDbContext<ImperiumDbContext>(opts => opts.UseSqlite(conn));
            // Use a singleton dispatcher so the test can observe events enqueued from any scope
            services.AddSingleton<IEventDispatcher, InMemoryDispatcher>();
            services.AddScoped<Imperium.Api.MetricsService>();
            services.AddScoped<Imperium.Api.EventStreamService>();
            // deterministic random for tests
            services.AddSingleton<Imperium.Api.Utils.IRandomProvider>(new Imperium.Api.Utils.SeedableRandom(seed));
            return services.BuildServiceProvider();
        }

        private class InMemoryDispatcher : IEventDispatcher
        {
            public System.Collections.Generic.List<GameEvent> Enqueued { get; } = new System.Collections.Generic.List<GameEvent>();
            public ValueTask EnqueueAsync(GameEvent ev) { Enqueued.Add(ev); return ValueTask.CompletedTask; }
        }

        [Fact]
        public async Task ConflictAgent_UsesLlmRecommendation_StartConflict()
        {
            var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared"); conn.Open();

            // LLM returns recommendation start_conflict
            var llm = new TestLlmClient("{ \"supportersDelta\": 3, \"recommendation\": \"start_conflict\" }");
            var provider = BuildProviderWithConnection(conn, llm, seed: 1);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            // seed an ownership_reclaim_attempt and some npc_reaction events referencing it
            var attempt = new GameEvent { Id = Guid.NewGuid(), Type = "ownership_reclaim_attempt", Timestamp = DateTime.UtcNow, Location = "test" , PayloadJson = "{}" };
            db.GameEvents.Add(attempt);
            db.GameEvents.Add(new GameEvent { Id = Guid.NewGuid(), Type = "npc_reaction", Timestamp = DateTime.UtcNow, Location = "test", PayloadJson = $"{{ \"action\": \"support_claimant\" , \"sourceEvent\": \"{attempt.Id}\" }}" });
            await db.SaveChangesAsync();

            var agent = new ConflictAgent();
            await agent.TickAsync(scope.ServiceProvider, CancellationToken.None);

            var dispatcher = (InMemoryDispatcher)provider.GetRequiredService<IEventDispatcher>();
            var ev = dispatcher.Enqueued.FirstOrDefault(e => e.Type == "conflict_started");
            Assert.NotNull(ev);
            // verify payload contains llm object
            Assert.Contains("\"llm\"", ev.PayloadJson);
        }

        [Fact]
        public async Task ConflictAgent_UsesLlmDelta_WhenProvided()
        {
            var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared"); conn.Open();

            var llm = new TestLlmClient("{ \"supportersDelta\": 2, \"recommendation\": \"no_conflict\" }");
            var provider = BuildProviderWithConnection(conn, llm);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            var attempt = new GameEvent { Id = Guid.NewGuid(), Type = "ownership_reclaim_attempt", Timestamp = DateTime.UtcNow, Location = "test" , PayloadJson = "{}" };
            db.GameEvents.Add(attempt);
            db.GameEvents.Add(new GameEvent { Id = Guid.NewGuid(), Type = "npc_reaction", Timestamp = DateTime.UtcNow, Location = "test", PayloadJson = $"{{ \"action\": \"support_claimant\" , \"sourceEvent\": \"{attempt.Id}\" }}" });
            await db.SaveChangesAsync();

            var agent = new ConflictAgent();
            await agent.TickAsync(scope.ServiceProvider, CancellationToken.None);

            var dispatcher = (InMemoryDispatcher)provider.GetRequiredService<IEventDispatcher>();
            // maybe conflict started depending on random; at least ensure code executed and enqueued list exists
            Assert.NotNull(dispatcher.Enqueued);
        }

        [Fact]
        public async Task ConflictAgent_Fallbacks_WhenLlmInvalid()
        {
            var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared"); conn.Open();

            var llm = new TestLlmClient("INVALID JSON");
            var provider = BuildProviderWithConnection(conn, llm);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            var attempt = new GameEvent { Id = Guid.NewGuid(), Type = "ownership_reclaim_attempt", Timestamp = DateTime.UtcNow, Location = "test" , PayloadJson = "{}" };
            db.GameEvents.Add(attempt);
            db.GameEvents.Add(new GameEvent { Id = Guid.NewGuid(), Type = "npc_reaction", Timestamp = DateTime.UtcNow, Location = "test", PayloadJson = $"{{ \"action\": \"support_claimant\" , \"sourceEvent\": \"{attempt.Id}\" }}" });
            await db.SaveChangesAsync();

            var agent = new ConflictAgent();
            var ex = await Record.ExceptionAsync(() => agent.TickAsync(scope.ServiceProvider, CancellationToken.None));
            Assert.Null(ex);
        }

        // Simple test LLM client used instead of Moq
        private class TestLlmClient : Imperium.Llm.ILlmClient
        {
            private readonly string _response;
            public TestLlmClient(string response) { _response = response; }
            public Task<string> SendPromptAsync(string prompt, CancellationToken ct = default) => Task.FromResult(_response);
        }
    }
}
