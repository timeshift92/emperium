using System.Threading.Channels;
using Imperium.Domain.Models;

namespace Imperium.Api.Services;

public record NpcReplyRequest(Guid CharacterId, string Archetype, CancellationToken CancellationToken);

public interface INpcReplyQueue
{
    ValueTask EnqueueAsync(NpcReplyRequest request);
}
