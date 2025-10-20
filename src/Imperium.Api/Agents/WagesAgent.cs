using Imperium.Domain.Agents;
using Imperium.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Imperium.Api.Agents;

public class WagesAgent : IWorldAgent
{
    public string Name => "WagesAI";

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var dispatcher = scopeServices.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();
        var metrics = scopeServices.GetRequiredService<Imperium.Api.MetricsService>();

        // pay small wages to subset of characters; can be expanded to real jobs later
        var chars = await db.Characters.OrderBy(c => EF.Functions.Random()).Take(10).ToListAsync(ct);
        foreach (var ch in chars)
        {
            var wage = Math.Round(0.5m + (decimal)Random.Shared.NextDouble(), 2); // 0.5 .. 1.5
            ch.Money += wage;
            var ev = new Imperium.Domain.Models.GameEvent
            {
                Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Type = "wage_paid", Location = ch.LocationName ?? "global",
                PayloadJson = JsonSerializer.Serialize(new { characterId = ch.Id, amount = wage })
            };
            await dispatcher.EnqueueAsync(ev);
        }
        await db.SaveChangesAsync(ct);
        metrics.Increment("economy.wages");
    }
}

