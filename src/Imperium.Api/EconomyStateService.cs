using System.Collections.Concurrent;

namespace Imperium.Api;

public class EconomyStateService
{
    private readonly HashSet<string> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (decimal factor, DateTime? expiresAt)> _shocks = new(StringComparer.OrdinalIgnoreCase);

    public EconomyStateService(IEnumerable<string>? seedItems = null)
    {
        foreach (var i in (seedItems ?? new[] { "grain", "wine", "oil" }))
            if (!string.IsNullOrWhiteSpace(i)) _items.Add(i.Trim());
    }

    public IReadOnlyCollection<string> GetItems() => _items.ToArray();

    public int AddItems(IEnumerable<string> items)
    {
        int added = 0;
        foreach (var i in items)
        {
            if (string.IsNullOrWhiteSpace(i)) continue;
            if (_items.Add(i.Trim())) added++;
        }
        return added;
    }

    // item: specific item or "*" for global
    public void SetShock(string item, decimal factor, DateTime? expiresAt)
    {
        var key = string.IsNullOrWhiteSpace(item) ? "*" : item.Trim();
        _shocks[key] = (factor, expiresAt);
    }

    public decimal GetEffectiveMultiplier(string item)
    {
        PurgeExpired();
        decimal mul = 1m;
        if (_shocks.TryGetValue("*", out var g)) mul *= g.factor;
        if (_shocks.TryGetValue(item, out var s)) mul *= s.factor;
        return mul;
    }

    public void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _shocks.ToArray())
        {
            var exp = kv.Value.expiresAt;
            if (exp.HasValue && exp.Value <= now)
            {
                _shocks.TryRemove(kv.Key, out _);
            }
        }
    }

    public IReadOnlyCollection<object> GetShocks()
    {
        PurgeExpired();
        return _shocks.Select(kv => (object)new { item = kv.Key, factor = kv.Value.factor, expiresAt = kv.Value.expiresAt }).ToArray();
    }
}
