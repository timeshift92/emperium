namespace Imperium.Domain.Models;

public class Character
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Status { get; set; } = string.Empty;
    // Gender: "male" | "female" (optional for backwards-compatibility)
    public string? Gender { get; set; }
    // Simple wallet balance for economy interactions
    public decimal Money { get; set; }
    // Навыки в виде простого JSON-строка или сериализуемого объекта
    public string? SkillsJson { get; set; }
    // Essence: характеристики, таланты, черты и др. JSON
    public string? EssenceJson { get; set; }
    // Привязка к локации (для простоты дубль: Id и читаемое имя)
    public Guid? LocationId { get; set; }
    public string? LocationName { get; set; }
    // Короткая история / биография персонажа
    public string? History { get; set; }
    // Precise coordinates (optional). If set, can override coarse LocationId for proximity checks
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
