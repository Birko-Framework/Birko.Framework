using System;
using System.IO;
using System.Linq;
using Birko.Random;
using FluentAssertions;
using Xunit;

namespace Birko.Random.Tests.Noise;

/// <summary>
/// TASK-449 — a seeded noise generator must produce the same output on every runtime, not merely the
/// same output twice in one process.
/// </summary>
/// <remarks>
/// <para>
/// Both generators shuffled their permutation table with <c>new System.Random(seed)</c>.
/// <c>System.Random</c>'s algorithm changed in .NET 6 (the subtractive generator was replaced with
/// xoshiro256**) and .NET guarantees no cross-version stability for a seeded sequence — so the same
/// seed produced a different table, and therefore different noise, on a different runtime. Silently:
/// the stored seed still looked like it was doing its job.
/// </para>
/// <para>
/// ⚠ <b>Why the 14 existing noise tests could not catch it.</b> They assert ranges, continuity,
/// zero-at-integer-coordinates, and that two instances built <i>in the same process</i> agree
/// (<c>SameSeed_ProducesSameValues</c>). Every one of those passes against the defect, because within
/// one process <c>System.Random</c> is perfectly deterministic. The thing that was never asserted is a
/// <b>value</b> — which is the only assertion that can fail when the runtime changes underneath.
/// CLAUDE.md § TASK-284: when a defect survives a well-covered area, look at what the coverage
/// actually asserts.
/// </para>
/// <para>
/// These vectors were generated from the fixed implementation and hard-coded. If a change to the
/// seeding or the shuffle alters them, these tests fail — that is the point, and such a change is a
/// deliberate break of the reproducibility promise, not a rebaseline to wave through.
/// </para>
/// </remarks>
public class NoiseReproducibilityTests
{
    /// <summary>The sample coordinates every vector below was taken at.</summary>
    private static double At(int i) => 0.5 + (i * 1.37);

    public static TheoryData<int, double[]> PerlinVectors() => new()
    {
        { 0, new[] { -0.20787115560880387, -0.120015776543433, -0.2838508203497653, -0.5276969801197741, 0.12487631383177544 } },
        { 42, new[] { 0.2105226321947487, -0.1186443063521827, -0.15439992468963787, 0.14573785468283726, 0.5023248338967611 } },
        { -7, new[] { 0.31131707131180963, -0.46717330894765297, 0.41543905685201565, 0.18635429098239392, 0.27967278327400347 } },
    };

    public static TheoryData<int, double[]> SimplexVectors() => new()
    {
        { 0, new[] { 0.3294320431131513, 0.46421266908513203, 0.11803186614927993, -0.6219594097399288, 0.19502834507337502 } },
        { 42, new[] { -0.41931436643266246, 0.6559919895109612, -0.5561049705963428, -0.1683615735726373, -0.7867237186648156 } },
        { -7, new[] { -0.018248627036695896, -0.31775513078978007, -0.7664282149076584, 0.1651496489751449, -0.1939786107030969 } },
    };

    [Theory]
    [MemberData(nameof(PerlinVectors))]
    public void Perlin_matches_its_pinned_vector_exactly(int seed, double[] expected)
    {
        var noise = new PerlinNoise(seed);

        for (int i = 0; i < expected.Length; i++)
        {
            double t = At(i);
            noise.Noise(t, t * 0.31, t * 0.77).Should().Be(expected[i],
                "sample {0} of seed {1} must be bit-identical on every runtime — a seeded generator "
                + "whose output moves with the .NET version is not seeded in any useful sense", i, seed);
        }
    }

    [Theory]
    [MemberData(nameof(SimplexVectors))]
    public void Simplex_matches_its_pinned_vector_exactly(int seed, double[] expected)
    {
        var noise = new SimplexNoise(seed);

        for (int i = 0; i < expected.Length; i++)
        {
            double t = At(i);
            noise.Noise(t, t * 0.31).Should().Be(expected[i],
                "sample {0} of seed {1} must be bit-identical on every runtime", i, seed);
        }
    }

