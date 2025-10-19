
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Imperium.ServiceDefaults;

public static class Extensions
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions();
        builder.Services.AddLogging(o => {
            o.AddSimpleConsole(c => {
                c.TimestampFormat = "HH:mm:ss ";
                c.SingleLine = true;
            });
        });
        return builder;
    }
}
