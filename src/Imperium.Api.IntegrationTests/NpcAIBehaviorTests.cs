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
using Xunit;

namespace Imperium.Api.IntegrationTests
{
    public class NpcAIBehaviorTests
    {
        [Fact]
        public async Task NpcAI_GeneratesActionAndUpdatesMoodEnergy()
        {
            var builder = WebApplication.CreateBuilder();
            var dataDir = System.IO.Path.Combine(Environment.CurrentDirectory, "npcai-test-data");
            System.IO.Directory.CreateDirectory(dataDir);
            var dbPath = System.IO.Path.Combine(dataDir, "test-npcai.db");
            builder.Services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
            builder.Services.AddSingleton<Imperium.Api.MetricsService>();
            builder.Services.AddSingleton<Imperium.Api.EventStreamService>();
            builder.Services.AddSingleton<TestEventDispatcher>();
            builder.Services.AddSingleton<Imperium.Domain.Services.IEventDispatcher>(sp => sp.GetRequiredService<TestEventDispatcher>());

            // LLM mock
            builder.Services.AddSingleton<ILlmClient, MockLlmClient>();

            // Register the API NpcBehaviorAgent
            builder.Services.AddScoped<Imperium.Domain.Agents.IWorldAgent, Imperium.Api.Agents.NpcBehaviorAgent>();

            builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
            var app = builder.Build();
            await app.StartAsync();

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            // create one character and essence
            var ch = new Imperium.Domain.Models.Character { Id = Guid.NewGuid(), Name = "Автономный NPC", Age = 30, Status = "ok", Money = 5m };
            db.Characters.Add(ch);
            db.NpcEssences.Add(new Imperium.Domain.Models.NpcEssence { Id = Guid.NewGuid(), CharacterId = ch.Id, Strength = 5, Intelligence = 5, Charisma = 5, Vitality = 5, Luck = 1, MutationChance = 0.0 });
            await db.SaveChangesAsync();

            var agent = scope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>().FirstOrDefault(a => a.Name == "NpcBehaviorAI");
            Assert.NotNull(agent);

            await agent!.TickAsync(scope.ServiceProvider, default);

            // events persisted synchronously by TestEventDispatcher
            var evs = await db.GameEvents.ToListAsync();
            Assert.True(evs.Any(e => e.Type == "npc_action"), "Expected npc_action event");

            var essence = await db.NpcEssences.FirstAsync();
            Assert.False(string.IsNullOrWhiteSpace(essence.LastAction));
            Assert.InRange(essence.Energy, 0.0, 1.0);

            await app.StopAsync();
        }

        private class MockLlmClient : ILlmClient
        {
            public Task<string> SendPromptAsync(string prompt, CancellationToken ct = default)
            {
                var resp = JsonSerializer.Serialize(new { action = "пошёл на рынок", emotion = "спокойный", energyDelta = -0.1, motivationDelta = 0.05 });
                return Task.FromResult(resp);
            }
        }
    }
}
