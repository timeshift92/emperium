namespace Imperium.Domain.Utils;

/// <summary>
/// Утилиты по работе с гендером персонажей.
/// Нормализуем строковые значения и проверяем их на допустимые варианты.
/// </summary>
public static class GenderHelper
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "male" or "мужчина" or "m" => "male",
            "female" or "женщина" or "f" => "female",
            _ => null
        };
    }

    public static bool IsSameGender(string? first, string? second)
    {
        var a = Normalize(first);
        var b = Normalize(second);
        return a != null && a == b;
    }
}
