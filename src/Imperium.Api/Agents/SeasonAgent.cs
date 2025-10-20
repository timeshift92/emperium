using Imperium.Domain.Agents;
using Imperium.Infrastructure;
using Imperium.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Imperium.Api.Agents;

public class SeasonAgent : IWorldAgent
{
    public string Name => "SeasonAI";

    // simple thresholds for demonstration
    private const int Lookback = 10; // number of latest weather snapshots to average

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var metrics = scopeServices.GetRequiredService<Imperium.Api.MetricsService>();

        // get last N snapshots
    var snaps = await db.WeatherSnapshots.OrderByDescending(s => s.Timestamp).Take(Lookback).ToListAsync();
        if (snaps == null || snaps.Count == 0)
        {
            return; // nothing to compute
        }

        var avgTemp = snaps.Average(s => s.TemperatureC);
        var avgPrecip = snaps.Average(s => s.PrecipitationMm);

        // simple season determination by average temperature
        string season;
        if (avgTemp <= 0) season = "Winter";
        else if (avgTemp <= 12) season = "Spring";
        else if (avgTemp <= 25) season = "Summer";
        else season = "Autumn";

        // read current season state (singleton)
    var state = await db.SeasonStates.OrderByDescending(s => s.StartedAt).FirstOrDefaultAsync();
        var dispatcher = scopeServices.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();
        if (state == null)
        {
            state = new SeasonState
            {
                Id = Guid.NewGuid(),
                CurrentSeason = season,
                AverageTemperatureC = avgTemp,
                AveragePrecipitationMm = avgPrecip,
                StartedAt = DateTime.UtcNow,
                DurationTicks = 0
            };
            db.SeasonStates.Add(state);
            await db.SaveChangesAsync();
            var ev = new GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "season_set",
                Location = "global",
                PayloadJson = JsonSerializer.Serialize(new { season = state.CurrentSeason, avgTemp, avgPrecip })
            };
            _ = dispatcher.EnqueueAsync(ev);
            metrics.Increment("season.initialized");
            return;
        }

        // if season changed, emit event and update
        if (!string.Equals(state.CurrentSeason, season, StringComparison.OrdinalIgnoreCase))
        {
            state.CurrentSeason = season;
            state.AverageTemperatureC = avgTemp;
            state.AveragePrecipitationMm = avgPrecip;
            state.StartedAt = DateTime.UtcNow;
            state.DurationTicks = 0;

            var ev = new GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "season_change",
                Location = "global",
                PayloadJson = JsonSerializer.Serialize(new { season = state.CurrentSeason, avgTemp, avgPrecip })
            };
            metrics.Increment("season.changes");
            await db.SaveChangesAsync();
            _ = dispatcher.EnqueueAsync(ev);
            return;
        }

        // else update averages and increment duration
        state.AverageTemperatureC = avgTemp;
        state.AveragePrecipitationMm = avgPrecip;
        state.DurationTicks += 1;
    await db.SaveChangesAsync();
        metrics.Increment("season.ticks");
    }
}
