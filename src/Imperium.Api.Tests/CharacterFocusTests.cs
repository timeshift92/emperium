
using System;
using System.Linq;
using System.Threading.Tasks;
using Imperium.Domain.Models;
using Imperium.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Imperium.Api.Tests;

public class CharacterFocusTests
{
    private static ImperiumDbContext CreateContext(SqliteConnection conn)
    {
        var options = new DbContextOptionsBuilder<ImperiumDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new ImperiumDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task RelationshipsQuery_ReturnsTopRelations()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        await using var db = CreateContext(conn);

        var main = new Character { Id = Guid.NewGuid(), Name = "Marcus", Status = "idle" };
        var other = new Character { Id = Guid.NewGuid(), Name = "Livia", Status = "idle" };
        var third = new Character { Id = Guid.NewGuid(), Name = "Decimus", Status = "idle" };
        db.Characters.AddRange(main, other, third);
        db.Relationships.AddRange(
            new Relationship { Id = Guid.NewGuid(), SourceId = main.Id, TargetId = other.Id, Trust = 80, Love = 60, Hostility = 5, Type = "friend" },
            new Relationship { Id = Guid.NewGuid(), SourceId = other.Id, TargetId = main.Id, Trust = 70, Love = 55, Hostility = 5, Type = "friend" },
            new Relationship { Id = Guid.NewGuid(), SourceId = main.Id, TargetId = third.Id, Trust = -40, Love = 0, Hostility = 75, Type = "enemy" }
        );
        await db.SaveChangesAsync();

        var rels = await db.Relationships
            .Where(r => r.SourceId == main.Id)
            .OrderByDescending(r => Math.Abs(r.Trust) + Math.Abs(r.Love) + Math.Abs(r.Hostility))
            .Take(8)
            .ToListAsync();

        Assert.Equal(2, rels.Count);
        var top = rels.First();
        Assert.Equal(third.Id, top.TargetId);
        Assert.True(Math.Abs(top.Hostility) >= Math.Abs(top.Trust));
    }

    [Fact]
    public async Task CommunicationsQuery_FiltersByPartnerAndLocation()
    {
        using var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        await using var db = CreateContext(conn);

        var main = new Character { Id = Guid.NewGuid(), Name = "Marcus", LocationName = "Roma" };
        var partner = new Character { Id = Guid.NewGuid(), Name = "Livia", LocationName = "Roma" };
        db.Characters.AddRange(main, partner);

        var payload = $"{{\"participants\": [\"{main.Id}\", \"{partner.Id}\"], \"dialog\": \"In foro\" }}";
        db.GameEvents.AddRange(
            new GameEvent { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Type = "npc_reply", Location = "Roma", PayloadJson = payload },
            new GameEvent { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Type = "npc_reply", Location = "Athenae", PayloadJson = $"{{\"participants\": [\"{main.Id}\"], \"dialog\": \"lonely\"}}" }
        );
        await db.SaveChangesAsync();

        var idStr = main.Id.ToString();
        var baseQuery = db.GameEvents
            .Where(e => (e.Type == "npc_reply" || e.Type == "npc_reaction") && e.PayloadJson.Contains(idStr));

        var sameLocationQuery = baseQuery.Where(e => e.Location == main.LocationName);
        var filtered = await sameLocationQuery.ToListAsync();
        Assert.Single(filtered);
        Assert.Contains(partner.Id.ToString(), filtered[0].PayloadJson);

        var partnerFiltered = filtered.Where(e => e.PayloadJson.Contains(partner.Id.ToString())).ToList();
        Assert.Single(partnerFiltered);
    }
}
