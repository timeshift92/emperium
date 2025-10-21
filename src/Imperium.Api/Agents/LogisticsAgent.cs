using Imperium.Domain.Agents;
using Imperium.Infrastructure;
using Imperium.Api.Services;
using Imperium.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Imperium.Api.Agents;

public class LogisticsAgent : IWorldAgent
{
    public string Name => "LogisticsAI";

    public async Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        var db = scopeServices.GetRequiredService<ImperiumDbContext>();
        var dispatcher = scopeServices.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();
        var queue = scopeServices.GetRequiredService<LogisticsQueueService>();
        var metrics = scopeServices.GetService<Imperium.Api.MetricsService>();

        int processed = 0;
        while (processed < 3)
        {
            var job = queue.TryStartNext(DateTime.UtcNow);
            if (job == null) break;

            try
            {
                Location? fromCity = null;
                if (job.FromLocationId.HasValue)
                {
                    fromCity = await db.Locations.FindAsync(new object?[] { job.FromLocationId.Value }, ct);
                    if (fromCity == null)
                    {
                        queue.Update(job, LogisticsJobStatus.Failed, "from_location_not_found");
                        continue;
                    }
                    if (fromCity.Treasury < job.CostEstimate)
                    {
                        queue.Update(job, LogisticsJobStatus.WaitingFunds, "insufficient_treasury", TimeSpan.FromMinutes(5));
                        metrics?.Increment("logistics.jobs.waiting");
                        continue;
                    }
                    fromCity.Treasury -= job.CostEstimate;
                }

                if (job.ToLocationId.HasValue)
                {
                    var dest = await db.Locations.FindAsync(new object?[] { job.ToLocationId.Value }, ct);
                    if (dest != null)
                    {
                        dest.Treasury += job.ExpectedProfit;
                    }
                }

                await db.SaveChangesAsync(ct);

                queue.Update(job, LogisticsJobStatus.Completed);
                metrics?.Increment("logistics.jobs.completed");

                var completed = new Imperium.Domain.Models.GameEvent
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow,
                    Type = "logistics_completed",
                    Location = job.ToLocationId?.ToString() ?? "global",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        jobId = job.Id,
                        from = job.FromLocationId,
                        to = job.ToLocationId,
                        item = job.Item,
                        volume = job.Volume,
                        cost = job.CostEstimate,
                        profit = job.ExpectedProfit
                    })
                };
                await dispatcher.EnqueueAsync(completed);
            }
            catch (Exception ex)
            {
                queue.Update(job, LogisticsJobStatus.Failed, ex.Message);
            }

            processed++;
        }
    }
}

