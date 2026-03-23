using Birko.Structures.Trees;
using FluentAssertions;
using Xunit;

namespace Birko.Structures.Tests.Trees;

public class IntervalTreeTests
{
    [Fact]
    public void Insert_And_QueryOverlapping()
    {
        var tree = new IntervalTree<int>();
        tree.Insert(1, 5);
        tree.Insert(3, 8);
        tree.Insert(10, 15);

        var results = tree.QueryOverlapping(4, 6);
        results.Should().HaveCount(2);
    }

    [Fact]
    public void QueryPoint_FindsContainingIntervals()
    {
        var tree = new IntervalTree<int>();
        tree.Insert(1, 10);
        tree.Insert(5, 15);
        tree.Insert(20, 30);

        var results = tree.QueryPoint(7);
        results.Should().HaveCount(2);
    }

    [Fact]
    public void AnyOverlapping_ReturnsFalseForNoOverlap()
    {
        var tree = new IntervalTree<int>();
        tree.Insert(1, 5);
        tree.Insert(10, 15);

        tree.AnyOverlapping(new Interval<int>(6, 9)).Should().BeFalse();
    }

    [Fact]
    public void Count_TracksInsertions()
    {
        var tree = new IntervalTree<int>();
        tree.Insert(1, 5);
        tree.Insert(3, 8);
        tree.Count.Should().Be(2);
    }

    [Fact]
    public void Clear_ResetsTree()
    {
        var tree = new IntervalTree<int>();
        tree.Insert(1, 5);
        tree.Clear();
        tree.Count.Should().Be(0);
        tree.GetAll().Should().BeEmpty();
    }
}
