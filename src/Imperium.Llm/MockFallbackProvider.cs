using Microsoft.Extensions.DependencyInjection;

namespace Imperium.Llm;

public class MockFallbackProvider : IFallbackLlmProvider
{
    private readonly IServiceProvider _sp;

    public MockFallbackProvider(IServiceProvider sp)
    {
        _sp = sp;
    }

    public ILlmClient? GetFallback()
    {
        // Prefer a registered MockLlmClient if available
        try
        {
            var mock = _sp.GetService<MockLlmClient>();
            if (mock != null) return mock;
        }
        catch (ObjectDisposedException)
        {
            // service provider disposed - fall back to creating a new mock instance
        }
        // Fallback to a generic MockLlmClient instance
        return new MockLlmClient();
    }
}
