using System;
using System.Linq;
using System.Threading.Tasks;
using Imperium.Api;
using Imperium.Api.Models;
using Xunit;

namespace Imperium.Api.Tests
{
    public class EconomyStateServiceTests
    {
        [Fact]
        public void DefaultSeedsContainCommonItems()
        {
            var svc = new EconomyStateService();
            var items = svc.GetItems().ToArray();
            Assert.Contains("grain", items, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("wine", items, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("oil", items, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void AddItemsCreatesDefaultDefinition()
        {
            var svc = new EconomyStateService();
            var added = svc.AddItems(new[] { "silver" });
            Assert.Equal(1, added);
            var def = svc.GetDefinition("silver");
            Assert.NotNull(def);
            Assert.Equal("silver", def!.Name, ignoreCase: true);
            Assert.Equal(5m, def.BasePrice);
            Assert.Equal(1m, def.WeightPerUnit);
            Assert.Equal(100, def.StackSize);
        }

        [Fact]
        public void AddOrUpdateDefinition_UpdatesExisting()
        {
            var svc = new EconomyStateService();
            var def = new EconomyItemDefinition { Name = "grain", BasePrice = 33.3m, Unit = "kg" };
            var result = svc.AddOrUpdateDefinition(def);
            Assert.True(result);
            var got = svc.GetDefinition("grain");
            Assert.NotNull(got);
            Assert.Equal(33.3m, got!.BasePrice);
            Assert.Equal("kg", got.Unit);
        }

        [Fact]
        public void Shocks_AreAppliedAndExpire()
        {
            var svc = new EconomyStateService();
            svc.SetShock("*", 1.2m, null);
            svc.SetShock("grain", 1.5m, null);
            var mult = svc.GetEffectiveMultiplier("grain");
            Assert.Equal(1.2m * 1.5m, mult);

            // expired shock is purged
            svc.SetShock("grain", 2m, DateTime.UtcNow.AddSeconds(-1));
            var mul2 = svc.GetEffectiveMultiplier("grain");
            Assert.Equal(1.2m, mul2);
        }
    }
}
