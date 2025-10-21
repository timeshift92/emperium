using System.Threading.Channels;
using Imperium.Domain.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Imperium.Api;

public class EventStreamService
{
    private readonly Channel<GameEvent> _events = Channel.CreateUnbounded<GameEvent>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private readonly Channel<WeatherSnapshot> _weathers = Channel.CreateUnbounded<WeatherSnapshot>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private readonly IHubContext<Hubs.EventsHub>? _hubContext;
    private readonly ILogger<EventStreamService>? _logger;
    private readonly MetricsService? _metrics;

    public EventStreamService(IHubContext<Hubs.EventsHub>? hubContext = null, ILogger<EventStreamService>? logger = null, MetricsService? metrics = null)
    {
        _hubContext = hubContext;
        _logger = logger;
        _metrics = metrics;
    }

    public async ValueTask PublishEventAsync(GameEvent e)
    {
        await _events.Writer.WriteAsync(e);
        if (string.Equals(e.Type, "npc_reply", StringComparison.OrdinalIgnoreCase))
            _metrics?.Increment("npc.replies");
        else
            _metrics?.Increment($"events.{e.Type}");
        if (_hubContext != null)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("event", e);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to push event to SignalR hub");
            }
        }
    }

    public async ValueTask PublishWeatherAsync(WeatherSnapshot s)
    {
        await _weathers.Writer.WriteAsync(s);
        _metrics?.Increment("weather.snapshots.published");
        if (_hubContext != null)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("weather", s);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Failed to push weather to SignalR hub");
            }
        }
    }

    public ChannelReader<GameEvent> Events => _events.Reader;
    public ChannelReader<WeatherSnapshot> Weathers => _weathers.Reader;
}
