using System.Collections.Generic;
using System.Linq;

namespace Imperium.Api.Services;

public enum LogisticsJobStatus
{
    Pending,
    Processing,
    WaitingFunds,
    Completed,
    Failed
}

public class LogisticsJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? FromLocationId { get; init; }
    public Guid? ToLocationId { get; init; }
    public string Item { get; init; } = "grain";
    public decimal Volume { get; init; }
    public decimal ExpectedProfit { get; init; }
    public decimal CostEstimate { get; set; }
    public LogisticsJobStatus Status { get; set; } = LogisticsJobStatus.Pending;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ReservedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public string? Note { get; set; }
}

public class LogisticsQueueService
{
    private readonly List<LogisticsJob> _jobs = new();
    private readonly object _lock = new();
    private readonly LogisticsOptions _options;
    private readonly Imperium.Api.EconomyStateService _econState;

    public LogisticsQueueService(Microsoft.Extensions.Options.IOptions<LogisticsOptions> options, Imperium.Api.EconomyStateService econState)
    {
        _options = options?.Value ?? new LogisticsOptions();
        _econState = econState;
    }

    public LogisticsJob Enqueue(Guid? from, Guid? to, string item, decimal volume, decimal expectedProfit)
    {
        var job = new LogisticsJob
        {
            FromLocationId = from,
            ToLocationId = to,
            Item = item,
            Volume = Math.Max(1m, volume),
            ExpectedProfit = Math.Max(0m, expectedProfit),
            CostEstimate = EstimateCost(from, to, volume, item)
        };
        lock (_lock)
        {
            _jobs.Add(job);
        }
        return job;
    }

    public LogisticsJob? TryStartNext(DateTime utcNow)
    {
        lock (_lock)
        {
            var job = _jobs
                .Where(j => j.Status == LogisticsJobStatus.Pending || (j.Status == LogisticsJobStatus.WaitingFunds && j.NextAttemptAt <= utcNow))
                .OrderBy(j => j.CreatedAt)
                .FirstOrDefault();
            if (job == null) return null;
            job.Status = LogisticsJobStatus.Processing;
            job.ReservedAt = utcNow;
            return job;
        }
    }

    public void Update(LogisticsJob job, LogisticsJobStatus status, string? note = null, TimeSpan? retryDelay = null)
    {
        lock (_lock)
        {
            var stored = _jobs.FirstOrDefault(x => x.Id == job.Id);
            if (stored == null) return;
            stored.Status = status;
            stored.Note = note;
            if (status == LogisticsJobStatus.Completed)
            {
                stored.CompletedAt = DateTime.UtcNow;
            }
            else if (status == LogisticsJobStatus.WaitingFunds)
            {
                stored.NextAttemptAt = DateTime.UtcNow + (retryDelay ?? TimeSpan.FromMinutes(5));
            }
        }
    }

    public IReadOnlyCollection<LogisticsJob> Snapshot()
    {
        lock (_lock)
        {
            return _jobs
                .Select(j => new LogisticsJob
                {
                    Id = j.Id,
                    FromLocationId = j.FromLocationId,
                    ToLocationId = j.ToLocationId,
                    Item = j.Item,
                    Volume = j.Volume,
                    ExpectedProfit = j.ExpectedProfit,
                    CostEstimate = j.CostEstimate,
                    Status = j.Status,
                    CreatedAt = j.CreatedAt,
                    ReservedAt = j.ReservedAt,
                    CompletedAt = j.CompletedAt,
                    NextAttemptAt = j.NextAttemptAt,
                    Note = j.Note
                })
                .ToList();
        }
    }

    private decimal EstimateCost(Guid? from, Guid? to, decimal volume, string? item = null)
    {
        var distance = _options.ResolveDistance(from, to);
        // Adjust cost based on weight of the item (kg per unit)
        var weightPerUnit = 1m;
        if (!string.IsNullOrWhiteSpace(item))
        {
            var def = _econState.GetDefinition(item!);
            if (def != null) weightPerUnit = def.WeightPerUnit;
        }
        var cost = _options.BaseCostPerUnit * volume * weightPerUnit;
        if (distance > 0)
        {
            cost += distance * _options.DistanceMultiplier * volume * weightPerUnit;
        }
        return Math.Round(cost, 2);
    }
}
