using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Imperium.Llm;

/// <summary>
/// Router that examines a prompt for a role prefix like "[role:Npc]" and routes the request
/// to the appropriate model based on configuration Llm:RoleModelMap. Falls back to default model.
/// Supports Ollama (local) and OpenAI (Responses API).
/// </summary>
public class RoleLlmRouter : ILlmClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly LlmOptions _options;
    private readonly ILogger<RoleLlmRouter> _logger;
    private readonly IFallbackLlmProvider? _fallbackProvider;

    public RoleLlmRouter(IHttpClientFactory httpFactory, IConfiguration config, LlmOptions options, ILogger<RoleLlmRouter> logger, IFallbackLlmProvider? fallbackProvider = null)
    {
        _httpFactory = httpFactory ?? throw new ArgumentNullException(nameof(httpFactory));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _options = options ?? new LlmOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fallbackProvider = fallbackProvider;
    }

    public async Task<string> SendPromptAsync(string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;
        var traceId = Guid.NewGuid().ToString();
        using var scope = _logger.BeginScope(new Dictionary<string, object?> { ["TraceId"] = traceId, ["RolePromptHash"] = prompt?.GetHashCode() });

    var (role, cleaned) = ExtractRolePrefix(prompt!);
        var model = ResolveModelForRole(role) ?? _options.Model;

        _logger.LogInformation("RoleLlmRouter: resolved role='{Role}', model='{Model}', provider='{Provider}', traceId={TraceId}", role ?? "(none)", model, _options.Provider, traceId);

        try
        {
            if (_options.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("RoleLlmRouter: sending prompt to Ollama with model {Model} traceId={TraceId}", model, traceId);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var resp = await SendToOllamaAsync(cleaned, model, ct);
                    sw.Stop();
                    _logger.LogDebug("RoleLlmRouter: Ollama call completed in {ElapsedMs}ms traceId={TraceId}", sw.ElapsedMilliseconds, traceId);
                    return resp;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    _logger.LogWarning(ex, "RoleLlmRouter: Ollama call failed after {ElapsedMs}ms traceId={TraceId}", sw.ElapsedMilliseconds, traceId);
                    throw;
                }
            }

            _logger.LogDebug("RoleLlmRouter: sending prompt to OpenAI Responses with model {Model} traceId={TraceId}", model, traceId);
            // Default: OpenAI Responses API
            return await SendToOpenAiResponsesAsync(cleaned, model, _options.Temperature, ct);
        }
        catch (Exception ex)
        {
            // Protective fallback: if the external LLM provider is unreachable or fails,
            // fall back to the local MockLlmClient so the simulator remains functional in dev/test.
            _logger.LogWarning(ex, "RoleLlmRouter: LLM provider call failed for role='{Role}' model='{Model}', falling back to MockLlmClient traceId={TraceId}", role ?? "(none)", model, traceId);
            try
            {
                var fb = _fallbackProvider?.GetFallback();
                if (fb != null)
                {
                    _logger.LogDebug("RoleLlmRouter: using IFallbackLlmProvider-provided fallback traceId={TraceId}", traceId);
                    return await fb.SendPromptAsync(cleaned, ct);
                }

                _logger.LogDebug("RoleLlmRouter: no fallback provider available, creating local MockLlmClient traceId={TraceId}", traceId);
                var mock = new MockLlmClient();
                return await mock.SendPromptAsync(cleaned, ct);
            }
            catch (Exception mex)
            {
                _logger.LogError(mex, "RoleLlmRouter: Mock fallback also failed traceId={TraceId}", traceId);
                throw; // rethrow original failure if mock also fails
            }
        }
    }

    private static (string? role, string cleanedPrompt) ExtractRolePrefix(string prompt)
    {
        // pattern: [role:Name]\n or [role:Name] followed by space
        var m = Regex.Match(prompt, @"^\s*\[role:(?<r>[^\]]+)\]\s*(\r?\n)?", RegexOptions.IgnoreCase);
        if (!m.Success) return (null, prompt);
        var role = m.Groups["r"].Value.Trim();
        var cleaned = prompt.Substring(m.Length).TrimStart();
        return (role, cleaned);
    }

    private string? ResolveModelForRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;
        // Config path: Llm:RoleModelMap:Npc = "phi3:medium"
        var section = _config.GetSection($"Llm:RoleModelMap:{role}");
        if (section.Exists()) return section.Value;
        // also try case-insensitive lookup among children
        var map = _config.GetSection("Llm:RoleModelMap");
        foreach (var child in map.GetChildren())
        {
            if (string.Equals(child.Key, role, StringComparison.OrdinalIgnoreCase)) return child.Value;
        }
        return null;
    }

    private async Task<string> SendToOllamaAsync(string prompt, string model, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        var req = new { model = model, prompt = prompt, stream = false };
        var res = await http.PostAsJsonAsync("http://localhost:11434/api/generate", req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.TryGetProperty("response", out var text) ? text.GetString() ?? string.Empty : string.Empty;
    }

    private async Task<string> SendToOpenAiResponsesAsync(string prompt, string model, double temperature, CancellationToken ct)
    {
        var apiKey = _config["OpenAI:ApiKey"] ?? _config["OPENAI_API_KEY"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        var http = _httpFactory.CreateClient();

        var wrapped = new StringBuilder();
        wrapped.AppendLine("You are a strict JSON generator. You MUST respond with a single compact JSON object ONLY — no prose, no explanation, no markdown, no code fences, no extra fields.");
        wrapped.AppendLine("Return exactly the JSON object as the top-level response text. If you cannot, return {} (an empty object).\n");
        wrapped.AppendLine("Prompt:");
        wrapped.AppendLine(prompt);

        var req = new
        {
            model = model,
            temperature = temperature,
            input = wrapped.ToString()
        };

        var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(apiKey)) httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var res = await http.SendAsync(httpReq, ct);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync(ct);

        // Try to extract JSON object from Responses API or fallback regex
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
                            var candidate = inner;
                            if (candidate.Length > 1 && candidate[0] == '"' && candidate[^1] == '"')
                            {
                                try { candidate = JsonSerializer.Deserialize<string>(candidate) ?? candidate; } catch { }
                            }
                            var innerJson = ExtractFirstJsonObject(candidate ?? string.Empty);
                            if (!string.IsNullOrWhiteSpace(innerJson)) return innerJson;
                        }
                    }
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
        catch { /* ignore */ }

        var json = ExtractFirstJsonObject(body);
        return json ?? body;
    }

    private static string? ExtractFirstJsonObject(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var m = Regex.Match(input, @"\{(?:[^{}]|(?<open>\{)|(?<-open>\}))*\}(?(open)(?!))", RegexOptions.Singleline);
        // Use a balanced-brace regex to extract the first JSON object
        m = Regex.Match(input, @"\{(?:[^{}]|(?<open>\{)|(?<-open>\}))*\}(?(open)(?!))", RegexOptions.Singleline);
        if (m.Success) return m.Value.Trim();
        return null;
    }
}
