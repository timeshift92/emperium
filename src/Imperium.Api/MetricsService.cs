using System.Collections.Concurrent;

namespace Imperium.Api;

public class MetricsService
{
    private readonly ConcurrentDictionary<string, long> _counters = new();

    public void Increment(string name)
    {
        _counters.AddOrUpdate(name, 1, (_, v) => v + 1);
    }

    public void Add(string name, long value)
    {
        _counters.AddOrUpdate(name, value, (_, v) => v + value);
    }

    public long Get(string name)
    {
        return _counters.TryGetValue(name, out var v) ? v : 0L;
    }

    public IReadOnlyDictionary<string, long> Snapshot()
    {
        return new Dictionary<string, long>(_counters);
    }
}
