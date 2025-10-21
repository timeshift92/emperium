using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Imperium.Domain.Agents;
using Imperium.Domain.Models;
using Imperium.Domain.Services;
using Imperium.Domain.Utils;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;

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
    private static readonly object GenderBiasLock = new();
    private static readonly Dictionary<(Guid Source, Guid Target), double> GenderBiasResiduals = new();

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var dispatcher = scopeServices.GetRequiredService<IEventDispatcher>();
        var metrics = scopeServices.GetRequiredService<MetricsService>();
        var relOptions = scopeServices.GetService<Microsoft.Extensions.Options.IOptions<RelationshipModifierOptions>>()?.Value;

        static string AddGuidToJsonArray(string? json, Guid id)
        {
            try
            {
                var node = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "[]" : json) as JsonArray ?? new JsonArray();
                var exists = node.Any(x => Guid.TryParse(x?.ToString(), out var g) && g == id);
                if (!exists)
                {
                    node.Add(id.ToString());
                }
                return node.ToJsonString();
            }
            catch
            {
                return JsonSerializer.Serialize(new[] { id });
            }
        }

        static (Character first, Character second) OrderPair(Character a, Character b)
        {
            return string.Compare(a.Name, b.Name, StringComparison.Ordinal) <= 0 ? (a, b) : (b, a);
        }

        async Task<GenealogyRecord> GetOrCreateGenealogyAsync(Guid characterId)
        {
            var record = await db.GenealogyRecords.FirstOrDefaultAsync(g => g.CharacterId == characterId);
            if (record == null)
            {
                record = new GenealogyRecord
                {
                    Id = Guid.NewGuid(),
                    CharacterId = characterId,
                    SpouseIdsJson = "[]",
                    ChildrenIdsJson = "[]"
                };
                db.GenealogyRecords.Add(record);
            }
            return record;
        }

        async Task<Household> EnsureHouseholdForPairAsync(Character a, Character b)
        {
            var (first, second) = OrderPair(a, b);
            var name = $"{first.Name}-{second.Name} Household";
            var household = await db.Households.FirstOrDefaultAsync(h => h.Name == name);
            if (household == null)
            {
                household = new Household
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    LocationId = first.LocationId ?? second.LocationId,
                    HeadId = first.Id,
                    MemberIdsJson = "[]",
                    Wealth = 0m
                };
                db.Households.Add(household);
            }

            household.MemberIdsJson = AddGuidToJsonArray(household.MemberIdsJson, first.Id);
            household.MemberIdsJson = AddGuidToJsonArray(household.MemberIdsJson, second.Id);
            return household;
        }

        async Task<Family> EnsureFamilyForHouseholdAsync(Household household)
        {
            var family = await db.Families.FirstOrDefaultAsync(f => f.Name == household.Name);
            if (family == null)
            {
                family = new Family
                {
                    Id = Guid.NewGuid(),
                    Name = household.Name,
                    MemberIds = new List<Guid>(),
                    Wealth = household.Wealth
                };
                db.Families.Add(family);
            }

            family.MemberIds ??= new List<Guid>();
            return family;
        }

        static void EnsureFamilyMembers(Family family, params Guid[] memberIds)
        {
            foreach (var member in memberIds)
            {
                if (member == Guid.Empty) continue;
                if (!family.MemberIds.Contains(member))
                {
                    family.MemberIds.Add(member);
                }
            }
        }

        var characters = await db.Characters
            .OrderBy(c => c.Name)
            .Take(MaxSample)
            .ToListAsync();

        if (characters.Count < 2)
        {
            return;
        }

        var ids = characters.Select(c => c.Id).ToList();
        var relations = await db.Relationships
            .Where(r => ids.Contains(r.SourceId) && ids.Contains(r.TargetId))
            .ToListAsync();

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
            var biasAB = ResolveGenderBias(a, b, relOptions);
            var biasBA = ResolveGenderBias(b, a, relOptions);
            var biasDeltaAB = ConsumeGenderBias(a.Id, b.Id, biasAB);
            var biasDeltaBA = ConsumeGenderBias(b.Id, a.Id, biasBA);
            relAB.Trust = Clamp(relAB.Trust + moodSwing + biasDeltaAB);
            relBA.Trust = Clamp(relBA.Trust + moodSwing + biasDeltaBA);

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

                    var household = await EnsureHouseholdForPairAsync(a, b);
                    var family = await EnsureFamilyForHouseholdAsync(household);
                    EnsureFamilyMembers(family, a.Id, b.Id);
                    family.Wealth = household.Wealth;

                    var recordA = await GetOrCreateGenealogyAsync(a.Id);
                    recordA.SpouseIdsJson = AddGuidToJsonArray(recordA.SpouseIdsJson, b.Id);

                    var recordB = await GetOrCreateGenealogyAsync(b.Id);
                    recordB.SpouseIdsJson = AddGuidToJsonArray(recordB.SpouseIdsJson, a.Id);
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

                        var (orderedA, orderedB) = OrderPair(a, b);
                        var household = await EnsureHouseholdForPairAsync(orderedA, orderedB);
                        var family = await EnsureFamilyForHouseholdAsync(household);

                        var childId = Guid.NewGuid();
                        var childName = $"{orderedA.Name.Split(' ')[0]}-{orderedB.Name.Split(' ')[0]} child";
                        var newborn = new Character
                        {
                            Id = childId,
                            Name = childName,
                            Age = 0,
                            Status = "infant",
                            LocationId = orderedA.LocationId ?? orderedB.LocationId,
                            LocationName = orderedA.LocationName ?? orderedB.LocationName,
                            History = $"Child of {orderedA.Name} and {orderedB.Name}"
                        };
                        db.Characters.Add(newborn);

                        household.MemberIdsJson = AddGuidToJsonArray(household.MemberIdsJson, childId);
                        EnsureFamilyMembers(family, childId);
                        family.Wealth = household.Wealth;

                        var parentARecord = await GetOrCreateGenealogyAsync(orderedA.Id);
                        parentARecord.ChildrenIdsJson = AddGuidToJsonArray(parentARecord.ChildrenIdsJson, childId);

                        var parentBRecord = await GetOrCreateGenealogyAsync(orderedB.Id);
                        parentBRecord.ChildrenIdsJson = AddGuidToJsonArray(parentBRecord.ChildrenIdsJson, childId);

                        var childRecord = await GetOrCreateGenealogyAsync(childId);
                        childRecord.FatherId = orderedA.Id;
                        childRecord.MotherId = orderedB.Id;

                        var birthEvent = new GameEvent
                        {
                            Id = Guid.NewGuid(),
                            Timestamp = DateTime.UtcNow,
                            Type = "child_birth",
                            Location = orderedA.LocationName ?? "global",
                            PayloadJson = JsonSerializer.Serialize(new
                            {
                                parentA = orderedA.Id,
                                parentB = orderedB.Id,
                                childId,
                                childName,
                                familyName = orderedA.LocationName ?? "family",
                                joy = Math.Min(relAB.Love, relBA.Love)
                            })
                        };
                        producedEvents.Add(birthEvent);
                        metrics.Increment("relationships.child_birth");
                    }
                }
            }

            await db.SaveChangesAsync();

            foreach (var ev in producedEvents)
            {
                await dispatcher.EnqueueAsync(ev);
            }
        }
    }

    private static double ResolveGenderBias(Character source, Character target, RelationshipModifierOptions? options)
    {
        if (options == null) return 0;
        var from = GenderHelper.Normalize(source.Gender);
        var to = GenderHelper.Normalize(target.Gender);
        if (from == null || to == null) return 0;
        if (from == to)
        {
            return options.Resolve("same");
        }
        if (from == "male" && to == "female")
        {
            return options.Resolve("male->female");
        }
        if (from == "female" && to == "male")
        {
            return options.Resolve("female->male");
        }
        return 0;
    }

    private static int ConsumeGenderBias(Guid sourceId, Guid targetId, double bias)
    {
        if (bias == 0) return 0;
        lock (GenderBiasLock)
        {
            var key = (sourceId, targetId);
            GenderBiasResiduals.TryGetValue(key, out var residual);
            residual += bias;
            int delta = 0;
            if (residual >= 1)
            {
                delta = (int)Math.Floor(residual);
                residual -= delta;
            }
            else if (residual <= -1)
            {
                delta = (int)Math.Ceiling(residual);
                residual -= delta;
            }

            if (Math.Abs(residual) < 1e-6)
            {
                GenderBiasResiduals.Remove(key);
            }
            else
            {
                GenderBiasResiduals[key] = residual;
            }
            return delta;
        }
    }

}

