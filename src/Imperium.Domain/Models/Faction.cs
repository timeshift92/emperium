namespace Imperium.Domain.Models;

public class Faction
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    // type: city_state | tribe | guild | other
    public string Type { get; set; } = "other";
    // Optional parent empire / suzerain
    public Guid? ParentFactionId { get; set; }
    // Persisted tax policy JSON (simple key->rate)
    public string? TaxPolicyJson { get; set; }
    // Optional primary location for the faction (city-state location)
    public Guid? LocationId { get; set; }
}
