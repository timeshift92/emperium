using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Imperium.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Imperium.Infrastructure;

public class ImperiumDbContext : DbContext
{
    public ImperiumDbContext(DbContextOptions<ImperiumDbContext> options) : base(options) { }

    public DbSet<Character> Characters { get; set; } = null!;
    public DbSet<Family> Families { get; set; } = null!;
    public DbSet<Location> Locations { get; set; } = null!;
    public DbSet<Faction> Factions { get; set; } = null!;
    public DbSet<NpcEssence> NpcEssences { get; set; } = null!;
    public DbSet<EconomySnapshot> EconomySnapshots { get; set; } = null!;
    public DbSet<WeatherSnapshot> WeatherSnapshots { get; set; } = null!;
    public DbSet<Building> Buildings { get; set; } = null!;
    public DbSet<KnowledgeField> KnowledgeFields { get; set; } = null!;
    public DbSet<WorldChronicle> WorldChronicles { get; set; } = null!;
    public DbSet<Rumor> Rumors { get; set; } = null!;
    public DbSet<SeasonState> SeasonStates { get; set; } = null!;
    public DbSet<GameEvent> GameEvents { get; set; } = null!;
    public DbSet<WorldTime> WorldTimes { get; set; } = null!;
    public DbSet<GameAction> GameActions { get; set; } = null!;
    public DbSet<CrimeRecord> CrimeRecords { get; set; } = null!;
    public DbSet<Relationship> Relationships { get; set; } = null!;
    public DbSet<Household> Households { get; set; } = null!;
    public DbSet<GenealogyRecord> GenealogyRecords { get; set; } = null!;
    public DbSet<Imperium.Domain.Models.Inventory> Inventories { get; set; } = null!;
    public DbSet<Imperium.Domain.Models.MarketOrder> MarketOrders { get; set; } = null!;
    public DbSet<Imperium.Domain.Models.Trade> Trades { get; set; } = null!;
    public DbSet<Imperium.Domain.Models.InheritanceRecord> InheritanceRecords { get; set; } = null!;
    public DbSet<Ownership> Ownerships { get; set; } = null!;
    public DbSet<NpcMemory> NpcMemories { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Простая конфигурация, позже можно расширить
        modelBuilder.Entity<Character>().HasKey(c => c.Id);
        modelBuilder.Entity<Family>().HasKey(f => f.Id);
        modelBuilder.Entity<Household>().HasKey(h => h.Id);
    modelBuilder.Entity<GenealogyRecord>().HasKey(g => g.Id);
    modelBuilder.Entity<Imperium.Domain.Models.InheritanceRecord>().HasKey(i => i.Id);
        modelBuilder.Entity<Relationship>().HasIndex(r => new { r.SourceId, r.TargetId }).IsUnique();
        modelBuilder.Entity<Ownership>().HasKey(o => o.Id);
        modelBuilder.Entity<NpcMemory>().HasKey(m => m.Id);
        modelBuilder.Entity<Imperium.Domain.Models.Inventory>().HasKey(i => i.Id);
        modelBuilder.Entity<Imperium.Domain.Models.MarketOrder>().HasKey(o => o.Id);
        modelBuilder.Entity<Imperium.Domain.Models.Trade>().HasKey(t => t.Id);
        modelBuilder.Entity<Imperium.Domain.Models.MarketOrder>().HasIndex(o => new { o.LocationId, o.Item, o.Side, o.Price });

        var guidListConverter = new ValueConverter<List<Guid>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => string.IsNullOrWhiteSpace(v)
                ? new List<Guid>()
                : JsonSerializer.Deserialize<List<Guid>>(v) ?? new List<Guid>());

        var guidListComparer = new ValueComparer<List<Guid>>(
            (c1, c2) => (c1 ?? new List<Guid>()).SequenceEqual(c2 ?? new List<Guid>()),
            c => (c ?? new List<Guid>()).Aggregate(0, (hash, guid) => HashCode.Combine(hash, guid.GetHashCode())),
            c => (c ?? new List<Guid>()).ToList());

        modelBuilder.Entity<NpcMemory>()
            .Property(m => m.KnownAssets)
            .HasConversion(guidListConverter)
            .Metadata.SetValueComparer(guidListComparer);

        modelBuilder.Entity<NpcMemory>()
            .Property(m => m.LostAssets)
            .HasConversion(guidListConverter)
            .Metadata.SetValueComparer(guidListComparer);
    }
}
