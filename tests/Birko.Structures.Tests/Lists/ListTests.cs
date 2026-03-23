using Birko.Structures.Lists;
using FluentAssertions;
using Xunit;

namespace Birko.Structures.Tests.Lists;

public class SkipListTests
{
    [Fact]
    public void Insert_And_Search()
    {
        var sl = new SkipList<int>();
        sl.Insert(3); sl.Insert(1); sl.Insert(5);
        sl.Search(3).Should().BeTrue();
        sl.Search(2).Should().BeFalse();
        sl.Count.Should().Be(3);
    }

    [Fact]
    public void Enumerates_InSortedOrder()
    {
        var sl = new SkipList<int>();
        sl.Insert(5); sl.Insert(1); sl.Insert(3); sl.Insert(2); sl.Insert(4);
        sl.ToList().Should().BeInAscendingOrder();
    }

    [Fact]
    public void Range_ReturnsSubset()
    {
        var sl = new SkipList<int>();
        sl.Insert(1); sl.Insert(3); sl.Insert(5); sl.Insert(7); sl.Insert(9);
        var range = sl.Range(3, 7);
        range.Should().Equal(3, 5, 7);
    }

    [Fact]
    public void Remove_DecreasesCount()
    {
        var sl = new SkipList<int>();
        sl.Insert(1); sl.Insert(2); sl.Insert(3);
        sl.Remove(2).Should().BeTrue();
        sl.Count.Should().Be(2);
        sl.Search(2).Should().BeFalse();
    }

    [Fact]
    public void Insert_Duplicate_ReturnsFalse()
    {
        var sl = new SkipList<int>();
        sl.Insert(1).Should().BeTrue();
        sl.Insert(1).Should().BeFalse();
    }
}

public class DequeTests
{
    [Fact]
    public void PushBack_And_PopFront()
    {
        var dq = new Deque<int>();
        dq.PushBack(1); dq.PushBack(2); dq.PushBack(3);
        dq.PopFront().Should().Be(1);
        dq.PopFront().Should().Be(2);
    }

    [Fact]
    public void PushFront_And_PopBack()
    {
        var dq = new Deque<int>();
        dq.PushFront(1); dq.PushFront(2); dq.PushFront(3);
        dq.PopBack().Should().Be(1);
        dq.PopBack().Should().Be(2);
    }

    [Fact]
    public void GrowsAutomatically()
    {
        var dq = new Deque<int>(2);
        dq.PushBack(1); dq.PushBack(2); dq.PushBack(3); dq.PushBack(4);
        dq.Count.Should().Be(4);
        dq.ToArray().Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void PopFront_OnEmpty_Throws()
    {
        var dq = new Deque<int>();
        var act = () => dq.PopFront();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Indexer_ReturnsCorrectElement()
    {
        var dq = new Deque<int>();
        dq.PushBack(10); dq.PushBack(20); dq.PushBack(30);
        dq[0].Should().Be(10);
        dq[1].Should().Be(20);
        dq[2].Should().Be(30);
    }
}
