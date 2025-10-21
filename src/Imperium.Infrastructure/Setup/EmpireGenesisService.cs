using Imperium.Domain.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Imperium.Infrastructure.Setup;

public static class EmpireGenesisService
{
    public static async Task InitializeAsync(ImperiumDbContext db, CancellationToken ct = default)
    {
        if (await db.Factions.AnyAsync(f => f.Type == "empire", ct))
            return; // already created

        // Find powerful city-states
        var candidates = await db.Factions.Where(f => f.Type == "city_state").ToListAsync(ct);
        if (!candidates.Any()) return;

        // Choose up to 3
        var selected = candidates.Take(3).ToList();

    foreach (var s in selected)
        {
            var empireName = s.Name.Contains("Дом") ? s.Name.Replace("Дом", "Империя") : s.Name + " Империя";
            var taxPolicy = new { trade = 0.05m, agriculture = 0.08m };
            var empire = new Faction { Id = Guid.NewGuid(), Name = empireName, Type = "empire", TaxPolicyJson = JsonSerializer.Serialize(taxPolicy) };
            db.Factions.Add(empire);

            // attach found city-state as child of the new empire
            s.ParentFactionId = empire.Id;

            // Armies
            var armyTypes = new[] { "infantry", "cavalry", "archers", "navy" };
            foreach (var t in armyTypes)
            {
                db.Army.Add(new Army { Id = Guid.NewGuid(), FactionId = empire.Id, Type = t, Manpower = 100 + (int)(Random.Shared.NextDouble() * 200), Morale = 0.6m + (decimal)(Random.Shared.NextDouble() * 0.4) });
            }

            // Legal code & tax policy (stored as JSON payloads in GameEvent for now)
            var laws = new { code = "Law of Markets", content = "Торговля регламентируется налогом на прибыль и пошлинами" };
            var taxes = taxPolicy;

            // Knowledge fields
            var fields = new[] { "Государственное управление", "Военное искусство", "Юриспруденция" };
            foreach (var f in fields)
            {
                if (!await db.KnowledgeFields.AnyAsync(k => k.Name == f, ct))
                {
                    db.KnowledgeFields.Add(new KnowledgeField { Id = Guid.NewGuid(), Name = f });
                }
            }

            // Characters
            db.Characters.Add(new Character { Id = Guid.NewGuid(), Name = "Император " + empireName, Status = "ruler" });
            db.Characters.Add(new Character { Id = Guid.NewGuid(), Name = "Верховный Жрец " + empireName, Status = "priest" });

            // Buildings
            db.Buildings.Add(new Building { Id = Guid.NewGuid(), LocationId = null, Kind = "palace" });
            db.Buildings.Add(new Building { Id = Guid.NewGuid(), LocationId = null, Kind = "forum" });
            db.Buildings.Add(new Building { Id = Guid.NewGuid(), LocationId = null, Kind = "arsenal" });

            // Chronicle
            db.WorldChronicles.Add(new WorldChronicle { Id = Guid.NewGuid(), Year = (await db.WorldTimes.Select(w => w.Year).FirstOrDefaultAsync(ct)), Summary = "Настала Эпоха Империй. Городские державы объединились в первые великие державы, установив законы и армии." });

            // Rumors
            db.Rumors.Add(new Rumor { Id = Guid.NewGuid(), Content = $"Император {empireName} говорит с богами." });
            db.Rumors.Add(new Rumor { Id = Guid.NewGuid(), Content = $"Великие войска {empireName} маршируют на границе." });

            // Log a simple game event for empire formation
            var payload = JsonSerializer.Serialize(new { empire = empire.Name, laws, taxes, parentCandidates = selected.Select(x => x.Name).ToArray() });
            db.GameEvents.Add(new GameEvent { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Type = "empire_formation", Location = "unknown", PayloadJson = payload });
        }

        await db.SaveChangesAsync(ct);
        Console.WriteLine("Эпоха Империй успешно создана.");
    }

    // Overload that accepts an LLM client to generate founding myths
    public static async Task InitializeAsync(ImperiumDbContext db, Imperium.Llm.ILlmClient? llm, CancellationToken ct = default)
    {
        // First create the empires normally
        await InitializeAsync(db, ct);

        if (llm == null) return;

        // For each empire, generate a short founding myth and persist as a rumor
        var empires = await db.Factions.Where(f => f.Type == "empire").ToListAsync(ct);
        foreach (var e in empires)
        {
            try
            {
                var prompt = $"Generate a short (1-2 sentences) founding myth in Russian for an ancient empire named '{e.Name}'. Return only JSON with a property named \"myth\".";
                var resp = await llm.SendPromptAsync(prompt, ct);
                // Defensive parse: try to extract 'myth' property from JSON
                using var doc = JsonDocument.Parse(resp);
                if (doc.RootElement.TryGetProperty("myth", out var mythEl))
                {
                    var myth = mythEl.GetString() ?? string.Empty;
                    db.Rumors.Add(new Rumor { Id = Guid.NewGuid(), Content = myth });
                }
            }
            catch
            {
                // ignore LLM failures for seeding
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
