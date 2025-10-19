using System;

namespace Imperium.Domain.Agents;

public interface IWorldAgent
{
    string Name { get; }
    /// <summary>
    /// Execute one tick. The implementation may resolve required services from the provided IServiceProvider scope.
    /// </summary>
    Task TickAsync(IServiceProvider scopeServices, CancellationToken ct);
}
