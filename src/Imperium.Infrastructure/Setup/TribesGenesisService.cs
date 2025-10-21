using System.Text.Json;
using Imperium.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System;

namespace Imperium.Infrastructure.Setup;

public static class TribesGenesisService
{
    private static readonly Random rnd = new();

    public static async Task InitializeAsync(ImperiumDbContext db, CancellationToken ct = default)
    {
        if (await db.Factions.AnyAsync(f => f.Type == "tribe", ct))
            return; // already seeded

        Console.WriteLine("[TribesGenesis] Создание племён первой эпохи...");

        var locations = await db.Locations.ToArrayAsync(ct);
        if (locations.Length == 0)
        {
            Console.WriteLine("[TribesGenesis] Нет доступных локаций для размещения племён.");
            return;
        }

        int tribesCount = rnd.Next(5, 8); // 5-7 tribes
        var tribes = new List<Faction>();

        for (int i = 0; i < tribesCount; i++)
        {
            // pick a location biased by biome to create realistic placement
            var loc = locations[rnd.Next(locations.Length)];
            // prefer plains/coast for higher populations
            if (rnd.NextDouble() > 0.7)
            {
                var preferred = locations.Where(l => l.Biome == "plain" || l.Biome == "coast").ToArray();
                if (preferred.Length > 0) loc = preferred[rnd.Next(preferred.Length)];
            }
            var tribeName = GenerateTribeName(loc.Biome, rnd);
            var faction = new Faction { Name = tribeName, Type = "tribe" };
            tribes.Add(faction);
            db.Factions.Add(faction);

            // attributes
            // attributes (population, tech, livestock, subsistence)
            var population = rnd.Next(50, 800);
            var tech = rnd.Next(1, 5); // 1-4 primitive
            var livestock = rnd.Next(0, 300);
            var subsistence = loc.Biome switch
            {
                "forest" => "hunting",
                "plain" => "agriculture",
                "mountain" => "herding",
                "coast" => "fishing",
                _ => "mixed"
            };

            // create 2-3 characters: chief, shaman, warrior (optional)
            var members = new List<Character>();
            var chief = new Character { Id = Guid.NewGuid(), Name = tribeName + " Вождь", Gender = rnd.Next(0, 2) == 0 ? "male" : "female", LocationId = loc.Id };
            members.Add(chief);
            db.Characters.Add(chief);

            var shaman = new Character { Id = Guid.NewGuid(), Name = tribeName + " Шаман", Gender = rnd.Next(0, 2) == 0 ? "male" : "female", LocationId = loc.Id };
            members.Add(shaman);
            db.Characters.Add(shaman);

            if (rnd.NextDouble() > 0.3)
            {
                var warrior = new Character { Id = Guid.NewGuid(), Name = tribeName + " Воин", Gender = rnd.Next(0, 2) == 0 ? "male" : "female", LocationId = loc.Id };
                members.Add(warrior);
                db.Characters.Add(warrior);
            }

            // npc essences
            foreach (var c in members)
            {
                db.NpcEssences.Add(new NpcEssence
                {
                    CharacterId = c.Id,
                    Strength = rnd.Next(4, 9),
                    Intelligence = rnd.Next(3, 9),
                    Charisma = rnd.Next(3, 9),
                    Vitality = rnd.Next(4, 9),
                    Luck = rnd.Next(1, 7),
                    MutationChance = Math.Round(rnd.NextDouble() * 0.05, 3)
                });
            }

            // small ownerships
            db.Ownerships.Add(new Ownership { OwnerId = faction.Id, AssetId = Guid.NewGuid(), OwnerType = "faction", AssetType = "land", Confidence = 0.6, IsRecognized = true, AcquiredAt = DateTime.UtcNow, AcquisitionType = "initial" });

            // memories
            db.NpcMemories.Add(new NpcMemory { CharacterId = members[0].Id, KnownAssets = new List<Guid>(), LostAssets = new List<Guid>(), Greed = 0.2, Attachment = 0.5, LastUpdated = DateTime.UtcNow });

            // relationships (alliances/rivalries) — pairwise random between tribes created so far
            foreach (var other in tribes.Take(i))
            {
                var rel = new Relationship
                {
                    SourceId = faction.Id,
                    TargetId = other.Id,
                    Type = rnd.NextDouble() > 0.7 ? "rivalry" : "alliance",
                    Trust = rnd.Next(-10, 10),
                    Love = rnd.Next(-5, 10),
                    Hostility = rnd.Next(0, 10),
                    LastUpdated = DateTime.UtcNow
                };
                db.Relationships.Add(rel);
            }
            // per tribe event
            db.GameEvents.Add(new GameEvent { Timestamp = DateTime.UtcNow, Type = "tribe_seed", Location = loc.Name, PayloadJson = JsonSerializer.Serialize(new { summary = $"{tribeName} поселилось в {loc.Name} ({subsistence})", tribe = tribeName, location = loc.Name, population, tech, subsistence }) });
        }
        await db.SaveChangesAsync(ct);
        Console.WriteLine("[TribesGenesis] Племена созданы.");
    }

    private static string GenerateTribeName(string biome, Random rnd)
    {
        var syllables = biome switch
        {
            "forest" => new[] { "Ka", "ra", "na", "sha", "li", "mor" },
            "plain" => new[] { "Gra", "ta", "lo", "vin", "sha", "ur" },
            "mountain" => new[] { "Kag", "tor", "mar", "dal", "rug", "on" },
            "coast" => new[] { "Bel", "sea", "mar", "nar", "ri", "ca" },
            _ => new[] { "Tu", "na", "lu", "ra" }
        };
        var parts = Enumerable.Range(0, 2 + rnd.Next(2)).Select(_ => syllables[rnd.Next(syllables.Length)]);
        return string.Join(string.Empty, parts) + "i";
    }
}