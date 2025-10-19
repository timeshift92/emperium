namespace Imperium.Llm;

using Microsoft.Extensions.DependencyInjection;

public class LlmClientFactory
{
    private readonly IServiceProvider _sp;
    private readonly LlmOptions _opt;

    public LlmClientFactory(IServiceProvider sp, LlmOptions opt)
    {
        _sp = sp;
        _opt = opt;
    }

    public ILlmClient CreateClient()
        => _opt.Provider.ToLower() switch
        {
            "ollama" => ActivatorUtilities.CreateInstance<OllamaLlmClient>(_sp),
            _ => ActivatorUtilities.CreateInstance<OpenAiLlmClient>(_sp)
        };
}
