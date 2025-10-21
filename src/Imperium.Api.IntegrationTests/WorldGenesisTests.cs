using System;
using System.Threading.Tasks;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Imperium.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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
}
