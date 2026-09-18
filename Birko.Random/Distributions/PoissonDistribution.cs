using System;

namespace Birko.Random;

/// <summary>
/// Generates Poisson-distributed random integers. Models the number of events
/// occurring in a fixed interval (e.g., request counts, error rates).
/// Uses Knuth's algorithm for small lambda, rejection method for large lambda.
/// </summary>
public sealed class PoissonDistribution
{
    private readonly IRandomProvider _provider;
    private readonly double _lambda;

    /// <param name="provider">Random provider to use.</param>
    /// <param name="lambda">Expected number of events. Must be positive.</param>
    public PoissonDistribution(IRandomProvider provider, double lambda)
    {
        if (lambda <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lambda), "Lambda must be positive.");
        }

        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _lambda = lambda;
    }

    /// <summary>
    /// Returns the next Poisson-distributed integer.
    /// </summary>
    public int Next()
    {
        if (_lambda < 30)
        {
            return KnuthAlgorithm();
        }

        return RejectionMethod();
    }

    private int KnuthAlgorithm()
    {
        double L = Math.Exp(-_lambda);
        int k = 0;
        double p = 1.0;

        do
        {
            k++;
            p *= _provider.NextDouble();
        } while (p > L);

        return k - 1;
    }

    private int RejectionMethod()
    {
        // Transformed rejection method (PA) for large lambda
        double c = 0.767 - 3.36 / _lambda;
        double beta = Math.PI / Math.Sqrt(3.0 * _lambda);
        double alpha = beta * _lambda;
        double k = Math.Log(c) - _lambda - Math.Log(beta);

        while (true)
        {
            double u = _provider.NextDouble();
            double x = (alpha - Math.Log((1.0 - u) / u)) / beta;

            if (x < -0.5)
            {
                continue;
            }

            int n = (int)Math.Floor(x + 0.5);

            if (n < 0)
            {
                continue;
            }

            double v = _provider.NextDouble();
            double y = alpha - beta * x;
            double lhs = y + Math.Log(v / (1.0 + Math.Exp(y)) / (1.0 + Math.Exp(y)));
            double rhs = k + n * Math.Log(_lambda) - LogFactorial(n);

            if (lhs <= rhs)
            {
                return n;
            }
        }
    }

    private static double LogFactorial(int n)
    {
        if (n <= 1)
        {
            return 0.0;
        }

        // Stirling's approximation for large n
        if (n > 20)
        {
            double x = n + 1.0;
            return (x - 0.5) * Math.Log(x) - x + 0.5 * Math.Log(2.0 * Math.PI) + 1.0 / (12.0 * x);
        }

        double result = 0.0;
        for (int i = 2; i <= n; i++)
        {
            result += Math.Log(i);
        }

        return result;
    }
}
