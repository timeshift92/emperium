
using Imperium.Llm;
using System.Text.Json;

namespace Imperium.Domain;

public interface IEconomyService
{
    void UpdateEconomy(AppDb db);
}

public interface IDecreeService
{
    Task<Decree> CreateAsync(AppDb db, string title, string content, CancellationToken ct);
    Task ApplyActiveAsync(AppDb db, CancellationToken ct);
}

public interface INpcService
{
    IEnumerable<Npc> SelectActive(AppDb db, int max = 3);
    Task ProcessNpcAsync(AppDb db, Npc npc, CancellationToken ct);
}

public class EconomyService : IEconomyService
{
    public void UpdateEconomy(AppDb db)
    {
        var last = db.Economy.OrderByDescending(e => e.Id).FirstOrDefault();
        var tick = (last?.Tick ?? 0) + 1;
        var stock = Math.Max(0, (last?.GrainStock ?? 1000) - new Random().Next(-5, 6));
        var price = Math.Max(0.1, (last?.GrainPrice ?? 1.0) + ((stock < 900) ? 0.02 : -0.01));
        var treasury = (last?.Treasury ?? 1000) + ((last?.TaxRate ?? 0.1) * 10);

        db.Economy.Add(new EconomySnapshot {
            Tick = tick,
            GrainStock = stock,
            GrainPrice = price,
            Treasury = treasury,
            TaxRate = last?.TaxRate ?? 0.1
        });
    }
}

public class DecreeService : IDecreeService
{
    private readonly ILlmClient _llm;
    public DecreeService(ILlmClient llm) => _llm = llm;

    public async Task<Decree> CreateAsync(AppDb db, string title, string content, CancellationToken ct)
    {
        var schema = "Extract a minimal JSON object describing this decree. " +
                     "Allowed keys: type (e.g. \"tax\"), target (e.g. \"grain\"), rate (0..1), earmark (string), notes (string). " +
                     "Return ONLY compact JSON.";

        var parsed = await _llm.StructuredJsonAsync(content, schema, ct);

        var decree = new Decree {
            Title = title,
            Content = content,
            ParsedJson = parsed,
            Status = DecreeStatus.Active,
            IssuedAt = DateTime.UtcNow,
            EffectiveAt = DateTime.UtcNow
        };
        db.Decrees.Add(decree);
        db.Events.Add(new GameEvent { Type = "decree_issued", PayloadJson = JsonSerializer.Serialize(new { decree = title }) });
        return decree;
    }

    public Task ApplyActiveAsync(AppDb db, CancellationToken ct)
    {
        var lastEconomy = db.Economy.OrderByDescending(e => e.Id).FirstOrDefault();
        var active = db.Decrees.Where(d => d.Status == DecreeStatus.Active).ToList();
        foreach (var d in active)
        {
            try {
                var json = JsonDocument.Parse(d.ParsedJson).RootElement;
                if (json.TryGetProperty("type", out var t) && t.GetString() == "tax"
                    && json.TryGetProperty("target", out var target) && target.GetString() == "grain")
                {
                    var rate = json.TryGetProperty("rate", out var r) ? r.GetDouble() : (lastEconomy?.TaxRate ?? 0.1);
                    if (lastEconomy != null) lastEconomy.TaxRate = rate;
                }
            } catch { /* ignore in MVP */ }
        }
        return Task.CompletedTask;
    }
}

public class NpcService : INpcService
{
    private readonly ILlmClient _llm;
    public NpcService(ILlmClient llm) => _llm = llm;

    public IEnumerable<Npc> SelectActive(AppDb db, int max = 3)
        => db.Npcs.OrderByDescending(n => n.Influence).Take(max).ToList();

    public async Task ProcessNpcAsync(AppDb db, Npc npc, CancellationToken ct)
    {
        var econ = db.Economy.OrderByDescending(e => e.Id).FirstOrDefault();
        var context = new {
            npc = new { npc.Name, npc.Role, npc.Influence, npc.Loyalty },
            economy = new { econ?.GrainPrice, econ?.TaxRate, econ?.Treasury }
        };
        var prompt = $"You are a {npc.Role} in ancient city. Briefly react (<= 25 words) to current tax and grain price from your role perspective.";
        var reply = await _llm.ShortReplyAsync(prompt, context, ct);
        var payload = System.Text.Json.JsonSerializer.Serialize(new { npc = npc.Name, role = npc.Role, reply });
        db.Events.Add(new GameEvent { Type = "reaction", PayloadJson = payload });
        npc.Loyalty = Math.Clamp(npc.Loyalty + (reply.Contains("support") ? 0.05 : -0.02), -1, 1);
    }
}
