using Microsoft.Extensions.Hosting;
using System.Diagnostics;

namespace Imperium.Api;

public class TickWorker : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("Imperium.TickWorker");

    private readonly IServiceProvider _sp;
    private readonly ILogger<TickWorker> _logger;

    public TickWorker(IServiceProvider sp, ILogger<TickWorker> logger)
    {
        _sp = sp;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TickWorker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            Activity? tickActivity = null;
            Imperium.Api.MetricsService? metrics = null;

            try
            {
                using var scope = _sp.CreateScope();
                metrics = scope.ServiceProvider.GetService<Imperium.Api.MetricsService>();
                var agents = scope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>()
                    // ensure TimeAI runs first
                    .OrderBy(a => a.Name == "TimeAI" ? 0 : 1)
                    .ToList();

                metrics?.Increment("tick.started");
                tickActivity = ActivitySource.StartActivity("world.tick", ActivityKind.Internal);
                tickActivity?.SetTag("agent.count", agents.Count);

                var tickStopwatch = Stopwatch.StartNew();
                foreach (var a in agents)
                {
                    _logger.LogInformation("AI Tick: {Agent}", a.Name);
                    using var agentActivity = ActivitySource.StartActivity("world.agent", ActivityKind.Internal);
                    agentActivity?.SetTag("agent.name", a.Name);
                    var agentWatch = Stopwatch.StartNew();

                    // Give each agent its own short-lived cancellation token so a slow or blocked
                    // agent won't cancel the whole worker loop. This keeps ticks robust while
                    // still respecting application shutdown (stoppingToken).
                    using var agentCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    // per-agent execution timeout (shorter than full tick) to avoid long blocking
                    agentCts.CancelAfter(TimeSpan.FromSeconds(15));
                    try
                    {
                        await a.TickAsync(scope.ServiceProvider, agentCts.Token);
                        metrics?.Increment($"agents.{a.Name}.success");
                        agentActivity?.SetStatus(ActivityStatusCode.Ok);
                    }
                    catch (OperationCanceledException)
                    {
                        // Could be a per-agent timeout or application shutdown; treat as non-fatal
                        _logger.LogWarning("Tick for {Agent} canceled (timeout or shutdown)", a.Name);
                        metrics?.Increment($"agents.{a.Name}.timeouts");
                        agentActivity?.SetStatus(ActivityStatusCode.Ok, "timeout");
                    }
                    catch (Exception agEx)
                    {
                        // Log agent-level failures but keep the loop running
                        _logger.LogWarning(agEx, "Agent {Agent} failed", a.Name);
                        metrics?.Increment($"agents.{a.Name}.errors");
                        agentActivity?.SetStatus(ActivityStatusCode.Error, agEx.Message);
                    }
                    finally
                    {
                        agentWatch.Stop();
                        metrics?.RecordAgentDuration(a.Name, agentWatch.Elapsed.TotalMilliseconds);
                    }
                }
                tickStopwatch.Stop();
                metrics?.RecordTickDuration(tickStopwatch.Elapsed.TotalMilliseconds);
                metrics?.Increment("tick.completed");
                tickActivity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                metrics?.Increment("tick.errors");
                tickActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Tick failed");
            }
            finally
            {
                tickActivity?.Dispose();
            }
            // 1 тик = 30s
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    // Exposed for testing: run a single tick synchronously using the same logic as the loop
    public async Task TickOnceAsync()
    {
        using var scope = _sp.CreateScope();
        var agents = scope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>()
            .OrderBy(a => a.Name == "TimeAI" ? 0 : 1)
            .ToList();
        foreach (var a in agents)
        {
            using var agentCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await a.TickAsync(scope.ServiceProvider, agentCts.Token);
            }
            catch { }
        }
    }
}
