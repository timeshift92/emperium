using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Imperium.Infrastructure;

namespace Imperium.Api.IntegrationTests
{
    // Test dispatcher: synchronously persists events into the DB for deterministic testing
    public class TestEventDispatcher : Imperium.Domain.Services.IEventDispatcher
    {
        private readonly IServiceProvider _sp;
        public TestEventDispatcher(IServiceProvider sp) => _sp = sp;
        public async ValueTask EnqueueAsync(Imperium.Domain.Models.GameEvent e)
        {
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
            db.GameEvents.Add(e);
            await db.SaveChangesAsync();
        }
    }
}
