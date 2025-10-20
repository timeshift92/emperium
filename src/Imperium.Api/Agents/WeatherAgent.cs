using Imperium.Infrastructure;
using Imperium.Domain.Models;
using Imperium.Llm;
using Imperium.Domain.Agents;
using Microsoft.Extensions.DependencyInjection;

namespace Imperium.Api.Agents;

public class WeatherAgent : IWorldAgent
{
    private readonly ILogger<WeatherAgent> _logger;

    public string Name => "WeatherAI";

    public WeatherAgent(ILogger<WeatherAgent> logger)
    {
        _logger = logger;
    }

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var llm = scopeServices.GetRequiredService<Imperium.Llm.ILlmClient>();

    var prompt = "[role:World]\nGenerate compact JSON weather snapshot: {condition, temperatureC, windKph, precipitationMm}";

        string? raw = null;
        WeatherSnapshotDto? dto = null;
        string? parseError = null;

        var metrics = scopeServices.GetRequiredService<Imperium.Api.MetricsService>();

        // try with a couple of retries, then reask explicitly
        for (int attempt = 1; attempt <= 3; attempt++)
        {
                raw = await llm.SendPromptAsync(prompt, ct);
            if (string.IsNullOrWhiteSpace(raw))
            {
                _logger.LogWarning("WeatherAgent: empty response from LLM (attempt {Attempt})", attempt);
                metrics.Increment("weather.invalid_responses");
                continue;
            }

            if (Imperium.Llm.WeatherValidator.TryParse(raw, out dto, out parseError) && dto != null)
            {
                break;
            }

            _logger.LogWarning("WeatherAgent: LLM returned invalid weather JSON (attempt {Attempt}): {Error}. Raw: {Raw}", attempt, parseError, raw);
            metrics.Increment("weather.invalid_responses");
            await Task.Delay(TimeSpan.FromSeconds(1 * attempt), ct);

            // After 2 failed attempts, do a short explicit reask asking for pure JSON only
            if (attempt == 2)
            {
                metrics.Increment("weather.reasks");
                var reask = "Return only the JSON object now, nothing else. Schema: {condition, temperatureC, windKph, precipitationMm}";
                raw = await llm.SendPromptAsync(reask, ct);
                if (!string.IsNullOrWhiteSpace(raw) && Imperium.Llm.WeatherValidator.TryParse(raw, out dto, out parseError) && dto != null)
                {
                    break;
                }
            }
        }

        // Fallback to local mock generator if LLM failed
        if (dto == null)
        {
            _logger.LogWarning("WeatherAgent: falling back to local mock generator for weather snapshot");
            metrics.Increment("weather.fallbacks");
            dto = GenerateMockSnapshot();
        }

        var snap = new WeatherSnapshot
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            Condition = dto.Condition ?? "unknown",
            TemperatureC = (int)Math.Round(dto.TemperatureC ?? 0.0),
            WindKph = (int)Math.Round(dto.WindKph ?? 0.0),
            PrecipitationMm = dto.PrecipitationMm ?? 0.0
        };
        db.WeatherSnapshots.Add(snap);
        var stream = scopeServices.GetRequiredService<Imperium.Api.EventStreamService>();
        var dispatcher = scopeServices.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();
        try
        {
            await db.SaveChangesAsync(ct);
            metrics.Increment("weather.saved");
            _logger.LogInformation("WeatherAgent: saved snapshot {Id} cond={Cond} t={T}C p={P}mm", snap.Id, snap.Condition, snap.TemperatureC, snap.PrecipitationMm);
            // publish snapshot to SSE stream
            _ = stream.PublishWeatherAsync(snap);
            // enqueue a game event for weather_update to dispatcher
            var ev = new GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "weather_update",
                Location = "global",
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { snapshotId = snap.Id, condition = snap.Condition, temperatureC = snap.TemperatureC })
            };
            _ = dispatcher.EnqueueAsync(ev);

            // Apply economy shocks based on precipitation extremes
            var state = scopeServices.GetRequiredService<Imperium.Api.EconomyStateService>();
            // drought -> grain up 20% for 1 hour; heavy rain -> grain down 10% for 1 hour
            if (snap.PrecipitationMm < 1.0)
            {
                state.SetShock("grain", 1.20m, DateTime.UtcNow.AddHours(1));
            }
            else if (snap.PrecipitationMm > 5.0)
            {
                state.SetShock("grain", 0.90m, DateTime.UtcNow.AddHours(1));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save WeatherSnapshot");
        }
    }

    private static WeatherSnapshotDto GenerateMockSnapshot()
    {
        var temp = Random.Shared.Next(-5, 35);
        var wind = Random.Shared.Next(0, 40);
        var precip = Math.Round(Random.Shared.NextDouble() * 10, 1);
        var cond = temp < 0 ? "Snow" : (precip > 2 ? "Rain" : "Clear");
        return new WeatherSnapshotDto { Condition = cond, TemperatureC = temp, WindKph = wind, PrecipitationMm = precip };
    }
}
