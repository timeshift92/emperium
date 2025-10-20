using System;

namespace Imperium.Domain.Models;

public class MarketOrder
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerType { get; set; } = "character";
    public Guid? LocationId { get; set; }
    public string Item { get; set; } = "grain";
    public string Side { get; set; } = "buy"; // buy|sell
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal Remaining { get; set; }
    public string Status { get; set; } = "open"; // open|partial|filled|cancelled
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    // Reservations to prevent double-spend
    public decimal ReservedFunds { get; set; } // for buy orders: estimated max funds reserved
    public decimal ReservedQty { get; set; }    // for sell orders: quantity reserved from inventory
    public DateTime? ExpiresAt { get; set; }
}
