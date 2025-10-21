using System;
using Imperium.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Imperium.Domain.Models;
using Imperium.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Imperium.Api.Tests;

internal class TestEventDispatcher : IEventDispatcher
{
    private readonly IServiceProvider _sp;

    public TestEventDispatcher(IServiceProvider sp)
    {
        _sp = sp;
    }

    public ValueTask EnqueueAsync(GameEvent e)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ImperiumDbContext>();
        // Avoid duplicate insert if the event was already persisted as part of a transaction
        var exists = db.GameEvents.AsNoTracking().Any(x => x.Id == e.Id);
        if (!exists)
        {
            db.GameEvents.Add(e);
            db.SaveChanges();
        }
        return ValueTask.CompletedTask;
    }
}
