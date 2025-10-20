using Microsoft.Extensions.Hosting;

namespace Imperium.Api;

public class TickWorker : BackgroundService
{
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
            try
            {
                using var scope = _sp.CreateScope();
                var agents = scope.ServiceProvider.GetServices<Imperium.Domain.Agents.IWorldAgent>()
                    // ensure TimeAI runs first
                    .OrderBy(a => a.Name == "TimeAI" ? 0 : 1)
                    .ToList();
                foreach (var a in agents)
                {
                    _logger.LogInformation("AI Tick: {Agent}", a.Name);

                    // Give each agent its own short-lived cancellation token so a slow or blocked
                    // agent won't cancel the whole worker loop. This keeps ticks robust while
                    // still respecting application shutdown (stoppingToken).
                    using var agentCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    // per-agent execution timeout (shorter than full tick) to avoid long blocking
                    agentCts.CancelAfter(TimeSpan.FromSeconds(15));
                    try
                    {
                        await a.TickAsync(scope.ServiceProvider, agentCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Could be a per-agent timeout or application shutdown; treat as non-fatal
                        _logger.LogWarning("Tick for {Agent} canceled (timeout or shutdown)", a.Name);
                    }
                    catch (Exception agEx)
                    {
                        // Log agent-level failures but keep the loop running
                        _logger.LogWarning(agEx, "Agent {Agent} failed", a.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tick failed");
            }
            // 1 тик = 30s
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
