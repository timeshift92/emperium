using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Net.Http.Headers;
using Xunit;

namespace Imperium.Llm.Tests;

public class RoleLlmRouterTests
{
    [Fact]
    public async Task SendPromptAsync_OllamaPath_ReturnsResponseText()
    {
        // Arrange: fake HttpClient that returns a JSON with response
        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ \"response\": \"{\\\"reply\\\": \\\"OK\\\"}\" }")
        });
        var http = new HttpClient(handler);
        var factory = new SimpleFactory(http);

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var options = new LlmOptions { Provider = "ollama", Model = "mistral" };

    var router = new RoleLlmRouter(factory, config, options, new NullLogger<RoleLlmRouter>(), null);

        // Act
        var result = await router.SendPromptAsync("[role:World]\nGenerate something", CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("reply", result);
    }

    [Fact]
    public async Task SendPromptAsync_OnFailure_UsesFallback()
    {
        // Arrange: HttpClient that throws
        var handler2 = new FakeHandler(ex: new HttpRequestException("boom"));
        var http2 = new HttpClient(handler2);
        var factory2 = new SimpleFactory(http2);

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var options = new LlmOptions { Provider = "ollama", Model = "phi3:medium" };

    var fallbackClient = new SimpleLlmClient("{ \"reply\": \"fallback\" }");
    var fallbackProv = new SimpleFallbackProvider(fallbackClient);

    var router = new RoleLlmRouter(factory2, config, options, new NullLogger<RoleLlmRouter>(), fallbackProv);

        // Act
        var result = await router.SendPromptAsync("[role:Npc]\nHi", CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("fallback", result);
    }
}

// Simple fake HttpMessageHandler to emulate responses or throw
class FakeHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;
    private readonly Exception _ex;
    public FakeHandler(HttpResponseMessage response = null, Exception ex = null)
    {
        _response = response;
        _ex = ex;
    }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_ex != null) throw _ex;
        return Task.FromResult(_response ?? new HttpResponseMessage(HttpStatusCode.OK));
    }
}

class SimpleFactory : IHttpClientFactory
{
    private readonly HttpClient _client;
    public SimpleFactory(HttpClient client) => _client = client;
    public HttpClient CreateClient(string name) => _client;
}

class SimpleLlmClient : ILlmClient
{
    private readonly string _resp;
    public SimpleLlmClient(string resp) => _resp = resp;
    public Task<string> SendPromptAsync(string prompt, CancellationToken ct) => Task.FromResult(_resp);
}

class SimpleFallbackProvider : IFallbackLlmProvider
{
    private readonly ILlmClient _client;
    public SimpleFallbackProvider(ILlmClient client) => _client = client;
    public ILlmClient GetFallback() => _client;
}
