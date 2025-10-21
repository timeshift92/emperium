namespace Imperium.Domain.Models;

public class Faction
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    // type: city_state | tribe | guild | other
    public string Type { get; set; } = "other";
}
