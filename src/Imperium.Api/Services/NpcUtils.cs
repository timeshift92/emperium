using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Imperium.Domain.Models;
using Imperium.Domain.Utils;

namespace Imperium.Api.Services;

public static class NpcUtils
{
    public static object? TryParseJsonOrRaw(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText())!;
        }
        catch
        {
            return json;
        }
    }

    public static bool TryParseNpcReply(string input, out string reply, out int? mood)
    {
        reply = string.Empty;
        mood = null;
        try
        {
            using var doc = JsonDocument.Parse(input);
            var root = doc.RootElement;
            if (root.TryGetProperty("reply", out var r))
                reply = r.GetString() ?? string.Empty;
            if (root.TryGetProperty("moodDelta", out var m) && m.ValueKind == JsonValueKind.Number)
                mood = m.GetInt32();
            return !string.IsNullOrWhiteSpace(reply);
        }
        catch
        {
            return false;
        }
    }

    public static string SanitizeReply(string input, string[] forbidden)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var s = Regex.Replace(input, "\\b(19|20)\\d{2}\\b", "", RegexOptions.Compiled);
        foreach (var f in forbidden.OrderByDescending(x => x.Length))
            s = Regex.Replace(s, Regex.Escape(f), "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "[A-Za-z]", "", RegexOptions.Compiled);
        s = Regex.Replace(s, "\\s+", " ", RegexOptions.Compiled).Trim();

        if (string.IsNullOrWhiteSpace(s))
        {
            s = "Я говорю на языке Империи и обсуждаю дела нашего времени.";
        }

        return s;
    }

    public static bool IsSignificantLatinOrTechnical(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        var latinMatches = Regex.Matches(input, "[A-Za-z]", RegexOptions.Compiled);
        var letterMatches = Regex.Matches(input, "[A-Za-zА-Яа-я]", RegexOptions.Compiled);
        int latin = latinMatches.Count;
        int letters = Math.Max(1, letterMatches.Count);

        double ratio = (double)latin / letters;
        if (ratio > 0.25) return true;

        var lower = input.ToLowerInvariant();
        if (lower.Contains("http") || lower.Contains("www.") || lower.Contains("@") || lower.Contains("mailto:")
            || lower.Contains("```") || lower.Contains("<code>") || lower.Contains("model:") || lower.Contains("temperature")
            || lower.Contains("tokens") || lower.Contains("usage") || lower.Contains("stop")
            || Regex.IsMatch(input, "\\b(api|github|stack overflow|stackoverflow|computer|server|internet)\\b", RegexOptions.IgnoreCase))
            return true;

        return false;
    }

    public static string ReaskPrompt() => "[role:Npc]\nОтвет верни в JSON {\"reply\": string, \"moodDelta\": int}. Пиши строго по-русски кириллицей и не вставляй английских слов.";

    public static bool HasForbiddenTokens(string text, string[] forbidden)
    {
        if (string.IsNullOrWhiteSpace(text) || forbidden == null || forbidden.Length == 0) return false;
        var lower = text.ToLowerInvariant();
        foreach (var f in forbidden)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            if (lower.Contains(f.ToLowerInvariant())) return true;
        }
        return false;
    }

    public static string BuildPrompt(Character ch, string archetype)
    {
        var npcNameJson = JsonSerializer.Serialize(ch.Name);
        var skillsJson = ch.SkillsJson ?? "[]";
        var essence = ch.EssenceJson ?? "{}";
        var loc = string.IsNullOrWhiteSpace(ch.LocationName) ? "неизвестное место" : ch.LocationName;
        var history = string.IsNullOrWhiteSpace(ch.History) ? "" : ch.History;
        var normalizedGender = GenderHelper.Normalize(ch.Gender);
        var genderRu = normalizedGender switch
        {
            "female" => "женщина",
            "male" => "мужчина",
            _ => string.Empty
        };
        var toneHint = normalizedGender switch
        {
            "female" => "Персонаж женского пола: используй мягкие, заботливые обороты речи и женские окончания, если уместно. ",
            "male" => "Персонаж мужского пола: допускаются более уверенные и решительные формулировки с мужскими окончаниями. ",
            _ => "Пол не указан: придерживайся нейтрального тона без упора на гендер. "
        };

        var sb = new StringBuilder();
        sb.Append("[role:Npc]\n");
        sb.Append("Ты отвечаешь как житель античного Средиземноморья. Говори уверенно и только на русском языке, используя кириллицу. ");
        sb.Append(toneHint);
        sb.Append("Пол персонажа: ");
        sb.Append(string.IsNullOrEmpty(genderRu) ? "не указан. " : $"{genderRu}. ");
        sb.Append("Не упоминай современные технологии, нейросети или английские слова. Верни JSON без лишних полей: {\"reply\": string, \"moodDelta\": int}. ");
        sb.Append("Поле reply должно содержать 2-3 короткие фразы (до 40 слов) и оставаться в рамках эпохи. ");
        sb.Append("Контекст: имя ");
        sb.Append(npcNameJson);
        sb.Append(", возраст ");
        sb.Append(ch.Age);
        sb.Append(", статус ");
        sb.Append(ch.Status ?? "неизвестно");
        sb.Append(", локация ");
        sb.Append(loc);
        sb.Append(", навыки ");
        sb.Append(skillsJson);
        sb.Append(", сущность ");
        sb.Append(essence);
        sb.Append(", история ");
        sb.Append(history);
        sb.Append(", текущая дата ");
        sb.Append(DateTime.UtcNow.ToString("O"));
        sb.Append('.');

        return sb.ToString();
    }

    public static string BuildRewritePrompt(string reply)
    {
        return "[role:Npc]\nПерепиши ответ, сохранив смысл и атмосферу эпохи, используя только русский язык (кириллица). Верни JSON {\"reply\": string, \"moodDelta\": int}.\nИсходный ответ:\n" + reply;
    }

    public static string InferArchetype(string? skillsJson)
    {
        if (string.IsNullOrWhiteSpace(skillsJson)) return "крестьянин";
        var s = skillsJson.ToLowerInvariant();
        return s switch
        {
            var x when x.Contains("ремес") || x.Contains("кузн") => "ремесленник",
            var x when x.Contains("купец") || x.Contains("торг") => "торговец",
            var x when x.Contains("солдат") || x.Contains("вояк") => "солдат",
            var x when x.Contains("жрец") || x.Contains("монах") || x.Contains("свящ") => "жрец",
            _ => "крестьянин"
        };
    }
}
