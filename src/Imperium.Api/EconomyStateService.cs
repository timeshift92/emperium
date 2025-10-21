using System.Collections.Concurrent;

namespace Imperium.Api;

public class EconomyStateService
{
    private readonly HashSet<string> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Models.EconomyItemDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (decimal factor, DateTime? expiresAt)> _shocks = new(StringComparer.OrdinalIgnoreCase);

    public EconomyStateService(IEnumerable<string>? seedItems = null)
    {
        var seeds = seedItems ?? new[] { "grain", "wine", "oil" };
        foreach (var i in seeds)
        {
            if (string.IsNullOrWhiteSpace(i)) continue;
            var n = i.Trim();
            _items.Add(n);
            // default definitions
                if (!_definitions.ContainsKey(n))
                {
                    _definitions[n] = new Models.EconomyItemDefinition
                    {
                        Name = n,
                        BasePrice = n switch { "grain" => 10m, "wine" => 15m, "oil" => 8m, _ => 5m },
                        Unit = "unit",
                        ConsumptionPerTick = n == "grain" ? 0.5m : 0.2m,
                        WeightPerUnit = n switch { "grain" => 0.75m, "wine" => 1.0m, "oil" => 0.9m, _ => 1.0m },
                        PerishableDays = n switch { "bread" => 3, "wine" => 365, _ => (int?)null },
                        StackSize = 100,
                        Category = n switch { "grain" => "food", "wine" => "drink", "oil" => "food", _ => "misc" }
                    };
                }
        }
    }

    public IReadOnlyCollection<string> GetItems() => _items.ToArray();

    public IReadOnlyCollection<Models.EconomyItemDefinition> GetDefinitions() => _definitions.Values.ToArray();

    public Models.EconomyItemDefinition? GetDefinition(string item)
    {
        if (string.IsNullOrWhiteSpace(item)) return null;
        _definitions.TryGetValue(item.Trim(), out var def);
        return def;
    }

    public bool AddOrUpdateDefinition(Models.EconomyItemDefinition def)
    {
        if (def == null || string.IsNullOrWhiteSpace(def.Name)) return false;
        var name = def.Name.Trim();
        _items.Add(name);
        _definitions[name] = def;
        return true;
    }

    public int AddItems(IEnumerable<string> items)
    {
        int added = 0;
        foreach (var i in items)
        {
            if (string.IsNullOrWhiteSpace(i)) continue;
            var name = i.Trim();
            if (_items.Add(name))
            {
                added++;
                if (!_definitions.ContainsKey(name))
                {
                    _definitions[name] = new Models.EconomyItemDefinition
                    {
                        Name = name,
                        BasePrice = 5m,
                        Unit = "unit",
                        ConsumptionPerTick = 0.2m,
                        WeightPerUnit = 1m,
                        StackSize = 100,
                        Category = "misc"
                    };
                }
            }
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
