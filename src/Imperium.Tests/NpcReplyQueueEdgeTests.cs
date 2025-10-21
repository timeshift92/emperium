using System;
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
    internal class SeqLlmClient : ILlmClient
    {
        private readonly string[] _responses;
        private int _i = 0;
        public SeqLlmClient(params string[] responses) => _responses = responses;
        public Task<string> SendPromptAsync(string prompt, CancellationToken ct = default)
        {
            var idx = Math.Min(_i, _responses.Length - 1);
            _i++;
            return Task.FromResult(_responses[idx]);
        }
    }

    public class NpcReplyQueueEdgeTests
    {
        [Fact]
        public async Task ReaskPath_IncrementsReaskCounter()
        {
            var services = new ServiceCollection();
            var dbName = "seq_reask" + Guid.NewGuid();
            var options = new DbContextOptionsBuilder<ImperiumDbContext>().UseInMemoryDatabase(dbName).Options;
            services.AddSingleton(options);
            services.AddScoped(sp => new ImperiumDbContext(options));
            // LLM returns technical content first (should reask), then valid JSON
            var seq = new SeqLlmClient("http://example.com code", "{\"reply\":\"Привет\",\"moodDelta\":1}");
            services.AddSingleton<ILlmClient>(seq);
            var fakeDispatcher = new FakeDispatcher();
            services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(fakeDispatcher);
            services.AddSingleton<NpcReplyQueueService>();
            services.AddSingleton<INpcReplyQueue>(sp => sp.GetRequiredService<NpcReplyQueueService>());
            services.AddLogging();
            services.AddSingleton<Imperium.Api.MetricsService>();

            var sp = services.BuildServiceProvider();
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
                var ch = new Character { Id = Guid.NewGuid(), Name = "R", Age = 20, LocationName = "loc" };
                db.Characters.Add(ch);
                await db.SaveChangesAsync();
            }

            var svc = sp.GetRequiredService<NpcReplyQueueService>();
            var metrics = sp.GetRequiredService<Imperium.Api.MetricsService>();
            var req = new NpcReplyRequest(Guid.NewGuid(), "крестьянин", CancellationToken.None);
            // fix id to seeded
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
                req = new NpcReplyRequest(await db.Characters.Select(c => c.Id).FirstAsync(), "крестьянин", CancellationToken.None);
            }

            var ok = await svc.ProcessRequestAsync(req);
            Assert.True(ok);
            var snapshot = metrics.Snapshot();
            Assert.True(snapshot.ContainsKey("npc.queue.reasks"));
            Assert.True(snapshot["npc.queue.reasks"] >= 1);
        }

        [Fact]
        public async Task SanitizePath_CleansForbiddenToken()
        {
            var services = new ServiceCollection();
            var dbName = "seq_sanitize" + Guid.NewGuid();
            var options = new DbContextOptionsBuilder<ImperiumDbContext>().UseInMemoryDatabase(dbName).Options;
            services.AddSingleton(options);
            services.AddScoped(sp => new ImperiumDbContext(options));
            // LLM returns JSON but reply contains forbidden token
            var seq = new SeqLlmClient("{\"reply\":\"Посетите github\",\"moodDelta\":0}");
            services.AddSingleton<ILlmClient>(seq);
            var fakeDispatcher = new FakeDispatcher();
            services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(fakeDispatcher);
            services.AddSingleton<NpcReplyQueueService>();
            services.AddSingleton<INpcReplyQueue>(sp => sp.GetRequiredService<NpcReplyQueueService>());
            services.AddLogging();
            services.AddSingleton<Imperium.Api.MetricsService>();

            var sp = services.BuildServiceProvider();
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
                db.Characters.Add(new Character { Id = Guid.NewGuid(), Name = "S", Age = 22, LocationName = "loc" });
                await db.SaveChangesAsync();
            }

            var svc = sp.GetRequiredService<NpcReplyQueueService>();
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
                var ch = await db.Characters.FirstAsync();
                var ok = await svc.ProcessRequestAsync(new NpcReplyRequest(ch.Id, "крестьянин", CancellationToken.None));
                Assert.True(ok);
            }

            var ev = fakeDispatcher.Events.ToArray();
            Assert.NotEmpty(ev);
            Assert.DoesNotContain("github", ev[0].PayloadJson, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task NoReplyPath_FallsBack()
        {
            var services = new ServiceCollection();
            var dbName = "seq_empty" + Guid.NewGuid();
            var options = new DbContextOptionsBuilder<ImperiumDbContext>().UseInMemoryDatabase(dbName).Options;
            services.AddSingleton(options);
            services.AddScoped(sp => new ImperiumDbContext(options));
            // LLM returns empty strings
            var seq = new SeqLlmClient("","","");
            services.AddSingleton<ILlmClient>(seq);
            var fakeDispatcher = new FakeDispatcher();
            services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(fakeDispatcher);
            services.AddSingleton<NpcReplyQueueService>();
            services.AddSingleton<INpcReplyQueue>(sp => sp.GetRequiredService<NpcReplyQueueService>());
            services.AddLogging();
            services.AddSingleton<Imperium.Api.MetricsService>();

            var sp = services.BuildServiceProvider();
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
                db.Characters.Add(new Character { Id = Guid.NewGuid(), Name = "X", Age = 22, LocationName = "loc" });
                await db.SaveChangesAsync();
            }

            var svc = sp.GetRequiredService<NpcReplyQueueService>();
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
                var ch = await db.Characters.FirstAsync();
                var ok = await svc.ProcessRequestAsync(new NpcReplyRequest(ch.Id, "крестьянин", CancellationToken.None));
                Assert.True(ok);
            }

            var ev = fakeDispatcher.Events.ToArray();
            Assert.NotEmpty(ev);
            var json = System.Text.Json.JsonDocument.Parse(ev[0].PayloadJson);
            var reply = json.RootElement.GetProperty("reply").GetString();
            Assert.Equal("(нет ответа)", reply);
        }
    }
}
