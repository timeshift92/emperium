using System.Threading.Channels;
using Imperium.Domain.Models;

namespace Imperium.Api;

public class EventStreamService
{
    private readonly Channel<GameEvent> _events = Channel.CreateUnbounded<GameEvent>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    private readonly Channel<WeatherSnapshot> _weathers = Channel.CreateUnbounded<WeatherSnapshot>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });

    public ValueTask PublishEventAsync(GameEvent e)
    {
        return _events.Writer.WriteAsync(e);
    }

    public ValueTask PublishWeatherAsync(WeatherSnapshot s)
    {
        return _weathers.Writer.WriteAsync(s);
    }

    public ChannelReader<GameEvent> Events => _events.Reader;
    public ChannelReader<WeatherSnapshot> Weathers => _weathers.Reader;
}
