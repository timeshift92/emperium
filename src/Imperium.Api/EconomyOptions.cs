namespace Imperium.Api;

public class EconomyOptions
{
    public string[] Items { get; set; } = new[] { "grain", "wine", "oil" };
    // Units per tick added by ProductionAgent (before environmental multipliers)
    public Dictionary<string, decimal> ProductionPerTick { get; set; } = new()
    {
        ["grain"] = 1.0m,
        ["wine"] = 0.3m,
        ["oil"] = 0.25m
    };
    // Units per tick consumed by ConsumptionAgent per character
    public Dictionary<string, decimal> ConsumptionPerTick { get; set; } = new()
    {
        ["grain"] = 1.0m,
        ["wine"] = 0.05m,
        ["oil"] = 0.03m
    };
}

