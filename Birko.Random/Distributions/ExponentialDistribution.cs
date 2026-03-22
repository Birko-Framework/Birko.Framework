using System;

namespace Birko.Random;

/// <summary>
/// Generates exponentially distributed random values. Useful for modeling
/// time between events (e.g., retry jitter, inter-arrival times).
/// </summary>
public sealed class ExponentialDistribution
{
    private readonly IRandomProvider _provider;
    private readonly double _rate;

    /// <param name="provider">Random provider to use.</param>
    /// <param name="rate">Rate parameter (lambda). Must be positive. Mean = 1/rate.</param>
    public ExponentialDistribution(IRandomProvider provider, double rate = 1.0)
    {
        if (rate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Rate must be positive.");
        }

        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _rate = rate;
    }

    /// <summary>
    /// Returns the next exponentially distributed value.
    /// </summary>
    public double Next()
    {
        double u;
        do
        {
            u = _provider.NextDouble();
        } while (u == 0.0);

        return -Math.Log(u) / _rate;
    }
}
