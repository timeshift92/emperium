namespace Imperium.Domain.Models;

public class Building
{
    public Guid Id { get; set; }
    public Guid? LocationId { get; set; }
    public string Kind { get; set; } = string.Empty;
}
