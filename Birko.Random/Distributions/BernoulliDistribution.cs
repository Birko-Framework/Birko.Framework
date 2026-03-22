using System;

namespace Birko.Random;

/// <summary>
/// Generates Bernoulli-distributed random values (true/false with a given probability).
/// Useful for coin flips, A/B testing, and probabilistic decisions.
/// </summary>
public sealed class BernoulliDistribution
{
    private readonly IRandomProvider _provider;
    private readonly double _probability;

    /// <param name="provider">Random provider to use.</param>
    /// <param name="probability">Probability of success (true). Must be in [0, 1].</param>
    public BernoulliDistribution(IRandomProvider provider, double probability = 0.5)
    {
        if (probability < 0.0 || probability > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(probability), "Probability must be between 0 and 1.");
        }

        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _probability = probability;
    }

    /// <summary>
    /// Returns true with the configured probability.
    /// </summary>
    public bool Next() => _provider.NextDouble() < _probability;

    /// <summary>
    /// Returns 1 with the configured probability, 0 otherwise.
    /// </summary>
    public int NextInt() => Next() ? 1 : 0;
}
