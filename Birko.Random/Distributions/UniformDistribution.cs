using System;

namespace Birko.Random;

/// <summary>
/// Generates uniformly distributed random values in a specified range.
/// </summary>
public sealed class UniformDistribution
{
    private readonly IRandomProvider _provider;
    private readonly double _min;
    private readonly double _max;

    public UniformDistribution(IRandomProvider provider, double min = 0.0, double max = 1.0)
    {
        if (min >= max)
        {
            throw new ArgumentOutOfRangeException(nameof(min), "min must be less than max.");
        }

        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _min = min;
        _max = max;
    }

    /// <summary>
    /// Returns the next uniformly distributed value in [min, max).
    /// </summary>
    public double Next() => _min + _provider.NextDouble() * (_max - _min);

    /// <summary>
    /// Returns the next uniformly distributed integer in [min, max).
    /// </summary>
    public int NextInt(int min, int max) => _provider.NextInt(min, max);
}
