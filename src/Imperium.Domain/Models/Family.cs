namespace Imperium.Domain.Models;

public class Family
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Guid> MemberIds { get; set; } = new();
    public decimal Wealth { get; set; }
}
