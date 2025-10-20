using System;
using Imperium.Domain.Models;
using Imperium.Domain.Services;
using Imperium.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ReclaimRunner
{
    public class TestEventDispatcher : IEventDispatcher
{
    private readonly IServiceProvider _sp;

    public TestEventDispatcher(IServiceProvider sp)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
    }

    public ValueTask EnqueueAsync(GameEvent e)
    {
        // Persist synchronously using a scope to mimic dispatcher behaviour but deterministically
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        db.GameEvents.Add(e);
        db.SaveChanges();
        return ValueTask.CompletedTask;
    }
}
}
