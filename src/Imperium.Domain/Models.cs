
namespace Imperium.Domain;

public enum DecreeStatus { Draft, Active, Expired, Rejected }

public class Decree
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string ParsedJson { get; set; } = "{}";
    public DecreeStatus Status { get; set; } = DecreeStatus.Draft;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveAt { get; set; }
}

public class Npc
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "peasant";
    public string MemoryJson { get; set; } = "[]";
    public double Influence { get; set; }
    public double Loyalty { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class GameEvent
{
    public int Id { get; set; }
    public string Type { get; set; } = "reaction";
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class EconomySnapshot
{
    public int Id { get; set; }
    public int Tick { get; set; }
    public double GrainStock { get; set; }
    public double GrainPrice { get; set; }
    public double Treasury { get; set; }
    public double TaxRate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
