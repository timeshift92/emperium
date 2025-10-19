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
                    await a.TickAsync(scope.ServiceProvider, stoppingToken);
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
