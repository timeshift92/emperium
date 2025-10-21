using System;
using System.Collections.Generic;

namespace Imperium.Api;

public class LogisticsOptions
{
    public decimal BaseCostPerUnit { get; set; } = 0.05m;
    public decimal DistanceMultiplier { get; set; } = 0.01m;
    public Dictionary<string, decimal> RouteCost { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public decimal ResolveDistance(Guid? from, Guid? to)
    {
        if (!from.HasValue || !to.HasValue) return 0m;
        var key = $"{from.Value:D}->{to.Value:D}";
        if (RouteCost.TryGetValue(key, out var distance)) return distance;
        key = $"{to.Value:D}->{from.Value:D}";
        if (RouteCost.TryGetValue(key, out distance)) return distance;
        return 0m;
    }
}
