namespace Imperium.Domain.Models;

public class Army
{
    public Guid Id { get; set; }
    public Guid FactionId { get; set; }
    public string Type { get; set; } = string.Empty; // infantry/cavalry/archers/navy
    public int Manpower { get; set; }
    public decimal Morale { get; set; }
}
