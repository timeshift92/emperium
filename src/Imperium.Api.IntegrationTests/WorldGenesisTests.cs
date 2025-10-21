using System;
using System.Threading.Tasks;
using System.IO;
// using System.Linq; // already declared
using System.Text.Json;
// using Xunit; // already declared
using Microsoft.AspNetCore.Builder;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Imperium.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
// (EF Core and DI usings above)
using Xunit;

namespace Imperium.Api.IntegrationTests;

public class WorldGenesisTests
{
    [Fact]
    public async Task GenesisRunsOnce_WhenCalledTwice_DoesNotDuplicate()
    {
        var builder = WebApplication.CreateBuilder();
        var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "testdata");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "test-imperium.db");
        builder.Services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
        var app = builder.Build();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // run genesis twice
    await Imperium.Api.WorldGenesisService.InitializeAsync(db, scope.ServiceProvider);
        // capture counts
        var locCount1 = await db.Locations.CountAsync();
        var factions1 = await db.Factions.CountAsync();

    await Imperium.Api.WorldGenesisService.InitializeAsync(db, scope.ServiceProvider);
        var locCount2 = await db.Locations.CountAsync();
        var factions2 = await db.Factions.CountAsync();

        Assert.Equal(locCount1, locCount2);
        Assert.Equal(factions1, factions2);
    }

    [Fact]
    public async Task NatureAndTribes_GenesisIdempotent()
    {
        var builder = WebApplication.CreateBuilder();
        var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "testdata2");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "test-imperium2.db");
        builder.Services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
        var app = builder.Build();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        await Imperium.Api.WorldGenesisService.InitializeAsync(db, scope.ServiceProvider);
    await Imperium.Infrastructure.Setup.NatureGenesisService.InitializeAsync(db, scope.ServiceProvider);
    await Imperium.Infrastructure.Setup.TribesGenesisService.InitializeAsync(db);

        var events1 = await db.GameEvents.CountAsync();
        var factions1 = await db.Factions.CountAsync();

        // run again
        await Imperium.Api.WorldGenesisService.InitializeAsync(db, scope.ServiceProvider);
    await Imperium.Infrastructure.Setup.NatureGenesisService.InitializeAsync(db, scope.ServiceProvider);
    await Imperium.Infrastructure.Setup.TribesGenesisService.InitializeAsync(db);

        var events2 = await db.GameEvents.CountAsync();
        var factions2 = await db.Factions.CountAsync();

        Assert.Equal(events1, events2);
        Assert.Equal(factions1, factions2);
    }

    [Fact]
    public async Task CivilizationGenesis_CreatesCityStatesAndEconomy()
    {
        var builder = WebApplication.CreateBuilder();
        var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "testdata3");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "test-imperium3.db");
        builder.Services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
        var app = builder.Build();

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();

        // Seed world, nature and tribes then run civilization genesis
        await Imperium.Api.WorldGenesisService.InitializeAsync(db, scope.ServiceProvider);
        await Imperium.Infrastructure.Setup.NatureGenesisService.InitializeAsync(db, scope.ServiceProvider);
        await Imperium.Infrastructure.Setup.TribesGenesisService.InitializeAsync(db);
        await Imperium.Infrastructure.Setup.CivilizationGenesisService.InitializeAsync(db);

        // Check city-states created
        var cityStates = await db.Factions.Where(f => f.Type == "city_state").ToListAsync();
        Assert.True(cityStates.Count >= 1 && cityStates.Count <= 3, "Expected 1-3 city_states");

        // Check economy snapshots exist
        var econCount = await db.EconomySnapshots.CountAsync();
        Assert.True(econCount >= cityStates.Count, "Economy snapshots should exist for each city-state");

        // Check buildings created per city (by LocationId)
        foreach (var c in cityStates)
        {
            if (c.LocationId == null) continue;
            var bCount = await db.Buildings.Where(b => b.LocationId == c.LocationId && (b.Kind == "market" || b.Kind == "forge" || b.Kind == "temple" || b.Kind == "walls")).CountAsync();
            Assert.True(bCount >= 4, $"City {c.Name} at location {c.LocationId} should have at least 4 buildings");
        }
    }

        [Fact]
        public async Task CivilizationGenesis_IsIdempotent_OnSecondRun()
        {
            var builder = WebApplication.CreateBuilder();
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "testdata4");
            Directory.CreateDirectory(dataDir);
            var dbPath = Path.Combine(dataDir, "test-imperium4.db");
            builder.Services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
            var app = builder.Build();

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            // prerequisites
            await Imperium.Api.WorldGenesisService.InitializeAsync(db, scope.ServiceProvider);
            await Imperium.Infrastructure.Setup.NatureGenesisService.InitializeAsync(db, scope.ServiceProvider);
            await Imperium.Infrastructure.Setup.TribesGenesisService.InitializeAsync(db);

            // First run of civilization genesis
            await Imperium.Infrastructure.Setup.CivilizationGenesisService.InitializeAsync(db);
            var cityStatesAfterFirst = await db.Factions.Where(f => f.Type == "city_state").CountAsync();
            var econSnapshotsAfterFirst = await db.EconomySnapshots.CountAsync();

            // Second run (should be no-op)
            await Imperium.Infrastructure.Setup.CivilizationGenesisService.InitializeAsync(db);
            var cityStatesAfterSecond = await db.Factions.Where(f => f.Type == "city_state").CountAsync();
            var econSnapshotsAfterSecond = await db.EconomySnapshots.CountAsync();

            Assert.Equal(cityStatesAfterFirst, cityStatesAfterSecond);
            Assert.Equal(econSnapshotsAfterFirst, econSnapshotsAfterSecond);
        }

        [Fact]
        public async Task EmpireGenesis_CreatesEmpiresAndArmies()
        {
            var builder = WebApplication.CreateBuilder();
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "testdata6");
            Directory.CreateDirectory(dataDir);
            var dbPath = Path.Combine(dataDir, "test-imperium6.db");
            builder.Services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
            var app = builder.Build();

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            // Seed world up to civilizations
            await Imperium.Api.WorldGenesisService.InitializeAsync(db, scope.ServiceProvider);
            await Imperium.Infrastructure.Setup.NatureGenesisService.InitializeAsync(db, scope.ServiceProvider);
            await Imperium.Infrastructure.Setup.TribesGenesisService.InitializeAsync(db);
            await Imperium.Infrastructure.Setup.CivilizationGenesisService.InitializeAsync(db);

            // Run empires genesis (with mock LLM for predictable myths)
            var llm = new Imperium.Llm.MockLlmClient("{\"myth\": \"Основание было предсказано звездой\"}");
            await Imperium.Infrastructure.Setup.EmpireGenesisService.InitializeAsync(db, llm);

            var empires = await db.Factions.Where(f => f.Type == "empire").ToListAsync();
            Assert.True(empires.Count >= 1 && empires.Count <= 3, "Expected 1-3 empires created");

            // Armies exist
            var armies = await db.Army.Where(a => empires.Select(e => e.Id).Contains(a.FactionId)).ToListAsync();
            Assert.NotEmpty(armies);

            // Chronicle entry
            var chron = await db.WorldChronicles.OrderByDescending(c => c.Year).FirstOrDefaultAsync();
            Assert.NotNull(chron);

            // Rumors exist and include founding myth from LLM
            var rumors = await db.Rumors.Select(r => r.Content).ToListAsync();
            Assert.True(rumors.Any(r => r.Contains("Основание было предсказано звездой")), "Founding myth should be present as a rumor");

            // Parent relations: ensure city-states attached to empires
            var children = await db.Factions.Where(f => f.ParentFactionId != null).ToListAsync();
            Assert.NotEmpty(children);

            // Tax policy persisted on empires
            Assert.All(empires, e => Assert.False(string.IsNullOrWhiteSpace(e.TaxPolicyJson)));
        }

        [Fact]
        public async Task EmpireGenesis_IsIdempotent_OnSecondRun()
        {
            var builder = WebApplication.CreateBuilder();
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "testdata7");
            Directory.CreateDirectory(dataDir);
            var dbPath = Path.Combine(dataDir, "test-imperium7.db");
            builder.Services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
            var app = builder.Build();

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            // prerequisites
            await Imperium.Api.WorldGenesisService.InitializeAsync(db, scope.ServiceProvider);
            await Imperium.Infrastructure.Setup.NatureGenesisService.InitializeAsync(db, scope.ServiceProvider);
            await Imperium.Infrastructure.Setup.TribesGenesisService.InitializeAsync(db);
            await Imperium.Infrastructure.Setup.CivilizationGenesisService.InitializeAsync(db);

            // First run (with mock LLM)
            var llm2 = new Imperium.Llm.MockLlmClient("{\"myth\": \"Основана при свете кометы\"}");
            await Imperium.Infrastructure.Setup.EmpireGenesisService.InitializeAsync(db, llm2);
            var empireCount1 = await db.Factions.CountAsync(f => f.Type == "empire");
            var armiesCount1 = await db.Army.CountAsync();

            // Second run
            await Imperium.Infrastructure.Setup.EmpireGenesisService.InitializeAsync(db, llm2);
            var empireCount2 = await db.Factions.CountAsync(f => f.Type == "empire");
            var armiesCount2 = await db.Army.CountAsync();

            Assert.Equal(empireCount1, empireCount2);
            Assert.Equal(armiesCount1, armiesCount2);
        }

        [Fact]
        public async Task CivilizationGenesis_CreatesValidTradeRoutesAndReserves()
        {
            var builder = WebApplication.CreateBuilder();
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "testdata5");
            Directory.CreateDirectory(dataDir);
            var dbPath = Path.Combine(dataDir, "test-imperium5.db");
            builder.Services.AddDbContext<ImperiumDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
            var app = builder.Build();

            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();

            // Seed
            await Imperium.Api.WorldGenesisService.InitializeAsync(db, scope.ServiceProvider);
            await Imperium.Infrastructure.Setup.NatureGenesisService.InitializeAsync(db, scope.ServiceProvider);
            await Imperium.Infrastructure.Setup.TribesGenesisService.InitializeAsync(db);
            await Imperium.Infrastructure.Setup.CivilizationGenesisService.InitializeAsync(db);

            // Economy snapshots should exist and contain reserve info
            var snapshot = await db.EconomySnapshots.OrderBy(s => s.Timestamp).LastOrDefaultAsync();
            Assert.NotNull(snapshot);
            Assert.False(string.IsNullOrEmpty(snapshot.PricesJson));
            Assert.False(string.IsNullOrEmpty(snapshot.ResourcesJson));

            // Parse reserves and prices and verify expected keys
            var reservesDoc = JsonDocument.Parse(snapshot.ResourcesJson);
            var root = reservesDoc.RootElement;
            Assert.True(root.TryGetProperty("grain", out var grain) && grain.GetDecimal() > 0);
            Assert.True(root.TryGetProperty("metal", out var metal) && metal.GetDecimal() > 0);
            Assert.True(root.TryGetProperty("wood", out var wood) && wood.GetDecimal() > 0);

            var pricesDoc = JsonDocument.Parse(snapshot.PricesJson);
            var pRoot = pricesDoc.RootElement;
            Assert.True(pRoot.TryGetProperty("grain", out var _));
            Assert.True(pRoot.TryGetProperty("metal", out var _));

            // Rumors should mention at least one created city-state
            var cityNames = await db.Factions.Where(f => f.Type == "city_state").Select(f => f.Name).ToListAsync();
            var rumors = await db.Rumors.Select(r => r.Content).ToListAsync();
            Assert.NotEmpty(rumors);
            Assert.True(cityNames.Any(c => rumors.Any(r => r.Contains(c))), "At least one rumor should reference a created city-state");

            // Trades if any should have both buyer and seller
            var trades = await db.Trades.ToListAsync();
            if (trades.Any())
            {
                Assert.All(trades, t => Assert.True(t.BuyerId != Guid.Empty && t.SellerId != Guid.Empty));
            }

            // Trade routes persisted
            var routes = await db.TradeRoutes.ToListAsync();
            Assert.NotEmpty(routes);
        }
}
