namespace Imperium.Domain.Models;

public class Location
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Population { get; set; }
}
