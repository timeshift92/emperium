using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Imperium.Infrastructure;
using Imperium.Api.Services;
using Xunit;

namespace Imperium.Api.Tests
{
    public class EventTraceIdTests
    {
        [Fact]
        public async Task NewEvent_Includes_Meta_TraceId()
        {
            var ev = typeof(InheritanceService).GetMethod("NewEvent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(ev);
            var ge = (Imperium.Domain.Models.GameEvent?)ev!.Invoke(null, new object[] { "test", "loc", new { foo = "bar" } });
            Assert.NotNull(ge);
            var payload = JsonDocument.Parse(ge!.PayloadJson);
            Assert.True(payload.RootElement.TryGetProperty("meta", out var meta));
            Assert.True(meta.TryGetProperty("traceId", out var trace));
            Assert.False(string.IsNullOrWhiteSpace(trace.GetString()));
        }
    }
}
