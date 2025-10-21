using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Imperium.Infrastructure;
using Imperium.Domain.Models;

namespace Imperium.Api.Tests
{
    public class TimeAgentTests : IAsyncLifetime
    {
        private ServiceProvider? _sp;
        private SqliteConnection? _conn;

        public async Task InitializeAsync()
        {
            _conn = new SqliteConnection("Data Source=:memory:");
            _conn.Open();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite(_conn));
            services.AddSingleton<Imperium.Api.MetricsService>();
            services.AddSingleton<Imperium.Domain.Services.IEventDispatcher, TestEventDispatcher>();
            services.AddSingleton<EventStreamService>();
            services.AddSingleton<Imperium.Api.Utils.IRandomProvider, Imperium.Api.Utils.SeedableRandom>();
            _sp = services.BuildServiceProvider();

            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();
            await db.SaveChangesAsync();
        }

        public Task DisposeAsync()
        {
            _conn?.Dispose();
            _sp?.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task TickAsync_Emits_TimeTick_WithMonthAndDayOfMonth()
        {
            using var scope = _sp!.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ImperiumDbContext>();
            var timeAgent = new Imperium.Api.Agents.TimeAgent();

            await timeAgent.TickAsync(sp, default);

            // read events
            var events = await db.GameEvents.OrderByDescending(e => e.Timestamp).Take(10).ToListAsync();
            var tickEv = events.FirstOrDefault(e => e.Type == "time_tick");
            Assert.NotNull(tickEv);
            Assert.False(string.IsNullOrEmpty(tickEv.PayloadJson));
            using var doc = JsonDocument.Parse(tickEv.PayloadJson);
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("month", out var monthProp));
            Assert.True(root.TryGetProperty("dayOfMonth", out var dayProp));
            Assert.True(monthProp.ValueKind == JsonValueKind.Number);
            Assert.True(dayProp.ValueKind == JsonValueKind.Number);
            Assert.InRange(monthProp.GetInt32(), 1, 12);
            Assert.InRange(dayProp.GetInt32(), 1, 31);
        }
    }
}
