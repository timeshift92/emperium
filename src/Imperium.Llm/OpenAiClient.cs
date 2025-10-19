
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Imperium.Llm;

public interface ILlmClient
{
    Task<string> StructuredJsonAsync(string userText, string extractionInstruction, CancellationToken ct);
    Task<string> ShortReplyAsync(string prompt, object context, CancellationToken ct);
}

public class OpenAiLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _model;
    public OpenAiLlmClient(HttpClient http, string apiKey, string? model = null)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://api.openai.com/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _model = model ?? "gpt-4o-mini";
    }

    private record ChatMessage(string role, string content);
    private record ChatRequest(string model, ChatMessage[] messages, int? max_tokens = null, bool? stream = null, object? response_format = null);

    public async Task<string> StructuredJsonAsync(string userText, string extractionInstruction, CancellationToken ct)
    {
        var sys = "Extract a compact JSON per instruction. Return ONLY JSON. No commentary.";
        var req = new ChatRequest(
            model: _model,
            messages: new[] {
                new ChatMessage("system", sys + "\n" + extractionInstruction),
                new ChatMessage("user", userText)
            },
            max_tokens: 400,
            response_format: new { type = "json_object" }
        );
        return await SendAsync(req, ct);
    }

    public async Task<string> ShortReplyAsync(string prompt, object context, CancellationToken ct)
    {
        var sys = "Answer concisely (<= 35 words). Stay in role. Use world facts only from user content.";
        var user = JsonSerializer.Serialize(new { prompt, context });
        var req = new ChatRequest(
            model: _model,
            messages: new[] {
                new ChatMessage("system", sys),
                new ChatMessage("user", user)
            },
            max_tokens: 120
        );
        return await SendAsync(req, ct);
    }

    private async Task<string> SendAsync(ChatRequest req, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(req);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await _http.SendAsync(httpReq, ct);
        res.EnsureSuccessStatusCode();
        var str = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(str);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        return content ?? "";
    }
}
