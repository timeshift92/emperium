namespace Imperium.Domain.Models;

public class NpcEssence
{
    public Guid Id { get; set; }
    public Guid CharacterId { get; set; }
    public int Strength { get; set; }
    public int Intelligence { get; set; }
    public int Charisma { get; set; }
    public int Vitality { get; set; }
    public int Luck { get; set; }
    public double MutationChance { get; set; }
    // Behavioral state
    public string Mood { get; set; } = "neutral";
    public string? LastAction { get; set; }
    // Energy [0..1]
    public double Energy { get; set; } = 1.0;
    // Motivation [0..1]
    public double Motivation { get; set; } = 1.0;
}
