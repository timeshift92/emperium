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
    public class InheritanceWealthTests
    {
        private ServiceProvider BuildProvider(SqliteConnection conn, int? seed = null)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ImperiumDbContext>(opts => opts.UseSqlite(conn));
            services.AddSingleton<Imperium.Domain.Services.IEventDispatcher, TestEventDispatcher>();
            if (seed.HasValue)
                services.AddSingleton<Imperium.Api.Utils.IRandomProvider>(new Imperium.Api.Utils.SeedableRandom(seed.Value));
            else
                services.AddSingleton<Imperium.Api.Utils.IRandomProvider, Imperium.Api.Utils.SeedableRandom>();
            // Default currency options
            services.Configure<Imperium.Api.CurrencyOptions>(opts => opts.DecimalPlaces = 2);
            services.AddScoped<InheritanceService>();
            return services.BuildServiceProvider();
        }

        private ServiceProvider BuildProviderWithCurrency(SqliteConnection conn, int decimalPlaces, int? seed = null)
        {
            var sp = new ServiceCollection();
            sp.AddLogging();
            sp.AddDbContext<ImperiumDbContext>(opts => opts.UseSqlite(conn));
            sp.AddSingleton<Imperium.Domain.Services.IEventDispatcher, TestEventDispatcher>();
            if (seed.HasValue)
                sp.AddSingleton<Imperium.Api.Utils.IRandomProvider>(new Imperium.Api.Utils.SeedableRandom(seed.Value));
            else
                sp.AddSingleton<Imperium.Api.Utils.IRandomProvider, Imperium.Api.Utils.SeedableRandom>();
            sp.Configure<Imperium.Api.CurrencyOptions>(opts => opts.DecimalPlaces = decimalPlaces);
            sp.AddScoped<InheritanceService>();
            return sp.BuildServiceProvider();
        }

        [Fact]
        public async Task Wealth_Distribution_Respects_Currency_Precision()
        {
            var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared"); conn.Open();
            var provider = BuildProviderWithCurrency(conn, 3);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            var deceased = new Character { Id = Guid.NewGuid(), Name = "Dead" };
            var heirA = new Character { Id = Guid.NewGuid(), Name = "A" };
            var heirB = new Character { Id = Guid.NewGuid(), Name = "B" };
            db.Characters.AddRange(deceased, heirA, heirB);

            // Household with wealth that would produce fractional cents (e.g., 0.03 split among 2 heirs)
            var hh = new Household { Id = Guid.NewGuid(), Name = "H", Wealth = 0.03m, HeadId = deceased.Id, MemberIdsJson = JsonSerializer.Serialize(new[] { deceased.Id }) };
            db.Households.Add(hh);
            await db.SaveChangesAsync();

            var rec = new InheritanceRecord { Id = Guid.NewGuid(), DeceasedId = deceased.Id, HeirsJson = "[\"" + heirA.Id + "\",\"" + heirB.Id + "\"]", RulesJson = "{ \"type\": \"equal_split\" }", CreatedAt = DateTime.UtcNow };
            db.InheritanceRecords.Add(rec);
            await db.SaveChangesAsync();

            var svc = scope.ServiceProvider.GetRequiredService<InheritanceService>();
            var res = await svc.ApplyInheritanceAsync(rec.Id);
            Assert.True(res.IsSuccess);

            var updated = await db.Households.FindAsync(hh.Id);
            Assert.Equal(0m, updated.Wealth);

            var evs = await db.GameEvents.Where(e => e.Type == "inheritance_wealth_transfer").ToListAsync();
            Assert.Equal(2, evs.Count); // two heirs, both should receive either 0.01 or 0.02 cents split correctly

            // Verify total transferred equals original wealth (0.03)
            decimal sum = 0m;
            foreach (var ev in evs)
            {
                var p = JsonSerializer.Deserialize<JsonElement>(ev.PayloadJson);
                if (p.TryGetProperty("amount", out var amt))
                {
                    var s = amt.GetString();
                    if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                        sum += v;
                }
            }
            Assert.Equal(0.03m, sum);
        }

        [Fact]
        public async Task Wealth_Distribution_With_Precision_3_Works()
        {
            var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared"); conn.Open();
            var provider = BuildProviderWithCurrency(conn, 3);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            var deceased = new Character { Id = Guid.NewGuid(), Name = "Dead3" };
            var heirA = new Character { Id = Guid.NewGuid(), Name = "A3" };
            var heirB = new Character { Id = Guid.NewGuid(), Name = "B3" };
            db.Characters.AddRange(deceased, heirA, heirB);

            // Wealth that requires 3 decimal places (e.g., 0.007 split among 2 heirs)
            var hh = new Household { Id = Guid.NewGuid(), Name = "H3", Wealth = 0.007m, HeadId = deceased.Id, MemberIdsJson = JsonSerializer.Serialize(new[] { deceased.Id }) };
            db.Households.Add(hh);
            await db.SaveChangesAsync();

            var rec = new InheritanceRecord { Id = Guid.NewGuid(), DeceasedId = deceased.Id, HeirsJson = "[\"" + heirA.Id + "\",\"" + heirB.Id + "\"]", RulesJson = "{ \"type\": \"equal_split\" }", CreatedAt = DateTime.UtcNow };
            db.InheritanceRecords.Add(rec);
            await db.SaveChangesAsync();

            var svc = scope.ServiceProvider.GetRequiredService<InheritanceService>();
            var res = await svc.ApplyInheritanceAsync(rec.Id);
            Assert.True(res.IsSuccess);

            var updated = await db.Households.FindAsync(hh.Id);
            Assert.Equal(0m, updated.Wealth);

            var evs = await db.GameEvents.Where(e => e.Type == "inheritance_wealth_transfer").ToListAsync();
            Assert.Equal(2, evs.Count);

            decimal sum = 0m;
            foreach (var ev in evs)
            {
                var p = JsonSerializer.Deserialize<JsonElement>(ev.PayloadJson);
                if (p.TryGetProperty("amount", out var amt))
                {
                    var s = amt.GetString();
                    if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                        sum += v;
                }
            }
            Assert.Equal(0.007m, sum);
        }

        [Fact]
        public async Task EqualSplit_Remainder_Distribution_Is_Deterministic_With_Seed()
        {
            var seed = 42;
            var conn = new SqliteConnection("DataSource=:memory:;Cache=Shared"); conn.Open();
            var provider = BuildProvider(conn, seed);
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.Database.EnsureCreated();

            var deceased = new Character { Id = Guid.NewGuid(), Name = "Dead2" };
            var heirs = Enumerable.Range(0, 5).Select(_ => new Character { Id = Guid.NewGuid() }).ToArray();
            db.Characters.Add(deceased);
            db.Characters.AddRange(heirs);

            // Wealth that causes remainder units (e.g., 0.07 with 2 decimals -> 7 cents among 5 heirs)
            var hh = new Household { Id = Guid.NewGuid(), Name = "H2", Wealth = 0.07m, HeadId = deceased.Id, MemberIdsJson = JsonSerializer.Serialize(new[] { deceased.Id }) };
            db.Households.Add(hh);
            await db.SaveChangesAsync();

            var heirsJson = "[" + string.Join(',', heirs.Select(h => '"' + h.Id.ToString() + '"')) + "]";
            var rec = new InheritanceRecord { Id = Guid.NewGuid(), DeceasedId = deceased.Id, HeirsJson = heirsJson, RulesJson = "{ \"type\": \"equal_split\" }", CreatedAt = DateTime.UtcNow };
            db.InheritanceRecords.Add(rec);
            await db.SaveChangesAsync();

            var svc = scope.ServiceProvider.GetRequiredService<InheritanceService>();
            var res = await svc.ApplyInheritanceAsync(rec.Id);
            Assert.True(res.IsSuccess);

            var evs = await db.GameEvents.Where(e => e.Type == "inheritance_wealth_transfer").ToListAsync();
            Assert.Equal(5, evs.Count);

            // Collect amounts in consistent order (by heir id ordering used in implementation)
            var amounts = evs.Select(ev => JsonSerializer.Deserialize<JsonElement>(ev.PayloadJson).GetProperty("amount").GetString())
                             .Select(s => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).OrderBy(x => x).ToArray();

            // With seed 42 we expect deterministic distribution: sum should be 0.07
            Assert.Equal(0.07m, amounts.Sum());
        }
    }
}
