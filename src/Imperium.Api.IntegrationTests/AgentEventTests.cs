using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Imperium.Infrastructure;
using Imperium.Llm;
using System.Threading;
using Imperium.Api.Services;
using Xunit;

namespace Imperium.Api.IntegrationTests
{
    public class AgentEventTests
    {
        [Fact]
        public async Task SeasonAgent_GeneratesSeasonEvent_WithPayload()
        {
            var builder = WebApplication.CreateBuilder();
            var dataDir = System.IO.Path.Combine(Environment.CurrentDirectory, "season-test-data");
            System.IO.Directory.CreateDirectory(dataDir);
            var dbPath = System.IO.Path.Combine(dataDir, "test-season.db");
            builder.Services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
            builder.Services.AddSingleton<Imperium.Api.MetricsService>();
            // Use a synchronous test dispatcher for deterministic behavior
            builder.Services.AddSingleton<TestEventDispatcher>();
            builder.Services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(sp => sp.GetRequiredService<TestEventDispatcher>());

            // Register SeasonAgent
            builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.SeasonAgent>();

            // avoid port collisions in parallel test runs
            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            var app = builder.Build();
            await app.StartAsync();

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            // seed weather snapshots with cold data to force Winter
            for (int i = 0; i < 12; i++)
            {
                db.WeatherSnapshots.Add(new Imperium.Domain.Models.WeatherSnapshot
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow.AddMinutes(-i),
                    Condition = "clear",
                    TemperatureC = -5,
                    WindKph = 5,
                    PrecipitationMm = 0,
                    DayLengthHours = 8
                });
            }
            await db.SaveChangesAsync();

            var agent = scope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>().FirstOrDefault(a => a.Name == "SeasonAI");
            Assert.NotNull(agent);

            await agent!.TickAsync(scope.ServiceProvider, default);

            // no need to wait — TestEventDispatcher persists synchronously

            var evs = await db.GameEvents.ToListAsync();
            var seasonEvent = evs.FirstOrDefault(e => e.Type == "season_set" || e.Type == "season_change");
            Assert.NotNull(seasonEvent);
            // payload contains season and avgTemp
            using var doc = JsonDocument.Parse(seasonEvent!.PayloadJson);
            Assert.True(doc.RootElement.TryGetProperty("season", out var s));
            Assert.True(doc.RootElement.TryGetProperty("avgTemp", out var t));
            Assert.True(t.ValueKind == JsonValueKind.Number);

            await app.StopAsync();
        }

        [Fact]
        public async Task NpcAgent_EnqueuesAndProcessesNpcReply_WithPayload()
        {
            var builder = WebApplication.CreateBuilder();
            var dataDir = System.IO.Path.Combine(Environment.CurrentDirectory, "npc-test-data");
            System.IO.Directory.CreateDirectory(dataDir);
            var dbPath = System.IO.Path.Combine(dataDir, "test-npc.db");
            builder.Services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
            builder.Services.AddSingleton<Imperium.Api.MetricsService>();
            // Use the synchronous test dispatcher for deterministic writes
            builder.Services.AddSingleton<TestEventDispatcher>();
            builder.Services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(sp => sp.GetRequiredService<TestEventDispatcher>());

            // provide real queue service and a mock LLM client
            builder.Services.AddSingleton<INpcReplyQueue, NpcReplyQueueService>();
            builder.Services.AddSingleton<ILlmClient, MockLlmClient>();

            // Register NpcAgent
            builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.NpcAgent>();

            // avoid port collisions in parallel test runs
            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            // event stream service required by NpcAgent
            builder.Services.AddSingleton<Imperium.Api.EventStreamService>();
            var app = builder.Build();
            await app.StartAsync();

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            // create one character
            var ch = new Imperium.Domain.Models.Character
            {
                Id = Guid.NewGuid(),
                Name = "Тестовый NPC",
                Age = 30,
                Status = "ok",
                Money = 10m
            };
            db.Characters.Add(ch);
            await db.SaveChangesAsync();

            var agent = scope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>().FirstOrDefault(a => a.Name == "NpcAI");
            Assert.NotNull(agent);

            // tick will enqueue a reply request
            await agent!.TickAsync(scope.ServiceProvider, default);

            // process one queued request synchronously
            var queue = (NpcReplyQueueService)scope.ServiceProvider.GetRequiredService<INpcReplyQueue>();
            var processed = await queue.ProcessNextAsync();
            Assert.True(processed, "Expected to process at least one queued npc request");

            // no delay required — events already persisted synchronously by TestEventDispatcher

            var evs = await db.GameEvents.ToListAsync();
            var replyEv = evs.FirstOrDefault(e => e.Type == "npc_reply");
            Assert.NotNull(replyEv);
            using var doc = JsonDocument.Parse(replyEv!.PayloadJson);
            Assert.True(doc.RootElement.TryGetProperty("characterId", out var cid));
            Assert.True(doc.RootElement.TryGetProperty("reply", out var r));
            Assert.True(doc.RootElement.TryGetProperty("meta", out var meta));
            Assert.True(meta.TryGetProperty("reasks", out var reasks));
            Assert.True(meta.TryGetProperty("sanitizations", out var sanitizations));
            Assert.Equal(ch.Id.ToString(), cid.GetString());
            Assert.True(!string.IsNullOrWhiteSpace(r.GetString()));
            Assert.True(reasks.ValueKind == JsonValueKind.Number);
            Assert.True(sanitizations.ValueKind == JsonValueKind.Number);

            await app.StopAsync();
        }

