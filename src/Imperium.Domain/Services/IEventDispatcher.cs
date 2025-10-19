using Imperium.Domain.Models;

namespace Imperium.Domain.Services
{
    public interface IEventDispatcher
    {
        /// <summary>
        /// Enqueue event for persistence and streaming. Must be fast and non-blocking for agents.
        /// </summary>
        /// <param name="e">GameEvent to enqueue</param>
        ValueTask EnqueueAsync(GameEvent e);
    }
}
