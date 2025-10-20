using Imperium.Domain.Agents;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Imperium.Api.Agents;

public class LogisticsAgent : IWorldAgent
{
    public string Name => "LogisticsAI";

    private static decimal ComputeCost(Guid? from, Guid? to, decimal volume)
    {
        if (!from.HasValue || !to.HasValue) return Math.Round(0.2m * volume, 2);
        // pseudo-distance by XOR hash difference
        var d = Math.Abs(from.Value.GetHashCode() ^ to.Value.GetHashCode()) % 1000;
        var dist = 10m + (decimal)d / 10m; // 10..110
        return Math.Round((0.01m * dist + 0.05m) * volume, 2); // simple
    }

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var dispatcher = scopeServices.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();
        var since = DateTime.UtcNow.AddMinutes(-2);
        var jobs = await db.GameEvents
            .Where(e => e.Type == "transport_job" && e.Timestamp >= since)
            .OrderBy(e => e.Timestamp)
            .Take(10)
            .ToListAsync(ct);
        foreach (var j in jobs)
        {
            try
            {
                using var doc = JsonDocument.Parse(j.PayloadJson);
                var root = doc.RootElement;
                Guid? from = null, to = null;
                if (root.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String)
                {
                    if (Guid.TryParse(f.GetString(), out var g)) from = g;
                }
                if (root.TryGetProperty("to", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    if (Guid.TryParse(t.GetString(), out var g)) to = g;
                }
                var profit = root.TryGetProperty("profit", out var pr) && pr.ValueKind == JsonValueKind.Number ? pr.GetDecimal() : 0m;
                var volume = Math.Max(5m, Math.Min(20m, profit));
                var cost = ComputeCost(from, to, volume);

                if (from.HasValue)
                {
                    var city = await db.Locations.FindAsync(new object?[] { from.Value }, ct);
                    if (city != null && city.Treasury >= cost)
                    {
                        city.Treasury -= cost; // reserve & pay
                        await db.SaveChangesAsync(ct);
                        var reserved = new Imperium.Domain.Models.GameEvent
                        {
                            Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Type = "logistics_completed", Location = city.Name ?? "global",
                            PayloadJson = JsonSerializer.Serialize(new { job = j.Id, from = from, to = to, item = "grain", volume, cost })
                        };
                        await dispatcher.EnqueueAsync(reserved);
                    }
                }
            }
            catch { }
        }
    }
}

