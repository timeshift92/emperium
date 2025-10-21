namespace Imperium.Domain.Models;

public class WorldChronicle
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public string Summary { get; set; } = string.Empty;
}
