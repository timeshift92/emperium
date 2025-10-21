using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Imperium.Api.Services;
using Imperium.Infrastructure;
using Imperium.Llm;
using Imperium.Domain.Models;

namespace Imperium.Tests.Smoke
{
    public class Smoke1000TicksTests
    {
    [Fact(Skip = "Long-running smoke test; enable locally")]
        public async Task Run1000TicksWithMockLlm()
        {
            var services = new ServiceCollection();
            var dbPath = $"smoke_{Guid.NewGuid()}.db";
            services.AddDbContext<ImperiumDbContext>(opts => opts.UseSqlite($"Data Source={dbPath}"));
            services.AddSingleton<Imperium.Api.MetricsService>();
            services.AddSingleton<Imperium.Api.EventStreamService>();
            services.AddLogging();
            // Mock LLM that returns a simple JSON reply each time
            services.AddSingleton<ILlmClient>(new Imperium.Llm.MockLlmClient("{\"reply\":\"Да\",\"moodDelta\":0}"));
            services.AddSingleton<Imperium.Api.EventDispatcherService>();
            services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(sp => sp.GetRequiredService<Imperium.Api.EventDispatcherService>());
            services.AddSingleton<Imperium.Api.Services.NpcReplyQueueService>();
            services.AddSingleton<Imperium.Api.Services.INpcReplyQueue>(sp => sp.GetRequiredService<Imperium.Api.Services.NpcReplyQueueService>());
            services.AddSingleton<Imperium.Api.NpcReactionOptions>();
            // register agents as in Program
            services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.TimeAgent>();
            services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.NpcAgent>();
            // tick worker
            services.AddSingleton<Imperium.Api.TickWorker>();

            var sp = services.BuildServiceProvider();
            // start background services
            var dispatcherSvc = sp.GetRequiredService<Imperium.Api.EventDispatcherService>();
            await dispatcherSvc.StartAsync(CancellationToken.None);
            var queueSvc = sp.GetRequiredService<Imperium.Api.Services.NpcReplyQueueService>();
            await queueSvc.StartAsync(CancellationToken.None);

            // ensure db
            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
                db.Database.EnsureCreated();
                // seed simple characters
                db.Characters.AddRange(
                    new Character { Id = Guid.NewGuid(), Name = "Smoke1", Age = 30, LocationName = "loc" },
                    new Character { Id = Guid.NewGuid(), Name = "Smoke2", Age = 40, LocationName = "loc" }
                );
                db.SaveChanges();
            }

            var worker = sp.GetRequiredService<Imperium.Api.TickWorker>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++)
            {
                await worker.TickOnceAsync();
            }
            sw.Stop();

            var m = sp.GetRequiredService<Imperium.Api.MetricsService>();
            var snapshot = m.Snapshot();
            Console.WriteLine($"1000 ticks took {sw.ElapsedMilliseconds} ms. Metrics snapshot: {string.Join(',', snapshot.Select(kv => kv.Key + '=' + kv.Value))}");
            // stop background services
            await dispatcherSvc.StopAsync(CancellationToken.None);
            await queueSvc.StopAsync(CancellationToken.None);
            Assert.True(sw.ElapsedMilliseconds < 60_000, "Smoke took too long");
        }
    }
}