    [Fact]
    public void A_NEGATIVE_seed_is_accepted_and_reproducible()
    {
        // The seed is `int` and the provider takes `ulong`, so the conversion has to be unchecked.
        // Covered by the -7 vectors above; this asserts the construction itself cannot throw under a
        // CheckForOverflowUnderflow build.
        var act = () => new PerlinNoise(int.MinValue).Noise(0.5, 0.5);

        act.Should().NotThrow();
        new SimplexNoise(int.MinValue).Noise(0.5, 0.5)
            .Should().Be(new SimplexNoise(int.MinValue).Noise(0.5, 0.5));
    }

    [Fact]
    public void The_default_constructor_is_seed_zero_and_therefore_also_reproducible()
    {
        // `new PerlinNoise()` chains to `this(0)`, so it is pinned by the seed-0 vector rather than
        // being an unseeded generator. Worth asserting: a parameterless ctor on a noise type is the
        // one a caller reaches for without thinking about determinism.
        double t = At(0);

        new PerlinNoise().Noise(t, t * 0.31, t * 0.77).Should().Be(-0.20787115560880387,
            "it must equal the seed-0 vector's first sample");
        new SimplexNoise().Noise(t, t * 0.31).Should().Be(0.3294320431131513);
    }

    /// <summary>
    /// Structural backstop for the behavioural vectors above: no noise source may reach for
    /// <c>System.Random</c> again.
    /// </summary>
    /// <remarks>
    /// ⚠ Comment lines are stripped before scanning, deliberately. All three noise sources <i>discuss</i>
    /// <c>System.Random</c> in their doc comments — that is how the defect stays explained — so a naive
    /// text search would match the explanation and the guard would fail for the wrong reason. The same
    /// trap CLAUDE.md § TASK-276 records, arriving from the other side.
    /// </remarks>
    [Fact]
    public void No_noise_source_uses_System_Random_in_CODE()
    {
        var noiseDir = NoiseSourceDirectory();
        var files = Directory.GetFiles(noiseDir, "*.cs", SearchOption.AllDirectories);

        files.Should().HaveCountGreaterThanOrEqualTo(3,
            "PerlinNoise, SimplexNoise and NoisePermutation must all be found, or this scan is looking "
            + "in the wrong place and would pass vacuously");

        var offenders = files
            .Select(f => new { Name = Path.GetFileName(f), Code = StripComments(File.ReadAllText(f)) })
            .Where(x => x.Code.Contains("System.Random", StringComparison.Ordinal)
                     || x.Code.Contains("new Random(", StringComparison.Ordinal))
            .Select(x => x.Name)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty(
            "System.Random's seeded sequence is not stable across .NET versions, so a noise generator "
            + "built from it silently produces different output after a runtime upgrade");
    }

    /// <summary>Removes <c>//</c> line comments so the scan sees code, not prose.</summary>
    private static string StripComments(string source) =>
        string.Join('\n', source
            .Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal)
                        && !line.StartsWith("*", StringComparison.Ordinal)
                        && !line.StartsWith("/*", StringComparison.Ordinal)));

    private static string NoiseSourceDirectory()
    {
        // .../Framework.Tests/Birko.Random.Tests/bin/Debug/net10.0 -> the sibling Framework checkout.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            foreach (var candidate in new[] { dir.FullName, Path.Combine(dir.FullName, "Framework") })
            {
                var noise = Path.Combine(candidate, "Birko.Random", "Noise");
                if (Directory.Exists(noise))
                {
                    return noise;
                }
            }
        }

        throw new DirectoryNotFoundException(
            "Birko.Random/Noise was not findable from the test binary; this scan is the only structural "
            + "guard there is, so do not weaken it into a silent skip.");
    }
}
