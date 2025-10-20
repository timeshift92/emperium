using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Imperium.Api.Agents;
using Imperium.Domain.Models;
using Imperium.Infrastructure;
using Xunit;

namespace Imperium.Api.Tests
{
    public class OwnershipAgentTests
    {
        private ServiceProvider BuildProvider(SqliteConnection conn)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ImperiumDbContext>(opts => opts.UseSqlite(conn));
            services.AddSingleton<Imperium.Api.MetricsService>();
            services.AddSingleton<Imperium.Domain.Services.IEventDispatcher, TestEventDispatcher>();
            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task OwnershipAgent_CreatesOwnership_OnInheritanceRecord()
        {
            var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared"); conn.Open();
            var provider = BuildProvider(conn);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            var prevOwner = Guid.NewGuid();
            var heir = Guid.NewGuid();
            var assetId = Guid.NewGuid();

            // create an inheritance record that implies asset transfer
            var rec = new InheritanceRecord { Id = Guid.NewGuid(), DeceasedId = prevOwner, HeirsJson = JsonSerializer.Serialize(new[] { heir }), RulesJson = JsonSerializer.Serialize(new { type = "equal_split", assetId = assetId }), CreatedAt = DateTime.UtcNow };
            db.InheritanceRecords.Add(rec);
            await db.SaveChangesAsync();

            var agent = new OwnershipAgent();
            await agent.TickAsync(provider, default);

            // Ownership should exist now
            var own = await db.Ownerships.FirstOrDefaultAsync(o => o.AssetId == assetId);
            Assert.NotNull(own);
            Assert.Equal(heir, own.OwnerId);
            Assert.Equal("inheritance", own.AcquisitionType);
        }
    }
}
