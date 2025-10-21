using Imperium.Domain.Models;
using Imperium.Domain.Services;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Imperium.Tests
{
    public class FakeEventDispatcher : IEventDispatcher
    {
        private readonly ConcurrentQueue<GameEvent> _events = new();

        public IReadOnlyCollection<GameEvent> Events => _events.ToArray();

        public ValueTask EnqueueAsync(GameEvent e)
        {
            _events.Enqueue(e);
            return ValueTask.CompletedTask;
        }
    }
}
