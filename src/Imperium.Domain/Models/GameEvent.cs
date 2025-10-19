namespace Imperium.Domain.Models;

public class GameEvent
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
}
