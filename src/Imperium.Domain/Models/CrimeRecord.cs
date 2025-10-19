namespace Imperium.Domain.Models;

public class CrimeRecord
{
    public Guid Id { get; set; }
    public Guid PerpetratorId { get; set; }
    public string CrimeType { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
}
