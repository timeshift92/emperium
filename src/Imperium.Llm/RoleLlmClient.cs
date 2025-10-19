using System.Net.Http.Json;
using System.Text.Json;

namespace Imperium.Llm;

/// <summary>
/// Generic Ollama-compatible typed client that pins a default model and posts to a local Ollama-like HTTP API.
/// </summary>
public class RoleLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _model;

    public RoleLlmClient(HttpClient http, string model)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _model = model ?? throw new ArgumentNullException(nameof(model));
    }

    public async Task<string> SendPromptAsync(string prompt, CancellationToken ct = default)
    {
        var req = new { model = _model, prompt, stream = false };

        var res = await _http.PostAsJsonAsync("/api/generate", req, ct);
        res.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("response", out var text) ? text.GetString() ?? string.Empty : string.Empty;
    }
}
