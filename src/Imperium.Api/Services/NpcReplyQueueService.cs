using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Imperium.Domain.Models;
using Imperium.Domain.Services;
using Imperium.Infrastructure;
using Imperium.Llm;

namespace Imperium.Api.Services;

public class NpcReplyQueueService : BackgroundService, INpcReplyQueue
{
    private readonly Channel<NpcReplyRequest> _channel;
    private long _droppedCount = 0;
    private long _processedCount = 0;
    private long _totalProcessingMs = 0;
    public long DroppedCount => Interlocked.Read(ref _droppedCount);
    public long ProcessedCount => Interlocked.Read(ref _processedCount);
    public long TotalProcessingMs => Interlocked.Read(ref _totalProcessingMs);
    private readonly IServiceProvider _sp;
    private readonly ILogger<NpcReplyQueueService> _logger;
    private readonly int _maxConcurrency = 4;

    public NpcReplyQueueService(IServiceProvider sp, ILogger<NpcReplyQueueService> logger)
    {
        _sp = sp;
        _logger = logger;
        _channel = Channel.CreateBounded<NpcReplyRequest>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public async ValueTask EnqueueAsync(NpcReplyRequest request)
    {
        try
        {
            await _channel.Writer.WriteAsync(request, request.CancellationToken);
        }
        catch (ChannelClosedException)
        {
            Interlocked.Increment(ref _droppedCount);
            _logger.LogWarning("NpcReplyQueueService: channel closed, dropped request for {Id}", request.CharacterId);
            return;
        }
        // update queued length metric
        var queueLength = _channel.Reader.Count; // not directly supported, approximate via readers/writers
        // We'll track queue length in MetricsService when queried by exposing counts below
    }

    // Helper for tests: process a single queued request synchronously.
    public async Task<bool> ProcessNextAsync(CancellationToken ct = default)
    {
        if (!await _channel.Reader.WaitToReadAsync(ct)) return false;
        if (!_channel.Reader.TryRead(out var req)) return false;
        return await ProcessRequestAsync(req, ct);
    }

    // Process a specific request directly (useful for tests)
    public async Task<bool> ProcessRequestAsync(NpcReplyRequest req, CancellationToken ct = default)
    {
        try
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Imperium.Infrastructure.ImperiumDbContext>();
            var llm = scope.ServiceProvider.GetRequiredService<ILlmClient>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();

            var ch = await db.Characters.FirstOrDefaultAsync(c => c.Id == req.CharacterId, ct);
            if (ch == null)
            {
                _logger.LogWarning("Character {Id} not found for npc reply.", req.CharacterId);
                return false;
            }

            var arche = req.Archetype ?? NpcUtils.InferArchetype(ch.SkillsJson);
            var prompt = NpcUtils.BuildPrompt(ch, arche);
            var forbidden = new[] { "интернет", "сервер", "компьютер", "смартфон", "телефон", "электрон", "email", "github", "stackoverflow" };

            const int maxAttempts = 3;
            const int perCallTimeoutSec = 8;
            var overallTimeout = TimeSpan.FromSeconds(15);
            using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(req.CancellationToken, ct);
            overallCts.CancelAfter(overallTimeout);

            string? finalReply = null;
            int? finalMood = null;
            int reasks = 0;
            int sanitizations = 0;

            for (int attempt = 1; attempt <= maxAttempts && !overallCts.Token.IsCancellationRequested; attempt++)
            {
                using var perCall = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);
                perCall.CancelAfter(TimeSpan.FromSeconds(perCallTimeoutSec));
                string llmRaw = string.Empty;
                try
                {
                    var callPrompt = attempt == 1 ? prompt : (attempt == 2 ? NpcUtils.ReaskPrompt() : NpcUtils.BuildRewritePrompt(finalReply ?? ""));
                    var resp = await llm.SendPromptAsync(callPrompt, perCall.Token);
                    llmRaw = resp ?? string.Empty;
                }
                catch (OperationCanceledException) when (overallCts.IsCancellationRequested)
                {
                    _logger.LogInformation("NpcReplyQueueService: overall timeout reached for {Id}", req.CharacterId);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "NpcReplyQueueService: LLM call error for {Id} attempt {Attempt}", req.CharacterId, attempt);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(llmRaw)) continue;

                if (NpcUtils.IsSignificantLatinOrTechnical(llmRaw) || NpcUtils.HasForbiddenTokens(llmRaw, forbidden))
                {
                    reasks++;
                    continue;
                }

                if (NpcUtils.TryParseNpcReply(llmRaw, out var parsedReply, out var mood))
                {
                    if (NpcUtils.IsSignificantLatinOrTechnical(parsedReply) || NpcUtils.HasForbiddenTokens(parsedReply, forbidden))
                    {
                        sanitizations++;
                        parsedReply = NpcUtils.SanitizeReply(parsedReply, forbidden);
                    }

                    finalReply = parsedReply.Length > 350 ? parsedReply[..350] : parsedReply;
                    finalMood = mood;
                    break;
                }
                else
                {
                    var cleaned = NpcUtils.SanitizeReply(llmRaw, forbidden);
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        finalReply = cleaned;
                        finalMood = null;
                        sanitizations++;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(finalReply))
            {
                _logger.LogInformation("NpcReplyQueueService: no valid reply for {Id} after attempts reasks={Reasks} sanitizations={Sanitizations}", req.CharacterId, reasks, sanitizations);
                finalReply = "(нет ответа)";
            }

            var metrics = scope.ServiceProvider.GetService<Imperium.Api.MetricsService>();
            metrics?.Add("npc.queue.reasks", reasks);
            if (sanitizations > 0) metrics?.Add("npc.queue.sanitizations", sanitizations);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ge = new GameEvent
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                Type = "npc_reply",
                Location = ch.LocationName ?? "unknown",
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { characterId = ch.Id, name = ch.Name, reply = finalReply, moodDelta = finalMood, meta = new { reasks, sanitizations } }),
            };
            await dispatcher.EnqueueAsync(ge);
            sw.Stop();
            Interlocked.Increment(ref _processedCount);
            Interlocked.Add(ref _totalProcessingMs, sw.ElapsedMilliseconds);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing npc reply request");
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>();
        var sem = new SemaphoreSlim(_maxConcurrency);

        await foreach (var req in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            await sem.WaitAsync(stoppingToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    using var scope = _sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<Imperium.Infrastructure.ImperiumDbContext>();
                    var llm = scope.ServiceProvider.GetRequiredService<ILlmClient>();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<Imperium.Domain.Services.IEventDispatcher>();

                    var ch = await db.Characters.FirstOrDefaultAsync(c => c.Id == req.CharacterId, stoppingToken);
                    if (ch == null)
                    {
                        _logger.LogWarning("Character {Id} not found for npc reply.", req.CharacterId);
                        return;
                    }

                    var arche = req.Archetype ?? NpcUtils.InferArchetype(ch.SkillsJson);
                    var prompt = NpcUtils.BuildPrompt(ch, arche);
                    var forbidden = new[] { "интернет", "сервер", "компьютер", "смартфон", "телефон", "электрон", "email", "github", "stackoverflow" };

                    // Attempts: try primary prompt, then reask once, then rewrite once. Keep overall time bounded.
                    const int maxAttempts = 3;
                    const int perCallTimeoutSec = 8;
                    var overallTimeout = TimeSpan.FromSeconds(15);
                    using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(req.CancellationToken);
                    overallCts.CancelAfter(overallTimeout);

                    string? finalReply = null;
                    int? finalMood = null;
                    int reasks = 0;
                    int sanitizations = 0;

                    for (int attempt = 1; attempt <= maxAttempts && !overallCts.Token.IsCancellationRequested; attempt++)
                    {
                        CancellationTokenSource perCall = CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token);
                        perCall.CancelAfter(TimeSpan.FromSeconds(perCallTimeoutSec));
                        string llmRaw = string.Empty;
                        try
                        {
                            var callPrompt = attempt == 1 ? prompt : (attempt == 2 ? NpcUtils.ReaskPrompt() : NpcUtils.BuildRewritePrompt(finalReply ?? ""));
                            var resp = await llm.SendPromptAsync(callPrompt, perCall.Token);
                            llmRaw = resp ?? string.Empty;
                        }
                        catch (OperationCanceledException) when (overallCts.IsCancellationRequested)
                        {
                            _logger.LogInformation("NpcReplyQueueService: overall timeout reached for {Id}", req.CharacterId);
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "NpcReplyQueueService: LLM call error for {Id} attempt {Attempt}", req.CharacterId, attempt);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(llmRaw)) continue;

                        // if contains forbidden technical tokens, prefer reask
                        if (NpcUtils.IsSignificantLatinOrTechnical(llmRaw) || NpcUtils.HasForbiddenTokens(llmRaw, forbidden))
                        {
                            reasks++;
                            continue;
                        }

                        if (NpcUtils.TryParseNpcReply(llmRaw, out var parsedReply, out var mood))
                        {
                            // sanitize reply if needed
                            if (NpcUtils.IsSignificantLatinOrTechnical(parsedReply) || NpcUtils.HasForbiddenTokens(parsedReply, forbidden))
                            {
                                sanitizations++;
                                parsedReply = NpcUtils.SanitizeReply(parsedReply, forbidden);
                            }

                            finalReply = parsedReply.Length > 350 ? parsedReply[..350] : parsedReply;
                            finalMood = mood;
                            break;
                        }
                        else
                        {
                            // try to sanitize free text
                            var cleaned = NpcUtils.SanitizeReply(llmRaw, forbidden);
                            if (!string.IsNullOrWhiteSpace(cleaned))
                            {
                                finalReply = cleaned;
                                finalMood = null;
                                sanitizations++;
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(finalReply))
                    {
                        _logger.LogInformation("NpcReplyQueueService: no valid reply for {Id} after attempts reasks={Reasks} sanitizations={Sanitizations}", req.CharacterId, reasks, sanitizations);
                        finalReply = "(нет ответа)";
                    }

                    // Metrics if available
                    var metrics = scope.ServiceProvider.GetService<Imperium.Api.MetricsService>();
                    metrics?.Add("npc.queue.reasks", reasks);
                    if (sanitizations > 0) metrics?.Add("npc.queue.sanitizations", sanitizations);

                    var ge = new GameEvent
                    {
                        Id = Guid.NewGuid(),
                        Timestamp = DateTime.UtcNow,
                        Type = "npc_reply",
                        Location = ch.LocationName ?? "unknown",
                        PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { characterId = ch.Id, name = ch.Name, reply = finalReply, moodDelta = finalMood, meta = new { reasks, sanitizations } }),
                    };

                    await dispatcher.EnqueueAsync(ge);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing npc reply request");
                }
                finally
                {
                    sem.Release();
                }
            }, CancellationToken.None));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch { }
    }
}
