using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Imperium.Llm;

public class OllamaLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _defaultModel;

    public OllamaLlmClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _defaultModel = config["Ollama:Model"] ?? "mistral";
    }

    public async Task<string> SendPromptAsync(string prompt, CancellationToken ct = default)
    {
        var req = new
        {
            model = _defaultModel,
            prompt,
            stream = false
        };

        var res = await _http.PostAsJsonAsync("http://localhost:11434/api/generate", req, cancellationToken: ct);
        res.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("response", out var text)
            ? text.GetString() ?? string.Empty
            : string.Empty;
    }
}
