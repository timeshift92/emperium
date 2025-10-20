using System;

namespace Imperium.Domain.Models;

public class Inventory
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerType { get; set; } = "character"; // character|household|faction
    public Guid? LocationId { get; set; }
    public string Item { get; set; } = "grain"; // item code
    public decimal Quantity { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

