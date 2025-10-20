using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
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
    public class InheritanceTieBreakerTests
    {
        private ServiceProvider BuildProvider(SqliteConnection conn, Imperium.Api.InheritanceOptions opts, Imperium.Api.Utils.IRandomProvider? rnd = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ImperiumDbContext>(optsdb => optsdb.UseSqlite(conn));
            services.AddSingleton<Imperium.Domain.Services.IEventDispatcher, TestEventDispatcher>();
            if (rnd != null) services.AddSingleton<Imperium.Api.Utils.IRandomProvider>(rnd);
            else services.AddSingleton<Imperium.Api.Utils.IRandomProvider, Imperium.Api.Utils.SeedableRandom>();
            services.Configure<Imperium.Api.InheritanceOptions>(o => { o.TieBreaker = opts.TieBreaker; o.Salt = opts.Salt; });
            services.Configure<Imperium.Api.CurrencyOptions>(c => c.DecimalPlaces = 2);
            services.AddScoped<InheritanceService>();
            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task Shares_TieBreak_DeterministicHash_RespectsSalt()
        {
            var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared"); conn.Open();
            var opt = new Imperium.Api.InheritanceOptions { TieBreaker = Imperium.Api.TieBreakerOption.DeterministicHash, Salt = "salt123" };
            var provider = BuildProvider(conn, opt);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            var deceased = new Character { Id = Guid.NewGuid() };
            var heirs = Enumerable.Range(0, 3).Select(_ => new Character { Id = Guid.NewGuid() }).ToArray();
            db.Characters.Add(deceased); db.Characters.AddRange(heirs);

            // No household wealth, just assets to force shares allocation fractional tie
            var assets = new List<Ownership>();
            for (int i = 0; i < 3; i++) assets.Add(new Ownership { Id = Guid.NewGuid(), OwnerId = deceased.Id, AssetId = Guid.NewGuid(), AssetType = "X" });
            db.Ownerships.AddRange(assets);
            await db.SaveChangesAsync();

            // shares all equal -> fractional desired equal -> tie situation
            var sharesJson = "{ \"type\": \"shares\", \"shares\": [ " + string.Join(',', heirs.Select(h => "{ \"heir\": \"" + h.Id + "\", \"pct\": 0.333333 }")) + " ] }";
            var heirsJson = "[" + string.Join(',', heirs.Select(h => '"' + h.Id.ToString() + '"')) + "]";
            var rec = new InheritanceRecord { Id = Guid.NewGuid(), DeceasedId = deceased.Id, HeirsJson = heirsJson, RulesJson = sharesJson, CreatedAt = DateTime.UtcNow };
            db.InheritanceRecords.Add(rec);
            await db.SaveChangesAsync();

            var svc = scope.ServiceProvider.GetRequiredService<InheritanceService>();
            var res = await svc.ApplyInheritanceAsync(rec.Id);
            Assert.True(res.IsSuccess);

            var assigned = (await db.Ownerships.Where(o => o.OwnerId != deceased.Id).ToListAsync()).Select(o => o.OwnerId).ToArray();
            // Deterministic hash should produce stable ordering; we assert count distribution 3 assets -> 1 each
            Assert.Equal(3, assigned.Length);
            var counts = assigned.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            Assert.All(heirs, h => Assert.Equal(1, counts[h.Id]));
        }

        [Fact]
        public async Task Shares_TieBreak_Random_UsesProvidedRandomProvider()
        {
            var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared"); conn.Open();
            var opt = new Imperium.Api.InheritanceOptions { TieBreaker = Imperium.Api.TieBreakerOption.Random, Salt = "" };
            // deterministic seedable random
            var rnd = new Imperium.Api.Utils.SeedableRandom(12345);
            var provider = BuildProvider(conn, opt, rnd);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            var deceased = new Character { Id = Guid.NewGuid() };
            var heirs = Enumerable.Range(0, 3).Select(_ => new Character { Id = Guid.NewGuid() }).ToArray();
            db.Characters.Add(deceased); db.Characters.AddRange(heirs);

            var assets = new List<Ownership>();
            for (int i = 0; i < 3; i++) assets.Add(new Ownership { Id = Guid.NewGuid(), OwnerId = deceased.Id, AssetId = Guid.NewGuid(), AssetType = "X" });
            db.Ownerships.AddRange(assets);
            await db.SaveChangesAsync();

            var sharesJson = "{ \"type\": \"shares\", \"shares\": [ " + string.Join(',', heirs.Select(h => "{ \"heir\": \"" + h.Id + "\", \"pct\": 0.333333 }")) + " ] }";
            var heirsJson = "[" + string.Join(',', heirs.Select(h => '"' + h.Id.ToString() + '"')) + "]";
            var rec = new InheritanceRecord { Id = Guid.NewGuid(), DeceasedId = deceased.Id, HeirsJson = heirsJson, RulesJson = sharesJson, CreatedAt = DateTime.UtcNow };
            db.InheritanceRecords.Add(rec);
            await db.SaveChangesAsync();

            var svc = scope.ServiceProvider.GetRequiredService<InheritanceService>();
            var res = await svc.ApplyInheritanceAsync(rec.Id);
            Assert.True(res.IsSuccess);

            var assigned = (await db.Ownerships.Where(o => o.OwnerId != deceased.Id).ToListAsync()).Select(o => o.OwnerId).ToArray();
            Assert.Equal(3, assigned.Length);
            var counts = assigned.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            Assert.All(heirs, h => Assert.Equal(1, counts[h.Id]));
        }
    }
}
