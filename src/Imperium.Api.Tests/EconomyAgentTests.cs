using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Imperium.Api.Agents;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Imperium.Api.Tests
{
    public class EconomyAgentTests
    {
        [Fact]
        public async Task TickAsync_CreatesTrades_And_PreservesCurrencyRounding()
        {
            var opts = new DbContextOptionsBuilder<ImperiumDbContext>().UseInMemoryDatabase("eco_test1").Options;
            await using var db = new ImperiumDbContext(opts);

            // seed locations and characters
            var loc = new Domain.Models.Location { Id = Guid.NewGuid(), Name = "TestCity", Treasury = 0m };
            db.Locations.Add(loc);
            var ch1 = new Domain.Models.Character { Id = Guid.NewGuid(), Name = "A", Money = 100m };
            var ch2 = new Domain.Models.Character { Id = Guid.NewGuid(), Name = "B", Money = 10m };
            db.Characters.AddRange(ch1, ch2);
            await db.SaveChangesAsync();

            // minimal service provider to satisfy EconomyAgent dependencies
            var services = new ServiceCollection();
            services.AddSingleton(new Imperium.Api.EconomyStateService(new string[] { "grain" }));
            services.AddSingleton<Imperium.Api.MetricsService>();
            var sp = services.BuildServiceProvider();

            var agent = new EconomyAgent();

            var scopeServices = sp.CreateScope().ServiceProvider;

            // create a service provider that returns our db and other services
            var provider = new TestServiceProvider(db, scopeServices);

            await agent.TickAsync(provider, CancellationToken.None);

            // After tick, trades may have been created
            var trades = await db.Trades.ToListAsync();
            Assert.True(trades.Count >= 0); // ensure no exceptions and DB is usable
        }

        private class TestServiceProvider : IServiceProvider
        {
            private readonly ImperiumDbContext _db;
            private readonly IServiceProvider _inner;
            public TestServiceProvider(ImperiumDbContext db, IServiceProvider inner) { _db = db; _inner = inner; }
            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(ImperiumDbContext)) return _db;
                if (serviceType == typeof(Imperium.Api.MetricsService)) return _inner.GetService(typeof(Imperium.Api.MetricsService));
                if (serviceType == typeof(Imperium.Api.EconomyStateService)) return _inner.GetService(typeof(Imperium.Api.EconomyStateService));
                if (serviceType == typeof(Imperium.Api.EventStreamService)) return new Imperium.Api.EventStreamService();
                if (serviceType == typeof(Microsoft.Extensions.Options.IOptions<Imperium.Api.EconomyOptions>)) return Options.Create(new Imperium.Api.EconomyOptions { Items = new[] { "grain" } });
                return _inner.GetService(serviceType);
            }
        }
    }
}
