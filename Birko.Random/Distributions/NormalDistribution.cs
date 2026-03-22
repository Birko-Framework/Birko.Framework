using System;

namespace Birko.Random;

/// <summary>
/// Generates normally (Gaussian) distributed random values using the Box-Muller transform.
/// </summary>
public sealed class NormalDistribution
{
    private readonly IRandomProvider _provider;
    private readonly double _mean;
    private readonly double _stdDev;
    private double? _spare;

    public NormalDistribution(IRandomProvider provider, double mean = 0.0, double stdDev = 1.0)
    {
        if (stdDev <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stdDev), "Standard deviation must be positive.");
        }

        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _mean = mean;
        _stdDev = stdDev;
    }

    /// <summary>
    /// Returns the next normally distributed value.
    /// </summary>
    public double Next()
    {
        if (_spare.HasValue)
        {
            double value = _spare.Value;
            _spare = null;
            return value * _stdDev + _mean;
        }

        double u, v, s;
        do
        {
            u = _provider.NextDouble() * 2.0 - 1.0;
            v = _provider.NextDouble() * 2.0 - 1.0;
            s = u * u + v * v;
        } while (s >= 1.0 || s == 0.0);

        double factor = Math.Sqrt(-2.0 * Math.Log(s) / s);
        _spare = v * factor;
        return u * factor * _stdDev + _mean;
    }
}
