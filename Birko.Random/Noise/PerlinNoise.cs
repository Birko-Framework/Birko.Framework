using System;

namespace Birko.Random;

/// <summary>
/// Perlin noise generator for smooth, natural-looking random values.
/// Useful for procedural content generation and test data with realistic variation.
/// Supports 1D, 2D, and 3D noise.
/// </summary>
public sealed class PerlinNoise
{
    private readonly int[] _permutation;

    public PerlinNoise() : this(0)
    {
    }

    /// <param name="seed">
    /// The same seed produces the same noise on every runtime and platform. TASK-449: that was not
    /// true before -- the table was shuffled with <c>System.Random</c>, whose algorithm changed in
    /// .NET 6 and carries no cross-version stability guarantee -- so a stored seed silently produced
    /// different terrain after a framework upgrade. See <see cref="NoisePermutation"/>.
    /// </param>
    public PerlinNoise(int seed)
    {
        _permutation = NoisePermutation.Build(seed);
    }

    /// <summary>
    /// Returns 1D Perlin noise value in approximately [-1, 1].
    /// </summary>
    public double Noise(double x)
    {
        return Noise(x, 0, 0);
    }

    /// <summary>
    /// Returns 2D Perlin noise value in approximately [-1, 1].
    /// </summary>
    public double Noise(double x, double y)
    {
        return Noise(x, y, 0);
    }

    /// <summary>
    /// Returns 3D Perlin noise value in approximately [-1, 1].
    /// </summary>
    public double Noise(double x, double y, double z)
    {
        int X = (int)Math.Floor(x) & 255;
        int Y = (int)Math.Floor(y) & 255;
        int Z = (int)Math.Floor(z) & 255;

        x -= Math.Floor(x);
        y -= Math.Floor(y);
        z -= Math.Floor(z);

        double u = Fade(x);
        double v = Fade(y);
        double w = Fade(z);

        int A = _permutation[X] + Y;
        int AA = _permutation[A] + Z;
        int AB = _permutation[A + 1] + Z;
        int B = _permutation[X + 1] + Y;
        int BA = _permutation[B] + Z;
        int BB = _permutation[B + 1] + Z;

        return Lerp(w,
            Lerp(v,
                Lerp(u, Grad(_permutation[AA], x, y, z),
                    Grad(_permutation[BA], x - 1, y, z)),
                Lerp(u, Grad(_permutation[AB], x, y - 1, z),
                    Grad(_permutation[BB], x - 1, y - 1, z))),
            Lerp(v,
                Lerp(u, Grad(_permutation[AA + 1], x, y, z - 1),
                    Grad(_permutation[BA + 1], x - 1, y, z - 1)),
                Lerp(u, Grad(_permutation[AB + 1], x, y - 1, z - 1),
                    Grad(_permutation[BB + 1], x - 1, y - 1, z - 1))));
    }

    /// <summary>
    /// Returns fractal Brownian motion (fBm) noise — multiple octaves of Perlin noise.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="octaves">Number of noise layers.</param>
    /// <param name="persistence">Amplitude decay per octave (0-1).</param>
    public double Fbm(double x, double y, int octaves = 6, double persistence = 0.5)
    {
        double total = 0;
        double amplitude = 1;
        double frequency = 1;
        double maxValue = 0;

        for (int i = 0; i < octaves; i++)
        {
            total += Noise(x * frequency, y * frequency) * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= 2;
        }

        return total / maxValue;
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static double Lerp(double t, double a, double b) => a + t * (b - a);

    private static double Grad(int hash, double x, double y, double z)
    {
        int h = hash & 15;
        double u = h < 8 ? x : y;
        double v = h < 4 ? y : h == 12 || h == 14 ? x : z;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }
}
