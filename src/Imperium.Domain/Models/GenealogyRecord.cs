namespace Imperium.Domain.Models;

public class GenealogyRecord
{
    public Guid Id { get; set; }
    public Guid CharacterId { get; set; }
    public Guid? FatherId { get; set; }
    public Guid? MotherId { get; set; }
    // JSON arrays of GUIDs for convenience
    public string SpouseIdsJson { get; set; } = "[]";
    public string ChildrenIdsJson { get; set; } = "[]";
}

