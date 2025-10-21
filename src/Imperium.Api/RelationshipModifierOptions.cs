using System.Collections.Generic;

namespace Imperium.Api;

/// <summary>
/// Настройки RelationshipAI: конфигурация мягких модификаторов.
/// </summary>
public class RelationshipModifierOptions
{
    public Dictionary<string, double> GenderBias { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public double Resolve(string key)
    {
        if (GenderBias == null || GenderBias.Count == 0) return 0;
        return GenderBias.TryGetValue(key, out var value) ? value : 0;
    }
}
