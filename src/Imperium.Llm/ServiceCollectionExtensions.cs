using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Imperium.Llm;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLlm(this IServiceCollection services, IConfiguration config)
    {
        var opt = config.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();
        services.AddSingleton(opt);

        // Register router which will decide which backend/model to call based on prompt role prefix and config
        services.AddHttpClient(); // for IHttpClientFactory
        services.AddTransient<ILlmClient, RoleLlmRouter>();

        return services;
    }
}
