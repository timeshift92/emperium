namespace Imperium.Domain.Models;

public class Character
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Status { get; set; } = string.Empty;
    // Навыки в виде простого JSON-строка или сериализуемого объекта
    public string? SkillsJson { get; set; }
}
