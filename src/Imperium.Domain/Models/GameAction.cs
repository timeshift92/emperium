namespace Imperium.Domain.Models;

public class GameAction
{
    public Guid Id { get; set; }
    public Guid ActorId { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = string.Empty;
}
