namespace Imperium.Domain.Models;

public class EconomySnapshot
{
    public Guid Id { get; set; }
    public DateTime Timestamp { get; set; }
    // Примеры: словарь товаров и их цен, хранящийся как JSON
    public string PricesJson { get; set; } = string.Empty;
    public decimal Treasury { get; set; }
}
