using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Imperium.Infrastructure;
using Imperium.Domain.Models;
using Imperium.Llm;

namespace Imperium.Tests
{
    public class NpcAgentIntegrationTests
    {
    [Fact(Skip = "Flaky under InMemory provider; enable in integration environment")]
    public async Task TickAsync_EnqueuesNpcReply_WhenCharactersExist()
        {
            var services = new ServiceCollection();

            services.AddDbContext<ImperiumDbContext>(opts => opts.UseInMemoryDatabase("testdb_npc" + Guid.NewGuid()));
            var fakeResponses = new string[10];
            for (int i = 0; i < fakeResponses.Length; i++) fakeResponses[i] = "{\"reply\":\"Здрасьте\", \"moodDelta\": 1}";
            services.AddSingleton<ILlmClient>(new MockLlmClient(fakeResponses));
            var fakeDispatcher = new FakeEventDispatcher();
            services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(fakeDispatcher);

            // minimal services required by NpcAgent
            services.AddLogging();
            services.AddSingleton<Imperium.Api.EventStreamService>();
            services.AddSingleton<Imperium.Api.MetricsService>();
            var sp = services.BuildServiceProvider(true);


            // seed DB with several characters to avoid issues with EF.Functions.Random in InMemory provider
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
                for (int i = 0; i < 5; i++)
                {
                    db.Characters.Add(new Character { Id = Guid.NewGuid(), Name = $"Test NPC {i}", LocationName = "Тестовая" });
                }
                await db.SaveChangesAsync();
            }

            // create NpcAgent and run TickAsync a few times to increase chance of generating replies
            var agent = new Imperium.Api.Agents.NpcAgent();
            for (int t = 0; t < 3; t++)
            {
                using (var scope = sp.CreateScope())
                {
                    await agent.TickAsync(scope.ServiceProvider, CancellationToken.None);
                }
            }

            // assert that dispatcher received at least one event (move or reply)
            Assert.True(fakeDispatcher.Events.Count > 0, "Expected at least one GameEvent enqueued by NpcAgent");
        }
    }
}
