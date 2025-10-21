using Imperium.Llm;
using System.Threading;
using System.Threading.Tasks;

namespace Imperium.Tests
{
    // Very small mock used in unit tests. It returns preconfigured responses sequentially.
    public class MockLlmClient : ILlmClient
    {
        private readonly Queue<string> _responses = new();

        public MockLlmClient(IEnumerable<string> responses)
        {
            foreach (var r in responses) _responses.Enqueue(r);
        }

        public Task<string> SendPromptAsync(string prompt, CancellationToken ct = default)
        {
            if (_responses.Count == 0) return Task.FromResult(string.Empty);
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
