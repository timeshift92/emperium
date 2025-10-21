using System.Diagnostics;
using Imperium.Llm;
using Microsoft.Extensions.Logging;

namespace Imperium.Api.Services;

/// <summary>
/// Decorator for ILlmClient that emits OpenTelemetry activities and MetricsService counters.
/// </summary>
public class LlmMetricsDecorator : ILlmClient
{
    private static readonly ActivitySource ActivitySource = new("Imperium.Llm");

    private readonly ILlmClient _inner;
    private readonly MetricsService _metrics;
    private readonly ILogger<LlmMetricsDecorator>? _logger;

    public LlmMetricsDecorator(ILlmClient inner, MetricsService metrics, ILogger<LlmMetricsDecorator>? logger = null)
    {
        _inner = inner;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<string> SendPromptAsync(string prompt, CancellationToken ct = default)
    {
        _metrics.Increment("llm.requests");
        using var activity = ActivitySource.StartActivity("llm.request", ActivityKind.Internal);
        activity?.SetTag("llm.prompt.length", prompt.Length);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _inner.SendPromptAsync(prompt, ct) ?? string.Empty;
            stopwatch.Stop();

            _metrics.Increment("llm.success");
            _metrics.RecordLlmDuration(stopwatch.Elapsed.TotalMilliseconds);

            activity?.SetTag("llm.response.length", response.Length);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _metrics.Increment("llm.canceled");
            _metrics.RecordLlmDuration(stopwatch.Elapsed.TotalMilliseconds);

            activity?.SetStatus(ActivityStatusCode.Ok, "canceled");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.Increment("llm.errors");
            _metrics.RecordLlmDuration(stopwatch.Elapsed.TotalMilliseconds);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger?.LogWarning(ex, "LLM request failed");
            throw;
        }
    }
}
