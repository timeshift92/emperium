using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Imperium.Api.Services;
using Imperium.Infrastructure;
using Imperium.Llm;
using Imperium.Domain.Models;

namespace Imperium.Tests
{
    internal class FakeLlmClient : ILlmClient
    {
        private readonly string _response;
        public FakeLlmClient(string response) => _response = response;
        public Task<string> SendPromptAsync(string prompt, CancellationToken ct = default) => Task.FromResult(_response);
    }

    internal class FakeDispatcher : Imperium.Domain.Services.IEventDispatcher
    {
        public readonly System.Collections.Concurrent.ConcurrentBag<GameEvent> Events = new();
        public ValueTask EnqueueAsync(GameEvent e)
        {
            Events.Add(e);
            return ValueTask.CompletedTask;
        }
    }

    public class NpcReplyQueueTests
    {
        [Fact]
        public async Task EnqueueProcessesAndCreatesGameEvent()
        {
            var services = new ServiceCollection();
            var dbName = "testdb_npc" + Guid.NewGuid();
            var options = new DbContextOptionsBuilder<ImperiumDbContext>().UseInMemoryDatabase(dbName).Options;
            services.AddSingleton(options);
            services.AddScoped(sp => new ImperiumDbContext(options));
            var fakeLlm = new FakeLlmClient("{\"reply\": \"Здрасте\", \"moodDelta\": 2}");
            services.AddSingleton<ILlmClient>(fakeLlm);
            var fakeDispatcher = new FakeDispatcher();
            services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(fakeDispatcher);
            services.AddSingleton<NpcReplyQueueService>();
            services.AddSingleton<INpcReplyQueue>(sp => sp.GetRequiredService<NpcReplyQueueService>());
            // required dependencies for NpcReplyQueueService
            services.AddLogging();

            var sp = services.BuildServiceProvider();
            // seed db and capture id
            Guid seededId;
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
                var ch = new Domain.Models.Character { Id = Guid.NewGuid(), Name = "Тест", Age = 30, LocationName = "loc" };
                db.Characters.Add(ch);
                await db.SaveChangesAsync();
                seededId = ch.Id;
            }

            var svc = sp.GetRequiredService<NpcReplyQueueService>();

            // verify seeded character is visible from a fresh scope
            using (var verifyScope = sp.CreateScope())
            {
                var db2 = verifyScope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
                var exists = await db2.Characters.AnyAsync(c => c.Id == seededId);
                Assert.True(exists, "Seeded character must exist in DB before processing");
            }

            // directly process the request synchronously for test determinism
            var req = new NpcReplyRequest(seededId, "крестьянин", CancellationToken.None);
            var processed = await svc.ProcessRequestAsync(req, CancellationToken.None);
            Assert.True(processed, "Expected the queue to be processed");
            Assert.True(fakeDispatcher.Events.Any(), "Expected at least one GameEvent enqueued");
        }
    }
}
