namespace Imperium.Api.Models;

/// <summary>
/// Определение товара в экономике: метаданные, которые используют агенты для принятия решений.
/// </summary>
public class EconomyItemDefinition
{
    /// <summary>Идентификатор (имя) товара, уникально в рамках экономики.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Базовая цена товара (глобальная отправная точка).</summary>
    public decimal BasePrice { get; set; } = 5m;
    /// <summary>Единица измерения (например, kg, liter, unit).</summary>
    public string? Unit { get; set; }
    /// <summary>Сколько единиц товара потребляется одним NPC за тик (при необходимости).</summary>
    public decimal ConsumptionPerTick { get; set; } = 0.5m;
    /// <summary>Вес одной единицы товара (в кг).</summary>
    public decimal WeightPerUnit { get; set; } = 1m;
    /// <summary>Срок годности в днях (при наличии). Null — не портится.</summary>
    public int? PerishableDays { get; set; }
    /// <summary>Размер стака (максимальное число в одной пачке).</summary>
    public int StackSize { get; set; } = 100;
    /// <summary>Категория товара (например food, drink, raw, material).</summary>
    public string? Category { get; set; }
    /// <summary>Короткое описание или категория товара.</summary>
    public string? Description { get; set; }
    /// <summary>Теги/категории для фильтрации и поиска.</summary>
    public string[]? Tags { get; set; }
}
