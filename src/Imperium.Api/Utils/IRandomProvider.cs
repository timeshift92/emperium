namespace Imperium.Api.Utils;

public interface IRandomProvider
{
    double NextDouble();
    int NextInt(int maxExclusive);
}

public class SeedableRandom : IRandomProvider
{
    private readonly Random _r;
    public SeedableRandom() : this(Environment.TickCount) { }
    public SeedableRandom(int seed) { _r = new Random(seed); }
    public double NextDouble() => _r.NextDouble();
    public int NextInt(int maxExclusive) => _r.Next(maxExclusive);
}
