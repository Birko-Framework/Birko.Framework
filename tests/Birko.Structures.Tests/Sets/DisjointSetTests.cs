using Birko.Structures.Sets;
using FluentAssertions;
using Xunit;

namespace Birko.Structures.Tests.Sets;

public class DisjointSetTests
{
    [Fact]
    public void MakeSet_And_Find()
    {
        var ds = new DisjointSet<string>();
        ds.MakeSet("A");
        ds.MakeSet("B");
        ds.Find("A").Should().Be("A");
        ds.SetCount.Should().Be(2);
    }

    [Fact]
    public void Union_MergesSets()
    {
        var ds = new DisjointSet<string>();
        ds.MakeSet("A"); ds.MakeSet("B"); ds.MakeSet("C");
        ds.Union("A", "B").Should().BeTrue();
        ds.Connected("A", "B").Should().BeTrue();
        ds.Connected("A", "C").Should().BeFalse();
        ds.SetCount.Should().Be(2);
    }

    [Fact]
    public void Union_SameSet_ReturnsFalse()
    {
        var ds = new DisjointSet<int>();
        ds.MakeSet(1); ds.MakeSet(2);
        ds.Union(1, 2);
        ds.Union(1, 2).Should().BeFalse();
    }

    [Fact]
    public void GetAllSets_ReturnsGroups()
    {
        var ds = new DisjointSet<int>();
        ds.MakeSet(1); ds.MakeSet(2); ds.MakeSet(3);
        ds.Union(1, 2);
        var sets = ds.GetAllSets();
        sets.Should().HaveCount(2);
    }
}
