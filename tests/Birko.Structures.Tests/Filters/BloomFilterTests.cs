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
}
