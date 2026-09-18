using Birko.Structures.Heaps;
using FluentAssertions;
using Xunit;

namespace Birko.Structures.Tests.Heaps;

public class HeapTests
{
    [Fact]
    public void MinHeap_PopsInAscendingOrder()
    {
        var heap = new MinHeap<int>();
        heap.Push(5); heap.Push(1); heap.Push(3); heap.Push(2); heap.Push(4);

        var sorted = new List<int>();
        while (!heap.IsEmpty) sorted.Add(heap.Pop());

        sorted.Should().BeInAscendingOrder();
    }

    [Fact]
    public void MaxHeap_PopsInDescendingOrder()
    {
        var heap = new MaxHeap<int>();
        heap.Push(5); heap.Push(1); heap.Push(3); heap.Push(2); heap.Push(4);

        var sorted = new List<int>();
        while (!heap.IsEmpty) sorted.Add(heap.Pop());

        sorted.Should().BeInDescendingOrder();
    }

    [Fact]
    public void Heapify_BuildsFromCollection()
    {
        var heap = new MinHeap<int>(new[] { 5, 1, 3, 2, 4 });
        heap.Count.Should().Be(5);
        heap.Peek().Should().Be(1);
    }

    [Fact]
    public void Pop_OnEmpty_Throws()
    {
        var heap = new MinHeap<int>();
        var act = () => heap.Pop();
        act.Should().Throw<InvalidOperationException>();
    }
}
