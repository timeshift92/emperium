using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Imperium.Api;

public class TriggerReclaimRequest
{
    public Guid? AssetId { get; set; }
    public Guid? ClaimantId { get; set; }
    public string? Location { get; set; }
}

public static class DevEndpoints
{
    public static void MapDevOwnershipEndpoints(this WebApplication app)
    {
        app.MapGet("/api/dev/ownerships", async (Imperium.Infrastructure.ImperiumDbContext db) =>
        {
            var items = await db.Ownerships.OrderBy(o => o.AssetType).ToListAsync();
            return Results.Json(items);
        }).WithName("GetOwnerships");

        app.MapGet("/api/dev/ownerships/{id:guid}", async (Guid id, Imperium.Infrastructure.ImperiumDbContext db) =>
        {
            var o = await db.Ownerships.FindAsync(id);
            return o == null ? Results.NotFound() : Results.Json(o);
        }).WithName("GetOwnership");

        app.MapPost("/api/dev/ownerships", async (Imperium.Domain.Models.Ownership model, Imperium.Infrastructure.ImperiumDbContext db) =>
        {
            if (model.Id == Guid.Empty) model.Id = Guid.NewGuid();
            db.Ownerships.Add(model);
            await db.SaveChangesAsync();
            return Results.Created($"/api/dev/ownerships/{model.Id}", model);
        }).WithName("CreateOwnership");

        app.MapPut("/api/dev/ownerships/{id:guid}", async (Guid id, Imperium.Domain.Models.Ownership update, Imperium.Infrastructure.ImperiumDbContext db) =>
        {
            var existing = await db.Ownerships.FindAsync(id);
            if (existing == null) return Results.NotFound();
            existing.OwnerId = update.OwnerId;
            existing.Confidence = update.Confidence;
            existing.IsRecognized = update.IsRecognized;
            existing.AcquisitionType = update.AcquisitionType;
            existing.AssetType = update.AssetType;
            existing.OwnerType = update.OwnerType;
            await db.SaveChangesAsync();
            return Results.Ok(existing);
        }).WithName("UpdateOwnership");

        app.MapDelete("/api/dev/ownerships/{id:guid}", async (Guid id, Imperium.Infrastructure.ImperiumDbContext db) =>
        {
            var existing = await db.Ownerships.FindAsync(id);
            if (existing == null) return Results.NotFound();
            db.Ownerships.Remove(existing);
            await db.SaveChangesAsync();
            return Results.Ok();
        }).WithName("DeleteOwnership");

        // NpcMemory endpoints
        app.MapGet("/api/dev/npcmemories", async (Imperium.Infrastructure.ImperiumDbContext db) =>
        {
            var items = await db.NpcMemories.OrderBy(n => n.CharacterId).ToListAsync();
            return Results.Json(items);
        }).WithName("GetNpcMemories");

        app.MapGet("/api/dev/npcmemories/{id:guid}", async (Guid id, Imperium.Infrastructure.ImperiumDbContext db) =>
        {
            var n = await db.NpcMemories.FindAsync(id);
            return n == null ? Results.NotFound() : Results.Json(n);
        }).WithName("GetNpcMemory");

        app.MapPost("/api/dev/npcmemories", async (Imperium.Domain.Models.NpcMemory model, Imperium.Infrastructure.ImperiumDbContext db) =>
        {
            if (model.Id == Guid.Empty) model.Id = Guid.NewGuid();
            model.LastUpdated = DateTime.UtcNow;
            db.NpcMemories.Add(model);
            await db.SaveChangesAsync();
            return Results.Created($"/api/dev/npcmemories/{model.Id}", model);
        }).WithName("CreateNpcMemory");

        app.MapPut("/api/dev/npcmemories/{id:guid}", async (Guid id, Imperium.Domain.Models.NpcMemory update, Imperium.Infrastructure.ImperiumDbContext db) =>
        {
            var existing = await db.NpcMemories.FindAsync(id);
            if (existing == null) return Results.NotFound();
            existing.KnownAssets = update.KnownAssets ?? new List<Guid>();
            existing.LostAssets = update.LostAssets ?? new List<Guid>();
            existing.Greed = update.Greed;
            existing.Attachment = update.Attachment;
            existing.LastUpdated = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(existing);
        }).WithName("UpdateNpcMemory");

        app.MapDelete("/api/dev/npcmemories/{id:guid}", async (Guid id, Imperium.Infrastructure.ImperiumDbContext db) =>
        {
            var existing = await db.NpcMemories.FindAsync(id);
            if (existing == null) return Results.NotFound();
            db.NpcMemories.Remove(existing);
            await db.SaveChangesAsync();
            return Results.Ok();
        }).WithName("DeleteNpcMemory");

        // Dev: trigger an ownership reclaim attempt (for testing NPC reactions / conflicts)
        app.MapPost("/api/dev/trigger-reclaim", async (TriggerReclaimRequest req, Imperium.Infrastructure.ImperiumDbContext db) =>
        {
            var ev = new Imperium.Domain.Models.GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "ownership_reclaim_attempt",
                Location = req.Location ?? "unknown",
                PayloadJson = JsonSerializer.Serialize(new { assetId = req.AssetId, characterId = req.ClaimantId, note = "dev_trigger" })
            };
            db.GameEvents.Add(ev);
            await db.SaveChangesAsync();
            // return the persisted event for immediate inspection
            return Results.Ok(ev);
        }).WithName("TriggerReclaim");

    // Request shape is defined at file scope: TriggerReclaimRequest
    }
}
