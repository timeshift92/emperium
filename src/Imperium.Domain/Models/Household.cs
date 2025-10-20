namespace Imperium.Domain.Models;

public class Household
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? LocationId { get; set; }
    public Guid? HeadId { get; set; }
    // Stored as JSON array of GUIDs
    public string MemberIdsJson { get; set; } = "[]";
    public decimal Wealth { get; set; }
}

