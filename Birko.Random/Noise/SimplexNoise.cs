using System;

namespace Birko.Random;

/// <summary>
/// Simplex noise generator — improved alternative to Perlin noise with better
/// visual isotropy, fewer directional artifacts, and O(n) complexity in n dimensions.
/// Supports 2D and 3D noise.
/// </summary>
public sealed class SimplexNoise
{
    private static readonly int[][] Grad3 =
    [
        [1, 1, 0], [-1, 1, 0], [1, -1, 0], [-1, -1, 0],
        [1, 0, 1], [-1, 0, 1], [1, 0, -1], [-1, 0, -1],
        [0, 1, 1], [0, -1, 1], [0, 1, -1], [0, -1, -1]
    ];

    private readonly int[] _perm;

    public SimplexNoise() : this(0)
    {
    }

    /// <param name="seed">
    /// The same seed produces the same noise on every runtime and platform. TASK-449: that was not
    /// true before -- the table was shuffled with <c>System.Random</c>, whose algorithm changed in
    /// .NET 6 and carries no cross-version stability guarantee -- so a stored seed silently produced
    /// different output after a framework upgrade. See <see cref="NoisePermutation"/>.
    /// </param>
    public SimplexNoise(int seed)
    {
        _perm = NoisePermutation.Build(seed);
    }

    /// <summary>
    /// Returns 2D simplex noise value in approximately [-1, 1].
    /// </summary>
    public double Noise(double x, double y)
    {
        const double F2 = 0.5 * (1.7320508075688772 - 1.0); // (sqrt(3)-1)/2
        const double G2 = (3.0 - 1.7320508075688772) / 6.0; // (3-sqrt(3))/6

        double s = (x + y) * F2;
        int i = FastFloor(x + s);
        int j = FastFloor(y + s);

        double t = (i + j) * G2;
        double x0 = x - (i - t);
        double y0 = y - (j - t);

        int i1, j1;
        if (x0 > y0) { i1 = 1; j1 = 0; }
        else { i1 = 0; j1 = 1; }

        double x1 = x0 - i1 + G2;
        double y1 = y0 - j1 + G2;
        double x2 = x0 - 1.0 + 2.0 * G2;
        double y2 = y0 - 1.0 + 2.0 * G2;

        int ii = i & 255;
        int jj = j & 255;

        double n0 = Contribution2D(x0, y0, ii, jj);
        double n1 = Contribution2D(x1, y1, ii + i1, jj + j1);
        double n2 = Contribution2D(x2, y2, ii + 1, jj + 1);

        return 70.0 * (n0 + n1 + n2);
    }

    /// <summary>
    /// Returns 3D simplex noise value in approximately [-1, 1].
    /// </summary>
    public double Noise(double x, double y, double z)
    {
        const double F3 = 1.0 / 3.0;
        const double G3 = 1.0 / 6.0;

        double s = (x + y + z) * F3;
        int i = FastFloor(x + s);
        int j = FastFloor(y + s);
        int k = FastFloor(z + s);

        double t = (i + j + k) * G3;
        double x0 = x - (i - t);
        double y0 = y - (j - t);
        double z0 = z - (k - t);

        int i1, j1, k1, i2, j2, k2;

        if (x0 >= y0)
        {
            if (y0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
            else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
            else { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
        }
        else
        {
            if (y0 < z0) { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
            else if (x0 < z0) { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
            else { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
        }

        double x1 = x0 - i1 + G3;
        double y1 = y0 - j1 + G3;
        double z1 = z0 - k1 + G3;
        double x2 = x0 - i2 + 2.0 * G3;
        double y2 = y0 - j2 + 2.0 * G3;
        double z2 = z0 - k2 + 2.0 * G3;
        double x3 = x0 - 1.0 + 3.0 * G3;
        double y3 = y0 - 1.0 + 3.0 * G3;
        double z3 = z0 - 1.0 + 3.0 * G3;

        int ii = i & 255;
        int jj = j & 255;
        int kk = k & 255;

        double n0 = Contribution3D(x0, y0, z0, ii, jj, kk);
        double n1 = Contribution3D(x1, y1, z1, ii + i1, jj + j1, kk + k1);
        double n2 = Contribution3D(x2, y2, z2, ii + i2, jj + j2, kk + k2);
        double n3 = Contribution3D(x3, y3, z3, ii + 1, jj + 1, kk + 1);

        return 32.0 * (n0 + n1 + n2 + n3);
    }

    /// <summary>
    /// Returns fractal Brownian motion (fBm) noise using simplex noise.
    /// </summary>
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

    private double Contribution2D(double x, double y, int gi, int gj)
    {
        double t = 0.5 - x * x - y * y;
        if (t < 0)
        {
            return 0.0;
        }

        int gi2 = _perm[(gi + _perm[gj & 255]) & 255] % 12;
        t *= t;
        return t * t * Dot2D(Grad3[gi2], x, y);
    }

    private double Contribution3D(double x, double y, double z, int gi, int gj, int gk)
    {
        double t = 0.6 - x * x - y * y - z * z;
        if (t < 0)
        {
            return 0.0;
        }

        int gi2 = _perm[(gi + _perm[(gj + _perm[gk & 255]) & 255]) & 255] % 12;
        t *= t;
        return t * t * Dot3D(Grad3[gi2], x, y, z);
    }

    private static int FastFloor(double x)
    {
        int xi = (int)x;
        return x < xi ? xi - 1 : xi;
    }

    private static double Dot2D(int[] g, double x, double y) => g[0] * x + g[1] * y;

    private static double Dot3D(int[] g, double x, double y, double z) => g[0] * x + g[1] * y + g[2] * z;
}
