using Imperium.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace Imperium.Infrastructure;

public class ImperiumDbContext : DbContext
{
    public ImperiumDbContext(DbContextOptions<ImperiumDbContext> options) : base(options) { }

    public DbSet<Character> Characters { get; set; } = null!;
    public DbSet<Family> Families { get; set; } = null!;
    public DbSet<Location> Locations { get; set; } = null!;
    public DbSet<EconomySnapshot> EconomySnapshots { get; set; } = null!;
    public DbSet<WeatherSnapshot> WeatherSnapshots { get; set; } = null!;
    public DbSet<SeasonState> SeasonStates { get; set; } = null!;
    public DbSet<GameEvent> GameEvents { get; set; } = null!;
    public DbSet<WorldTime> WorldTimes { get; set; } = null!;
    public DbSet<GameAction> GameActions { get; set; } = null!;
    public DbSet<CrimeRecord> CrimeRecords { get; set; } = null!;
    public DbSet<Relationship> Relationships { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // Простая конфигурация, позже можно расширить
        modelBuilder.Entity<Character>().HasKey(c => c.Id);
        modelBuilder.Entity<Family>().HasKey(f => f.Id);
        modelBuilder.Entity<Relationship>().HasIndex(r => new { r.SourceId, r.TargetId }).IsUnique();
    }
}
