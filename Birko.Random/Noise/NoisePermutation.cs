namespace Birko.Random;

/// <summary>
/// Builds the 512-entry permutation table both noise generators are seeded from.
/// </summary>
/// <remarks>
/// <para>
/// TASK-449. <see cref="PerlinNoise"/> and <see cref="SimplexNoise"/> each built this table from
/// <c>new System.Random(seed)</c>. <c>System.Random</c>'s algorithm <b>changed in .NET 6</b> — the
/// subtractive generator was replaced with xoshiro256** — and .NET does not guarantee that a seeded
/// sequence is stable across versions. So the same seed produced a different permutation table, and
/// therefore different noise, on a different runtime: silently, with the stored seed still looking
/// like it was doing its job.
/// </para>
/// <para>
/// <see cref="SplitMixProvider"/> replaces it. Its state transition is pure 64-bit integer
/// arithmetic defined in this repository, so it cannot drift with the runtime, and its own summary
/// already describes it as the generator "commonly used for seeding other PRNGs".
/// <c>NextInt(maxValue)</c> derives its value through <c>NextDouble()</c>, which is
/// <c>(NextUInt64() &gt;&gt; 11) * (1.0 / (1UL &lt;&lt; 53))</c> — an integer shift multiplied by an
/// exactly-representable power-of-two reciprocal, so IEEE 754 makes it bit-identical everywhere too.
/// </para>
/// <para>
/// The rule lives here rather than in both generators because the defect <i>was</i> the same wrong
/// choice made twice: with one producer, a future edit cannot change how one of them is seeded and
/// leave the other behind.
/// </para>
/// </remarks>
internal static class NoisePermutation
{
    /// <summary>The number of distinct entries; the returned table repeats them once.</summary>
    internal const int Size = 256;

    /// <summary>
    /// Returns a 512-entry table: a seed-determined shuffle of <c>0..255</c>, repeated so callers can
    /// index it with an unmasked sum of two bytes.
    /// </summary>
    /// <param name="seed">
    /// Identical seeds produce identical tables on every runtime and platform, which is the whole
    /// point of this type. A negative seed is reinterpreted as its unsigned bit pattern.
    /// </param>
    internal static int[] Build(int seed)
    {
        var source = new int[Size];
        for (int i = 0; i < Size; i++)
        {
            source[i] = i;
        }

        // unchecked, so a negative seed maps to its bit pattern rather than throwing under a
        // CheckForOverflowUnderflow build.
        var rng = new SplitMixProvider(unchecked((ulong)seed));

        // Fisher-Yates, unchanged from what both generators did — only the source of randomness moved.
        for (int i = Size - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (source[i], source[j]) = (source[j], source[i]);
        }

        var permutation = new int[Size * 2];
        for (int i = 0; i < Size * 2; i++)
        {
            permutation[i] = source[i & (Size - 1)];
        }

        return permutation;
    }
}
