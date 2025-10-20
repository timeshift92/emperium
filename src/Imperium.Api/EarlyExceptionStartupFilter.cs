using System;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Imperium.Api
{
    // Ensures our safe JSON error middleware is added as the very first middleware
    public class EarlyExceptionStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, _next) =>
                {
                    try
                    {
                        await _next();
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            var logger = context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("EarlyExceptionStartupFilter");
                            logger?.LogError(ex, "Unhandled exception (early filter)");
                        }
                        catch { }

                        if (!context.Response.HasStarted)
                        {
                            context.Response.Clear();
                            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                            context.Response.ContentType = "application/json";
                            var payload = new { error = "internal_server_error", message = ex.Message };
                            var json = JsonSerializer.Serialize(payload);
                            await context.Response.WriteAsync(json);
                        }
                        else
                        {
                            throw;
                        }
                    }
                });

                next(app);
            };
        }
    }
}
