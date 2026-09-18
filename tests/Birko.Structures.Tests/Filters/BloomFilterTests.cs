using Birko.Structures.Filters;
using FluentAssertions;
using Xunit;

namespace Birko.Structures.Tests.Filters;

public class BloomFilterTests
{
    [Fact]
    public void Add_And_MayContain()
    {
        var filter = new BloomFilter<string>(100, 0.01);
        filter.Add("hello");
        filter.Add("world");

        filter.MayContain("hello").Should().BeTrue();
        filter.MayContain("world").Should().BeTrue();
    }

    [Fact]
    public void MayContain_ReturnsFalseForAbsent()
    {
        var filter = new BloomFilter<string>(100, 0.01);
        filter.Add("hello");

        // Very unlikely to be false positive with low load
        filter.MayContain("definitely_not_here_xyz").Should().BeFalse();
    }

    [Fact]
    public void Clear_ResetsBits()
    {
        var filter = new BloomFilter<string>(100, 0.01);
        filter.Add("hello");
        filter.Clear();
        filter.Count.Should().Be(0);
        filter.MayContain("hello").Should().BeFalse();
    }

    private sealed class ZeroHash
    {
        public override int GetHashCode() => 0;
        public override bool Equals(object? obj) => obj is ZeroHash;
    }

    [Fact]
    public void Add_ItemWithZeroHashCode_HasNoFalseNegative()
    {
        // CR-L376: an item whose GetHashCode() is 0 previously collapsed both hashes to 0, so every probe
        // hit bit 0. The item must still round-trip (no false negatives) through the new mixing.
        var filter = new BloomFilter<ZeroHash>(100, 0.01);
        var item = new ZeroHash();

        filter.Add(item);

        filter.MayContain(item).Should().BeTrue();
        filter.MayContain(new ZeroHash()).Should().BeTrue(); // equal + same hash → same membership
    }

    [Fact]
    public void Add_DeterministicIntSet_NoFalseNegatives_AndLowFalsePositiveRate()
    {
        // int.GetHashCode() is the value itself, so this is deterministic. The set includes 0 (the
        // zero-hash case CR-L376 fixed). A degenerate second hash would concentrate probes and inflate
        // the measured false-positive rate; a well-mixed, non-zero hash2 keeps it near the target.
        var filter = new BloomFilter<int>(500, 0.01);

        for (int i = 0; i < 500; i++)
        {
            filter.Add(i);
        }

        for (int i = 0; i < 500; i++)
        {
            filter.MayContain(i).Should().BeTrue(); // no false negatives, ever
        }

        int falsePositives = 0;
        const int queries = 5000;
        for (int i = 1000; i < 1000 + queries; i++)
        {
            if (filter.MayContain(i)) falsePositives++;
        }

        (falsePositives / (double)queries).Should().BeLessThan(0.05); // generous bound vs configured 0.01
    }
}
