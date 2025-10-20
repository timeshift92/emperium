using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Imperium.Api.Services;
using Imperium.Domain.Models;
using Imperium.Infrastructure;
using Xunit;

namespace Imperium.Api.Tests
{
    public class InheritanceIntegrationTests
    {
        private ServiceProvider BuildProvider(SqliteConnection conn)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ImperiumDbContext>(opts => opts.UseSqlite(conn));
            services.AddSingleton<Imperium.Domain.Services.IEventDispatcher, TestEventDispatcher>();
            // Seedable RNG for deterministic tests
            services.AddSingleton<Imperium.Api.Utils.IRandomProvider, Imperium.Api.Utils.SeedableRandom>();
            services.AddScoped<InheritanceService>();
            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task ApplyInheritance_ReassignsOwnerships_And_DistributesHouseholdWealth()
        {
            var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared"); conn.Open();
            var provider = BuildProvider(conn);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            // create deceased and heirs
            var deceased = new Character { Id = Guid.NewGuid(), Name = "Deceased" };
            var heirA = new Character { Id = Guid.NewGuid(), Name = "HeirA" };
            var heirB = new Character { Id = Guid.NewGuid(), Name = "HeirB" };
            db.Characters.AddRange(deceased, heirA, heirB);

            // Create household owned/headed by deceased with wealth
            var hh = new Household { Id = Guid.NewGuid(), Name = "OldHouse", Wealth = 100m, HeadId = deceased.Id, MemberIdsJson = JsonSerializer.Serialize(new[] { deceased.Id }) };
            db.Households.Add(hh);

            // Create two ownership assets owned by deceased
            var asset1 = new Ownership { Id = Guid.NewGuid(), OwnerId = deceased.Id, AssetId = Guid.NewGuid(), AssetType = "Land", OwnerType = "Character", Confidence = 1.0, AcquisitionType = "purchase", AcquiredAt = DateTime.UtcNow };
            var asset2 = new Ownership { Id = Guid.NewGuid(), OwnerId = deceased.Id, AssetId = Guid.NewGuid(), AssetType = "Item", OwnerType = "Character", Confidence = 1.0, AcquisitionType = "purchase", AcquiredAt = DateTime.UtcNow };
            db.Ownerships.AddRange(asset1, asset2);

            await db.SaveChangesAsync();

            // create inheritance record with two heirs and rule equal_split
            var heirsJson = "[\"" + heirA.Id + "\",\"" + heirB.Id + "\"]";
            var rec = new InheritanceRecord { Id = Guid.NewGuid(), DeceasedId = deceased.Id, HeirsJson = heirsJson, RulesJson = "{ \"type\": \"equal_split\" }", CreatedAt = DateTime.UtcNow };
            db.InheritanceRecords.Add(rec);
            await db.SaveChangesAsync();

            var service = scope.ServiceProvider.GetRequiredService<InheritanceService>();
            var result = await service.ApplyInheritanceAsync(rec.Id);

            Assert.True(result.IsSuccess, "Inheritance should succeed");

            // ownerships must no longer have deceased as owner
            var updated1 = await db.Ownerships.FirstOrDefaultAsync(o => o.Id == asset1.Id);
            var updated2 = await db.Ownerships.FirstOrDefaultAsync(o => o.Id == asset2.Id);
            Assert.NotEqual(deceased.Id, updated1.OwnerId);
            Assert.NotEqual(deceased.Id, updated2.OwnerId);

            // household wealth should be zeroed after distribution and events exist
            var updatedHh = await db.Households.FirstOrDefaultAsync(h => h.Id == hh.Id);
            Assert.Equal(0m, updatedHh.Wealth);

            var evs = await db.GameEvents.Where(e => e.Type.StartsWith("inheritance_")).ToListAsync();
            Assert.True(evs.Count >= 1, "At least one inheritance event should be recorded");
        }

