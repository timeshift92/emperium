using Imperium.Domain.Models;
using Imperium.Domain.Services;
using Imperium.Infrastructure;
using Microsoft.Extensions.Hosting;
using System.Threading.Channels;

namespace Imperium.Api
{
    public class EventDispatcherService : BackgroundService, IEventDispatcher
    {
        private readonly Channel<GameEvent> _channel = Channel.CreateUnbounded<GameEvent>(new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });
        private readonly IServiceProvider _services;
        private readonly EventStreamService _stream;
        private readonly ILogger<EventDispatcherService> _logger;

        public EventDispatcherService(IServiceProvider services, EventStreamService stream, ILogger<EventDispatcherService> logger)
        {
            _services = services;
            _stream = stream;
            _logger = logger;
        }

        public ValueTask EnqueueAsync(GameEvent e)
        {
            var written = _channel.Writer.TryWrite(e);
            if (!written)
            {
                _logger.LogWarning("EventDispatcher: channel full, dropping event {EventId}", e.Id);
            }
            return ValueTask.CompletedTask;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var ev in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
                    db.GameEvents.Add(ev);
                    await db.SaveChangesAsync(stoppingToken);

                    // publish to SSE stream (fire-and-forget is ok)
                    try { await _stream.PublishEventAsync(ev); } catch (Exception ex) { _logger.LogError(ex, "Failed to publish event {EventId}", ev.Id); }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "EventDispatcher: failed to persist event {EventId}", ev.Id);
                }
            }
        }
    }
}
