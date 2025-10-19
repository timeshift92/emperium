using System.Text.Json;
using Imperium.Domain.Agents;
using Imperium.Domain.Models;
using Imperium.Domain.Services;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Imperium.Api.Agents;

/// <summary>
/// RelationshipAI: управляет отношениями между NPC и генерирует социальные события.
/// </summary>
public class RelationshipAgent : IWorldAgent
{
    public string Name => "RelationshipAI";

    private const int MaxSample = 12;
    private const int ClampMin = -100;
    private const int ClampMax = 100;

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var dispatcher = scopeServices.GetRequiredService<IEventDispatcher>();
        var metrics = scopeServices.GetRequiredService<MetricsService>();

        var characters = await db.Characters
            .OrderBy(c => c.Name)
            .Take(MaxSample)
            .ToListAsync(ct);

        if (characters.Count < 2)
        {
            return;
        }

        var ids = characters.Select(c => c.Id).ToList();
        var relations = await db.Relationships
            .Where(r => ids.Contains(r.SourceId) && ids.Contains(r.TargetId))
            .ToListAsync(ct);

        var random = Random.Shared;
        var producedEvents = new List<GameEvent>();

        Relationship EnsureRelationship(Guid source, Guid target)
        {
            var rel = relations.FirstOrDefault(r => r.SourceId == source && r.TargetId == target);
            if (rel != null) return rel;

            rel = new Relationship
            {
                Id = Guid.NewGuid(),
                SourceId = source,
                TargetId = target,
                Type = "acquaintance",
                Trust = 0,
                Love = 0,
                Hostility = 0,
                LastUpdated = DateTime.UtcNow
            };
            relations.Add(rel);
            db.Relationships.Add(rel);
            return rel;
        }

        static int Clamp(int value) => Math.Clamp(value, ClampMin, ClampMax);

        for (var i = 0; i < Math.Min(characters.Count / 2, 6); i++)
        {
            var a = characters[random.Next(characters.Count)];
            var b = characters[random.Next(characters.Count)];
            if (a.Id == b.Id) continue;

            var relAB = EnsureRelationship(a.Id, b.Id);
            var relBA = EnsureRelationship(b.Id, a.Id);

            // Базовая динамика чувств
            var moodSwing = random.Next(-4, 5); // [-4, 4]
            relAB.Trust = Clamp(relAB.Trust + moodSwing);
            relBA.Trust = Clamp(relBA.Trust + moodSwing);

            if (moodSwing >= 3)
            {
                relAB.Love = Clamp(relAB.Love + random.Next(1, 4));
                relBA.Love = Clamp(relBA.Love + random.Next(1, 4));
                relAB.Hostility = Clamp(relAB.Hostility - 1);
                relBA.Hostility = Clamp(relBA.Hostility - 1);
            }
            else if (moodSwing <= -3)
            {
                relAB.Hostility = Clamp(relAB.Hostility + random.Next(2, 6));
                relBA.Hostility = Clamp(relBA.Hostility + random.Next(2, 6));
                relAB.Love = Clamp(relAB.Love - 1);
                relBA.Love = Clamp(relBA.Love - 1);
            }

            relAB.LastUpdated = DateTime.UtcNow;
            relBA.LastUpdated = DateTime.UtcNow;

            // Проверка на брак
            if (relAB.Type != "married" && relBA.Type != "married")
            {
                var loveScore = Math.Min(relAB.Love, relBA.Love);
                var trustScore = Math.Min(relAB.Trust, relBA.Trust);
                if (loveScore >= 70 && trustScore >= 50 && random.NextDouble() < 0.05)
                {
                    relAB.Type = "married";
                    relBA.Type = "married";
                    relAB.Trust = Clamp(relAB.Trust + 10);
                    relBA.Trust = Clamp(relBA.Trust + 10);

                    var marriage = new GameEvent
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = DateTime.UtcNow,
                        Type = "marriage",
                        Location = a.LocationName ?? "global",
                        PayloadJson = JsonSerializer.Serialize(new
                        {
                            spouseA = a.Id,
                            spouseB = b.Id,
                            names = new[] { a.Name, b.Name },
                            trust = trustScore,
                            love = loveScore
                        })
                    };
                    producedEvents.Add(marriage);
                    metrics.Increment("relationships.marriage");
                }
            }

            // Проверка на предательство/разрыв
            var hostilityScore = Math.Max(relAB.Hostility, relBA.Hostility);
            if (hostilityScore >= 70 && random.NextDouble() < 0.04)
            {
                relAB.Type = "enmity";
                relBA.Type = "enmity";
                relAB.Trust = Clamp(relAB.Trust - 20);
                relBA.Trust = Clamp(relBA.Trust - 20);
                relAB.Love = Clamp(relAB.Love - 15);
                relBA.Love = Clamp(relBA.Love - 15);

                var betrayal = new GameEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    Type = "betrayal",
                    Location = a.LocationName ?? "global",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        aggressorId = a.Id,
                        victimId = b.Id,
                        names = new[] { a.Name, b.Name },
                        hostility = hostilityScore
                    })
                };
                producedEvents.Add(betrayal);
                metrics.Increment("relationships.betrayal");
            }

            // Рождение ребёнка только для супружеских пар
            if (relAB.Type == "married" && relBA.Type == "married")
            {
                if (relAB.Love >= 80 && relBA.Love >= 80 && random.NextDouble() < 0.03)
                {
                    relAB.Trust = Clamp(relAB.Trust + 5);
                    relBA.Trust = Clamp(relBA.Trust + 5);

                    var child = new GameEvent
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = DateTime.UtcNow,
                        Type = "child_birth",
                        Location = a.LocationName ?? "global",
                        PayloadJson = JsonSerializer.Serialize(new
                        {
                            parentA = a.Id,
                            parentB = b.Id,
                            familyName = a.LocationName ?? "не указан",
                            joy = Math.Min(relAB.Love, relBA.Love)
                        })
                    };
                    producedEvents.Add(child);
                    metrics.Increment("relationships.child_birth");
                }
            }
        }

        await db.SaveChangesAsync(ct);

        foreach (var ev in producedEvents)
        {
            await dispatcher.EnqueueAsync(ev);
        }
    }
}
