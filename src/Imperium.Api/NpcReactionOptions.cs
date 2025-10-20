namespace Imperium.Api;

public class NpcReactionOptions
{
    public double BaseProbability { get; set; } = 0.05;
    public double AttachmentWeight { get; set; } = 0.30;
    public double GreedWeight { get; set; } = 0.15;
    public double RelClaimantWeight { get; set; } = 0.25;
    public double RelOwnerWeight { get; set; } = 0.20;
    public double MaxProbability { get; set; } = 0.95;
}

