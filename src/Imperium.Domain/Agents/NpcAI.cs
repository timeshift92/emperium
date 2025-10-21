namespace Imperium.Domain.Agents;

public class NpcAI : IWorldAgent
{
    public string Name => "NpcAI";

    public Task TickAsync(IServiceProvider scopeServices, CancellationToken ct)
    {
        // This stub was replaced by the API-level NpcBehaviorAgent.
        return Task.CompletedTask;

    }
}
