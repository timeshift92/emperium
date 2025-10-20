using System;

namespace Imperium.Domain.Models;

public class Trade
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Guid? LocationId { get; set; }
    public string Item { get; set; } = "grain";
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public Guid BuyOrderId { get; set; }
    public Guid SellOrderId { get; set; }
    public Guid BuyerId { get; set; }
    public Guid SellerId { get; set; }
}

