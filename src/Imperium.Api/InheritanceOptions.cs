namespace Imperium.Api
{
    public class InheritanceOptions
    {
        public TieBreakerOption TieBreaker { get; set; } = TieBreakerOption.DeterministicHash;
        // Salt used for deterministic hash tie-breaking
        public string? Salt { get; set; }
    }

    public enum TieBreakerOption
    {
        Stable,
        DeterministicHash,
        Random
    }
}
