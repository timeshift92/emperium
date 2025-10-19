using Imperium.Domain.Agents;
using Imperium.Infrastructure;
using Imperium.Domain.Models;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Imperium.Api;

public class TimeAgent : IWorldAgent
{
    public string Name => "TimeAI";

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();

        // singleton row for world time
        var worldTime = await db.WorldTimes.FirstOrDefaultAsync(ct);
        if (worldTime == null)
        {
            worldTime = new WorldTime
            {
                Id = Guid.NewGuid(),
                Tick = 0,
                Hour = 0,
                Day = 0,
                Year = 0,
                IsDaytime = true,
                LastUpdated = DateTime.UtcNow
            };
            db.WorldTimes.Add(worldTime);
        }

    // constants from project instructions
    const int ticksPerHour = 120;
    const int ticksPerDay = 2880;
    const int ticksPerYear = 34560;

    // remember previous counters before advancing the tick
    var prevTick = worldTime.Tick;
    var oldDay = prevTick / ticksPerDay;
    var oldYear = prevTick / ticksPerYear;

    // advance one tick
    worldTime.Tick += 1;

        // compute new hour/day/year
        worldTime.Hour = (int)((worldTime.Tick % ticksPerDay) / ticksPerHour);
        worldTime.Day = (int)((worldTime.Tick % ticksPerYear) / ticksPerDay);
        worldTime.Year = (int)(worldTime.Tick / ticksPerYear);
        worldTime.IsDaytime = worldTime.Hour >= 6 && worldTime.Hour < 18;
        worldTime.LastUpdated = DateTime.UtcNow;

        // persist worldTime
        await db.SaveChangesAsync(ct);

        // Emit tick event via central dispatcher (non-blocking for agents)
        var dispatcher = scopeServices.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();

        await dispatcher.EnqueueAsync(new GameEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Type = "time_tick",
            Location = "global",
            PayloadJson = JsonSerializer.Serialize(new { tick = worldTime.Tick, hour = worldTime.Hour, day = worldTime.Day, year = worldTime.Year })
        });

        // If day changed, emit day_change
        var newDay = worldTime.Tick / ticksPerDay;
        if (newDay != oldDay)
        {
            await dispatcher.EnqueueAsync(new GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "day_change",
                Location = "global",
                PayloadJson = JsonSerializer.Serialize(new { day = worldTime.Day, year = worldTime.Year })
            });
        }

        // If year changed, emit year_change
        var newYear = worldTime.Tick / ticksPerYear;
        if (newYear != oldYear)
        {
            await dispatcher.EnqueueAsync(new GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "year_change",
                Location = "global",
                PayloadJson = JsonSerializer.Serialize(new { year = worldTime.Year })
            });
        }
    }
}
