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
        var mock = _sp.GetService<MockLlmClient>();
        if (mock != null) return mock;
        // Fallback to a generic MockLlmClient instance
        return new MockLlmClient();
    }
}
