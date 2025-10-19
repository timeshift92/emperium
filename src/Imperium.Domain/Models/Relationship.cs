namespace Imperium.Domain.Models;

/// <summary>
/// Направленное отношение между двумя персонажами.
/// Значения в диапазоне от -100 до 100 для простых эвристик.
/// </summary>
public class Relationship
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public Guid TargetId { get; set; }
    public string Type { get; set; } = "acquaintance";
    public int Trust { get; set; }
    public int Love { get; set; }
    public int Hostility { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
