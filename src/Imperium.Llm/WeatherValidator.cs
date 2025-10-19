using System.Text.Json;
using System.Text.Json.Serialization;

namespace Imperium.Llm;

public class WeatherSnapshotDto
{
    [JsonPropertyName("condition")] public string? Condition { get; set; }
    [JsonPropertyName("temperatureC")] public double? TemperatureC { get; set; }
    [JsonPropertyName("windKph")] public double? WindKph { get; set; }
    [JsonPropertyName("precipitationMm")] public double? PrecipitationMm { get; set; }
}

public static class WeatherValidator
{
    // TryParse: attempts to extract a JSON object from arbitrary text and deserialize it.
    // Returns true when DTO is valid; returns false and an error message when parsing failed.
    public static bool TryParse(string input, out WeatherSnapshotDto? dto, out string? error)
    {
        dto = null;
        error = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "empty input";
            return false;
        }

        // Try to extract {...} JSON object from text first
        string candidate = ExtractFirstJsonObject(input) ?? input;
        // If candidate looks like a quoted/escaped JSON string, unescape it
        if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length > 1 && candidate[0] == '"' && candidate[^1] == '"')
        {
            try { candidate = JsonSerializer.Deserialize<string>(candidate) ?? candidate; } catch { }
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            dto = JsonSerializer.Deserialize<WeatherSnapshotDto>(candidate, options);
            if (dto == null)
            {
                error = "deserialized to null";
                return false;
            }
            if (string.IsNullOrWhiteSpace(dto.Condition)) { error = "missing condition"; return false; }
            dto.Condition = dto.Condition?.Trim();
            // Accept fractional temperatures and wind speeds
            if (!dto.TemperatureC.HasValue) { error = "missing temperatureC"; return false; }
            if (!dto.WindKph.HasValue) { error = "missing windKph"; return false; }
            if (!dto.PrecipitationMm.HasValue) { error = "missing precipitationMm"; return false; }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? ExtractFirstJsonObject(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        try
        {
            var m = System.Text.RegularExpressions.Regex.Match(input, @"\{(?:[^{}]|(?<open>\{)|(?<-open>\}))*\}(?(open)(?!))", System.Text.RegularExpressions.RegexOptions.Singleline);
            if (m.Success) return m.Value;
        }
        catch { }
        return null;
    }
}
