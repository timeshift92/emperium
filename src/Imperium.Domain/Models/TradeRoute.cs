namespace Imperium.Domain.Models;

public class TradeRoute
{
    public Guid Id { get; set; }
    public Guid FromLocationId { get; set; }
    public Guid ToLocationId { get; set; }
    // owning faction (creator/maintainer)
    public Guid OwnerFactionId { get; set; }
    public decimal Toll { get; set; }
    public string? Transport { get; set; }
}