        [Fact]
        public async Task ApplyInheritance_Primogeniture_AllAssetsToFirstHeir()
        {
            var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared"); conn.Open();
            var provider = BuildProvider(conn);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            var deceased = new Character { Id = Guid.NewGuid(), Name = "DeceasedP" };
            var heirA = new Character { Id = Guid.NewGuid(), Name = "HeirA" };
            var heirB = new Character { Id = Guid.NewGuid(), Name = "HeirB" };
            db.Characters.AddRange(deceased, heirA, heirB);

            var asset1 = new Ownership { Id = Guid.NewGuid(), OwnerId = deceased.Id, AssetId = Guid.NewGuid(), AssetType = "Land" };
            var asset2 = new Ownership { Id = Guid.NewGuid(), OwnerId = deceased.Id, AssetId = Guid.NewGuid(), AssetType = "Item" };
            db.Ownerships.AddRange(asset1, asset2);
            await db.SaveChangesAsync();

            var heirsJsonP = "[\"" + heirA.Id + "\",\"" + heirB.Id + "\"]";
            var rec = new InheritanceRecord { Id = Guid.NewGuid(), DeceasedId = deceased.Id, HeirsJson = heirsJsonP, RulesJson = "{ \"type\": \"primogeniture\" }", CreatedAt = DateTime.UtcNow };
            db.InheritanceRecords.Add(rec);
            await db.SaveChangesAsync();

            var service = scope.ServiceProvider.GetRequiredService<InheritanceService>();
            var result = await service.ApplyInheritanceAsync(rec.Id);
            Assert.True(result.IsSuccess);

            var updated1 = await db.Ownerships.FirstOrDefaultAsync(o => o.Id == asset1.Id);
            var updated2 = await db.Ownerships.FirstOrDefaultAsync(o => o.Id == asset2.Id);
            Assert.Equal(heirA.Id, updated1.OwnerId);
            Assert.Equal(heirA.Id, updated2.OwnerId);
        }

        [Fact]
        public async Task ApplyInheritance_Shares_DistributesAssetsByShares()
        {
            var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared"); conn.Open();
            var provider = BuildProvider(conn);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            var deceased = new Character { Id = Guid.NewGuid(), Name = "DeceasedS" };
            var heirA = new Character { Id = Guid.NewGuid(), Name = "HeirA" };
            var heirB = new Character { Id = Guid.NewGuid(), Name = "HeirB" };
            db.Characters.AddRange(deceased, heirA, heirB);

            // 3 assets
            var asset1 = new Ownership { Id = Guid.NewGuid(), OwnerId = deceased.Id, AssetId = Guid.NewGuid(), AssetType = "Land" };
            var asset2 = new Ownership { Id = Guid.NewGuid(), OwnerId = deceased.Id, AssetId = Guid.NewGuid(), AssetType = "Item" };
            var asset3 = new Ownership { Id = Guid.NewGuid(), OwnerId = deceased.Id, AssetId = Guid.NewGuid(), AssetType = "Boat" };
            db.Ownerships.AddRange(asset1, asset2, asset3);
            await db.SaveChangesAsync();

            // shares: heirA 2/3, heirB 1/3
            var rules = "{ \"type\": \"shares\", \"shares\": [ { \"heir\": \"" + heirA.Id + "\", \"pct\": 0.66 }, { \"heir\": \"" + heirB.Id + "\", \"pct\": 0.34 } ] }";
            var heirsJsonS = "[\"" + heirA.Id + "\",\"" + heirB.Id + "\"]";
            var rec = new InheritanceRecord { Id = Guid.NewGuid(), DeceasedId = deceased.Id, HeirsJson = heirsJsonS, RulesJson = rules, CreatedAt = DateTime.UtcNow };
            db.InheritanceRecords.Add(rec);
            await db.SaveChangesAsync();

            var service = scope.ServiceProvider.GetRequiredService<InheritanceService>();
            var result = await service.ApplyInheritanceAsync(rec.Id);
            Assert.True(result.IsSuccess);

            var updated = await db.Ownerships.Where(o => o.OwnerId != deceased.Id).ToListAsync();
            Assert.Equal(3, updated.Count);
            // check counts: heirA should have 2 assets, heirB 1 asset
            var aCount = updated.Count(o => o.OwnerId == heirA.Id);
            var bCount = updated.Count(o => o.OwnerId == heirB.Id);
            Assert.Equal(2, aCount);
            Assert.Equal(1, bCount);
        }
    }
}
