using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Imperium.Api;

public class StartupLog : IStartupFilter
{
    private readonly string _message;
    public StartupLog(string message) => _message = message;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            var logger = app.ApplicationServices.GetRequiredService<ILogger<StartupLog>>();
            logger.LogInformation("LLM client: {Message}", _message);
            next(app);
        };
    }
}