        // Простая mock-реализация ILlmClient, возвращает корректный JSON для npc reply
        private class MockLlmClient : ILlmClient
        {
            public Task<string> SendPromptAsync(string prompt, CancellationToken ct = default)
            {
                var resp = JsonSerializer.Serialize(new { reply = "Привет, земляк.", moodDelta = 1 });
                return Task.FromResult(resp);
            }
        }

        // Тестовый синхронный диспетчер событий: сразу сохраняет event в базу
        private class TestEventDispatcher : Imperium.Domain.Services.IEventDispatcher
        {
            private readonly IServiceProvider _sp;
            public TestEventDispatcher(IServiceProvider sp) => _sp = sp;
            public async ValueTask EnqueueAsync(Imperium.Domain.Models.GameEvent e)
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
                db.GameEvents.Add(e);
                await db.SaveChangesAsync();
            }
        }

        [Fact]
        public async Task SeasonAgent_ChangesSeason_WhenAverageTempShifts()
        {
            var builder = WebApplication.CreateBuilder();
            var dataDir = System.IO.Path.Combine(Environment.CurrentDirectory, "season-change-data");
            System.IO.Directory.CreateDirectory(dataDir);
            var dbPath = System.IO.Path.Combine(dataDir, "test-season-change.db");
            builder.Services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
            builder.Services.AddSingleton<Imperium.Api.MetricsService>();
            // register test dispatcher
            builder.Services.AddSingleton<TestEventDispatcher>();
            builder.Services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(sp => sp.GetRequiredService<TestEventDispatcher>());
            builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.SeasonAgent>();

            // avoid port collisions in parallel test runs
            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            var app = builder.Build();
            await app.StartAsync();
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            // seed warm snapshots to initialize Summer
            for (int i = 0; i < 12; i++)
                db.WeatherSnapshots.Add(new Imperium.Domain.Models.WeatherSnapshot { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow.AddHours(-i), Condition = "warm", TemperatureC = 20, WindKph = 5, PrecipitationMm = 0, DayLengthHours = 12 });
            await db.SaveChangesAsync();

            var agent = scope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>().First(a => a.Name == "SeasonAI");
            await agent.TickAsync(scope.ServiceProvider, default);

            // Now seed cold snapshots to force season change
            db.WeatherSnapshots.AddRange(Enumerable.Range(1, 12).Select(_ => new Imperium.Domain.Models.WeatherSnapshot { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Condition = "cold", TemperatureC = -3, WindKph = 5, PrecipitationMm = 0, DayLengthHours = 8 }));
            await db.SaveChangesAsync();

            await agent.TickAsync(scope.ServiceProvider, default);

            var evs = await db.GameEvents.ToListAsync();
            var change = evs.FirstOrDefault(e => e.Type == "season_change");
            Assert.NotNull(change);
            using var doc = JsonDocument.Parse(change!.PayloadJson);
            Assert.True(doc.RootElement.TryGetProperty("season", out var season));
            Assert.Equal("Winter", season.GetString());
            await app.StopAsync();
        }
    }
}
