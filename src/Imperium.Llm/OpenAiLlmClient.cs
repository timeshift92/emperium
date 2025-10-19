using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Imperium.Llm;

public class OpenAiLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly double _temperature;

    public OpenAiLlmClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        // Prefer configuration (supports user-secrets / appsettings), fallback to env var
        _apiKey = config["OpenAI:ApiKey"] ?? config["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
    _model = config["OpenAI:Model"] ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
    var tempStr = config["OpenAI:Temperature"] ?? Environment.GetEnvironmentVariable("OPENAI_TEMPERATURE");
    if (!string.IsNullOrWhiteSpace(tempStr) && double.TryParse(tempStr, out var t)) _temperature = t; else _temperature = 0.2;
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("OpenAI API key is not configured. Set OpenAI:ApiKey via user-secrets, appsettings or environment variable OPENAI_API_KEY.");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public async Task<string?> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        // Use OpenAI chat completions (text) - minimal implementation using Responses API format
        // Wrap prompt with explicit instruction to return only compact JSON object and nothing else.
    var wrapped = new StringBuilder();
    wrapped.AppendLine("You are a strict JSON generator. You MUST respond with a single compact JSON object ONLY — no prose, no explanation, no markdown, no code fences, no extra fields.");
    wrapped.AppendLine("Schema: {\"condition\": string, \"temperatureC\": integer, \"windKph\": integer, \"precipitationMm\": number}");
    wrapped.AppendLine("Return exactly the JSON object following the schema as the top-level response text. If you cannot, return {} (an empty object).\n");
    wrapped.AppendLine("EXAMPLE: {\"condition\":\"Clear\",\"temperatureC\":22,\"windKph\":15,\"precipitationMm\":0}");
    wrapped.AppendLine();
    wrapped.AppendLine("Prompt:");
    wrapped.AppendLine(prompt);

        var req = new
        {
            model = _model,
            temperature = _temperature,
            input = wrapped.ToString()
        };

        var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json")
        };
        httpReq.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var res = await _http.SendAsync(httpReq, ct);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync(ct);

        // Try to parse Responses API structure first: output[0].content[0].text
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array && output.GetArrayLength() > 0)
            {
                var first = output[0];
                if (first.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array && content.GetArrayLength() > 0)
                {
                    var c0 = content[0];
                    if (c0.ValueKind == JsonValueKind.Object && c0.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                    {
                        var inner = textEl.GetString();
                        if (!string.IsNullOrWhiteSpace(inner))
                        {
                            // Sometimes the model returns an escaped JSON string (e.g. "{\"condition\":...}").
                            var candidate = inner;
                            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length > 1 && candidate[0] == '"' && candidate[^1] == '"')
                            {
                                try
                                {
                                    candidate = JsonSerializer.Deserialize<string>(candidate) ?? candidate;
                                }
                                catch { }
                            }
                            var innerJson = ExtractFirstJsonObject(candidate ?? string.Empty);
                            if (!string.IsNullOrWhiteSpace(innerJson)) return innerJson;
                        }
                    }
                    // sometimes the content item itself may be a string
                    if (content[0].ValueKind == JsonValueKind.String)
                    {
                        var inner = content[0].GetString();
                        if (!string.IsNullOrWhiteSpace(inner))
                        {
                            var candidate = inner;
                            if (candidate.Length > 1 && candidate[0] == '"' && candidate[^1] == '"')
                            {
                                try { candidate = JsonSerializer.Deserialize<string>(candidate) ?? candidate; } catch { }
                            }
                            var innerJson = ExtractFirstJsonObject(candidate);
                            if (!string.IsNullOrWhiteSpace(innerJson)) return innerJson;
                        }
                    }
                }
            }
        }
        catch { /* ignore parse errors and fallback to regex */ }

        // Fallback: Try to extract the first JSON object from the full body
        var json = ExtractFirstJsonObject(body);
        return json ?? body;
    }

    // Backwards-compatible interface method
    public async Task<string> SendPromptAsync(string prompt, CancellationToken ct = default)
    {
        var r = await GenerateAsync(prompt, ct);
        return r ?? string.Empty;
    }

    private static string? ExtractFirstJsonObject(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        // simple regex to find first {...} block
        var m = Regex.Match(input, @"\{(?:[^{}]|(?<open>\{)|(?<-open>\}))*\}(?(open)(?!))", RegexOptions.Singleline);
        if (m.Success) return m.Value.Trim();
        return null;
    }
}
