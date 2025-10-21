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
    const int monthsPerYear = 12;
    var ticksPerMonth = ticksPerYear / monthsPerYear;

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
    worldTime.Month = (int)(((worldTime.Tick % ticksPerYear) / ticksPerMonth) + 1);
    // compute DayOfMonth as day index within month
    var dayOfYear = (int)((worldTime.Tick % ticksPerYear) / ticksPerDay);
    worldTime.DayOfMonth = (dayOfYear % (ticksPerMonth / ticksPerDay)) + 1;
        worldTime.LastUpdated = DateTime.UtcNow;

        // persist worldTime
        await db.SaveChangesAsync(ct);

        // Emit tick event via central dispatcher (non-blocking for agents)
        var dispatcher = scopeServices.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();
        var currentMonth = (int)((worldTime.Tick % ticksPerYear) / ticksPerMonth) + 1; // 1..12
        await dispatcher.EnqueueAsync(new GameEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Type = "time_tick",
            Location = "global",
            // include month and dayOfMonth for UI
            PayloadJson = JsonSerializer.Serialize(new { tick = worldTime.Tick, hour = worldTime.Hour, day = worldTime.Day, month = currentMonth, dayOfMonth = worldTime.DayOfMonth, year = worldTime.Year })
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

        // If month changed, emit month_change (months are derived from ticks/season about a 12-part year)
        var oldMonth = (int)((oldDay % ticksPerYear) / ticksPerMonth) + 1;
        var newMonth = (int)((worldTime.Tick % ticksPerYear) / ticksPerMonth) + 1;
        if (newMonth != oldMonth)
        {
            await dispatcher.EnqueueAsync(new GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "month_change",
                Location = "global",
                PayloadJson = JsonSerializer.Serialize(new { month = newMonth, year = worldTime.Year })
            });
        }
    }
}
