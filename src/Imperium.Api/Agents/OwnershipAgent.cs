using Imperium.Domain.Agents;
using Imperium.Domain.Models;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Imperium.Api.Agents;

/// <summary>
/// Управление владениями: спорные активы, перераспределение и попытки возврата.
/// </summary>
public class OwnershipAgent : IWorldAgent
{
    public string Name => "OwnershipAI";

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var metrics = scopeServices.GetRequiredService<Imperium.Api.MetricsService>();
        var dispatcher = scopeServices.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();

        var rand = Random.Shared;

        // 0. Попытки вернуть утраченные активы (мягкое давление)
    var npcMemories = await db.NpcMemories.AsNoTracking().ToListAsync();
        foreach (var mem in npcMemories)
        {
            if (mem.LostAssets == null || mem.LostAssets.Count == 0) continue;
            foreach (var lostAsset in mem.LostAssets)
            {
                if (rand.NextDouble() < 0.1)
                {
                    var evReclaim = new GameEvent
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = DateTime.UtcNow,
                        Type = "ownership_reclaim_attempt",
                        Location = "unknown",
                        PayloadJson = JsonSerializer.Serialize(new
                        {
                            characterId = mem.CharacterId,
                            assetId = lostAsset
                        })
                    };
                    await dispatcher.EnqueueAsync(evReclaim);
                    metrics.Increment("ownership.reclaim_attempt");
                }
            }
        }

        // 1. Спорные владения -> уведомления
    var disputed = await db.Ownerships.Where(o => o.Confidence < 0.5).ToListAsync();
        foreach (var own in disputed)
        {
            var ev = new GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "ownership_dispute",
                Location = own.AssetType,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    ownerId = own.OwnerId,
                    assetId = own.AssetId,
                    confidence = own.Confidence,
                    acquisitionType = own.AcquisitionType
                })
            };
            await dispatcher.EnqueueAsync(ev);
        }
        if (disputed.Count > 0)
        {
            metrics.Increment("ownership.disputes");
        }

        // 2. Перераспределение собственности в пользу реальных персонажей
        var toTransfer = await db.Ownerships
            .Where(o => o.Confidence > 0.8 && o.AcquisitionType == "purchase")
            .ToListAsync();
        var characterIds = await db.Characters.Select(c => c.Id).ToListAsync();

        foreach (var own in toTransfer)
        {
            var prevOwner = own.OwnerId;
            Guid newOwner = characterIds.Count > 0
                ? characterIds[rand.Next(characterIds.Count)]
                : Guid.NewGuid();

            var roll = rand.NextDouble();
            string newAcq = roll switch
            {
                < 0.6 => "inheritance",
                < 0.8 => "gift",
                < 0.95 => "conquest",
                _ => "confiscation"
            };

            own.OwnerId = newOwner;
            own.Confidence = 1.0;
            own.AcquisitionType = newAcq;
            own.AcquiredAt = DateTime.UtcNow;

            // If transfer happened via inheritance, record it for audit/UX
            if (newAcq == "inheritance")
            {
                try
                {
                    var heirs = new[] { newOwner };
                    var rec = new Imperium.Domain.Models.InheritanceRecord
                    {
                        Id = Guid.NewGuid(),
                        DeceasedId = prevOwner,
                        HeirsJson = JsonSerializer.Serialize(heirs),
                        RulesJson = JsonSerializer.Serialize(new { type = "equal_split", assetId = own.AssetId })
                    };
                    db.InheritanceRecords.Add(rec);

                    // Directly create an Ownership entry for the asset (if not exists) to reflect inheritance
                    var existsOwn = await db.Ownerships.FirstOrDefaultAsync(o => o.AssetId == own.AssetId);
                    if (existsOwn == null)
                    {
                        var newOwnership = new Ownership
                        {
                            Id = Guid.NewGuid(),
                            OwnerId = newOwner,
                            AssetId = own.AssetId,
                            OwnerType = "Character",
                            AssetType = own.AssetType,
                            Confidence = 1.0,
                            IsRecognized = true,
                            AcquisitionType = "inheritance",
                            AcquiredAt = DateTime.UtcNow
                        };
                        db.Ownerships.Add(newOwnership);

                        var evCreate = new GameEvent
                        {
                            Id = Guid.NewGuid(),
                            Timestamp = DateTime.UtcNow,
                            Type = "ownership_created",
                            Location = own.AssetType,
                            PayloadJson = JsonSerializer.Serialize(new { assetId = own.AssetId, newOwner, acquisitionType = "inheritance" })
                        };
                        await dispatcher.EnqueueAsync(evCreate);
                        metrics.Increment("ownership.created");
                    }
                    else
                    {
                        if (existsOwn.OwnerId == newOwner)
                        {
                            // owner already set to the same character -> confirm/increase confidence
                            existsOwn.Confidence = Math.Min(1.0, existsOwn.Confidence + 0.1);
                            existsOwn.AcquisitionType = "inheritance";
                            existsOwn.AcquiredAt = DateTime.UtcNow;
                            existsOwn.IsRecognized = true;

                            var evConfirm = new GameEvent
                            {
                                Id = Guid.NewGuid(),
                                Timestamp = DateTime.UtcNow,
                                Type = "ownership_confirmed",
                                Location = own.AssetType,
                                PayloadJson = JsonSerializer.Serialize(new { assetId = own.AssetId, owner = newOwner })
                            };
                            await dispatcher.EnqueueAsync(evConfirm);
                            metrics.Increment("ownership.confirmed");
                        }
                        else
                        {
                            var prev = existsOwn.OwnerId;
                            existsOwn.OwnerId = newOwner;
                            existsOwn.AcquisitionType = "inheritance";
                            existsOwn.Confidence = Math.Max(existsOwn.Confidence, 0.9);
                            existsOwn.AcquiredAt = DateTime.UtcNow;
                            existsOwn.IsRecognized = true;

                            var evReassign = new GameEvent
                            {
                                Id = Guid.NewGuid(),
                                Timestamp = DateTime.UtcNow,
                                Type = "ownership_reassigned",
                                Location = own.AssetType,
                                PayloadJson = JsonSerializer.Serialize(new { assetId = own.AssetId, prevOwner = prev, newOwner })
                            };
                            await dispatcher.EnqueueAsync(evReassign);
                            metrics.Increment("ownership.reassigned");
                        }
                    }

                    var evInh = new GameEvent
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = DateTime.UtcNow,
                        Type = "inheritance_recorded",
                        Location = own.AssetType,
                        PayloadJson = JsonSerializer.Serialize(new { inheritanceId = rec.Id, assetId = own.AssetId, deceasedId = prevOwner, heirId = newOwner })
                    };
                    await dispatcher.EnqueueAsync(evInh);
                    metrics.Increment("ownership.inheritance_recorded");
                }
                catch { metrics.Increment("ownership.inheritance_errors"); }
            }

            var prevMem = await db.NpcMemories.FirstOrDefaultAsync(m => m.CharacterId == prevOwner);
            if (prevMem != null)
            {
                if (!prevMem.LostAssets.Contains(own.AssetId))
                {
                    prevMem.LostAssets.Add(own.AssetId);
                }
                prevMem.Greed = Math.Min(1.0, prevMem.Greed + (newAcq == "conquest" ? 0.05 : 0.15));
                prevMem.Attachment = Math.Min(1.0, prevMem.Attachment + (newAcq == "gift" ? 0.02 : 0.08));
                prevMem.LastUpdated = DateTime.UtcNow;
            }

            var newMem = await db.NpcMemories.FirstOrDefaultAsync(m => m.CharacterId == newOwner);
            if (newMem == null)
            {
                newMem = new NpcMemory
                {
                    Id = Guid.NewGuid(),
                    CharacterId = newOwner,
                    Greed = 0.5,
                    Attachment = 0.5,
                    LastUpdated = DateTime.UtcNow
                };
                db.NpcMemories.Add(newMem);
            }

            if (!newMem.KnownAssets.Contains(own.AssetId))
            {
                newMem.KnownAssets.Add(own.AssetId);
            }
            newMem.LastUpdated = DateTime.UtcNow;

            var evTransfer = new GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "ownership_transfer",
                Location = own.AssetType,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    assetId = own.AssetId,
                    prevOwner,
                    newOwner,
                    acquisitionType = own.AcquisitionType
                })
            };
            await dispatcher.EnqueueAsync(evTransfer);

            if (prevMem != null)
            {
                var evLoss = new GameEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    Type = "ownership_lost",
                    Location = own.AssetType,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        assetId = own.AssetId,
                        characterId = prevOwner
                    })
                };
                await dispatcher.EnqueueAsync(evLoss);
            }

            var evGain = new GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "ownership_gained",
                Location = own.AssetType,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    assetId = own.AssetId,
                    characterId = newOwner
                })
            };
            await dispatcher.EnqueueAsync(evGain);
        }

        if (toTransfer.Count > 0)
        {
            metrics.Increment("ownership.transfers");
        }

    await db.SaveChangesAsync();
    }
}
