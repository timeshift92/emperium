
using Microsoft.EntityFrameworkCore;
using Imperium.Domain;

namespace Imperium.Domain;

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    public DbSet<Decree> Decrees => Set<Decree>();
    public DbSet<Npc> Npcs => Set<Npc>();
    public DbSet<GameEvent> Events => Set<GameEvent>();
    public DbSet<EconomySnapshot> Economy => Set<EconomySnapshot>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Decree>().Property(p => p.Status).HasConversion<string>();
        b.Entity<Decree>().Property(p => p.ParsedJson).HasColumnType("TEXT");
        b.Entity<Npc>().Property(p => p.MemoryJson).HasColumnType("TEXT");
        b.Entity<GameEvent>().Property(p => p.PayloadJson).HasColumnType("TEXT");
    }
}
