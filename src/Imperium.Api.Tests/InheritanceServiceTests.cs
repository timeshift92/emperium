using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Imperium.Api.Services;
using Imperium.Domain.Models;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Imperium.Api.Tests
{
    public class InheritanceServiceTests
    {
        [Fact]
        public async Task ApplyInheritance_EqualSplit_DistributesHouseholdWealth_AsMinimalUnits()
        {
            var opts = new DbContextOptionsBuilder<ImperiumDbContext>().UseInMemoryDatabase("inh_test1").Options;
            await using var db = new ImperiumDbContext(opts);

            // Prepare deceased, heirs and household
            var deceased = Guid.NewGuid();
            var heir1 = Guid.NewGuid();
            var heir2 = Guid.NewGuid();

            var hh = new Household { Id = Guid.NewGuid(), HeadId = deceased, MemberIdsJson = JsonSerializer.Serialize(new[] { deceased.ToString() }), Wealth = 101.23m };
            db.Households.Add(hh);

            var rec = new InheritanceRecord { Id = Guid.NewGuid(), DeceasedId = deceased, HeirsJson = JsonSerializer.Serialize(new[] { heir1, heir2 }), RulesJson = JsonSerializer.Serialize(new { type = "equal_split" }), CreatedAt = DateTime.UtcNow };
            db.InheritanceRecords.Add(rec);

            await db.SaveChangesAsync();

            var dispatcher = new TestDispatcher();
            var random = new Imperium.Api.Utils.SeedableRandom(123);
            var currencyOpts = Options.Create(new Imperium.Api.CurrencyOptions { DecimalPlaces = 2 });
            var inhOpts = Options.Create(new Imperium.Api.InheritanceOptions { TieBreaker = Imperium.Api.TieBreakerOption.DeterministicHash, Salt = "" });

            var svc = new InheritanceService(db, dispatcher, random, currencyOpts, inhOpts);

            var res = await svc.ApplyInheritanceAsync(rec.Id);

            Assert.True(res.IsSuccess);

            // Household wealth cleared
            var hhDb = await db.Households.FindAsync(hh.Id);
            Assert.Equal(0m, hhDb.Wealth);

            // Two inheritance_wealth_transfer events created and enqueued
            var events = db.GameEvents.Where(e => e.Type == "inheritance_wealth_transfer").ToList();
            Assert.Equal(2, events.Count);

            // Amounts (as decimals) should sum up approximately to original wealth (within minimal unit rounding)
            var amounts = events.Select(e =>
            {
                var doc = JsonDocument.Parse(e.PayloadJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("amount", out var a))
                {
                    // amount is a string formatted with F2 in code
                    if (a.ValueKind == JsonValueKind.String && decimal.TryParse(a.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                        return v;
                }
                return 0m;
            }).ToList();

            var sum = amounts.Sum();
            // Original wealth 101.23 -> sum should be either 101.23 or differ by up to 0.01 due to truncation strategy
            Assert.InRange(sum, 101.22m, 101.23m);
        }

        private class TestDispatcher : Imperium.Domain.Services.IEventDispatcher
        {
            public System.Collections.Concurrent.ConcurrentQueue<GameEvent> Enqueued { get; } = new();
            public System.Threading.Tasks.ValueTask EnqueueAsync(GameEvent ev)
            {
                Enqueued.Enqueue(ev);
                return new System.Threading.Tasks.ValueTask();
            }
        }
    }
}
