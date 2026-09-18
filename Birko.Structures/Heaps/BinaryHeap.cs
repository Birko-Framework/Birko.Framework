using System;
using System.Collections;
using System.Collections.Generic;

namespace Birko.Structures.Heaps;

/// <summary>
/// Array-backed binary heap with configurable comparison.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public class BinaryHeap<T> : IEnumerable<T>
{
    private readonly List<T> _items = new();
    private readonly IComparer<T> _comparer;

    /// <summary>
    /// Gets the number of elements in the heap.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Gets whether the heap is empty.
    /// </summary>
    public bool IsEmpty => _items.Count == 0;

    /// <summary>
    /// Creates a binary heap with the specified comparer.
    /// </summary>
    public BinaryHeap(IComparer<T> comparer)
    {
        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
    }

    /// <summary>
    /// Creates a binary heap with the default comparer.
    /// </summary>
    public BinaryHeap() : this(Comparer<T>.Default)
    {
    }

    /// <summary>
    /// Creates a binary heap from an existing collection (heapify).
    /// </summary>
    public BinaryHeap(IEnumerable<T> items, IComparer<T>? comparer = null)
    {
        _comparer = comparer ?? Comparer<T>.Default;
        _items.AddRange(items);
        Heapify();
    }

    /// <summary>
    /// Pushes an element onto the heap.
    /// </summary>
    public void Push(T item)
    {
        _items.Add(item);
        SiftUp(_items.Count - 1);
    }

    /// <summary>
    /// Returns the top element without removing it.
    /// </summary>
    public T Peek()
    {
        if (_items.Count == 0) throw new InvalidOperationException("Heap is empty.");
        return _items[0];
    }

    /// <summary>
    /// Removes and returns the top element.
    /// </summary>
    public T Pop()
    {
        if (_items.Count == 0) throw new InvalidOperationException("Heap is empty.");

        var top = _items[0];
        var last = _items.Count - 1;
        _items[0] = _items[last];
        _items.RemoveAt(last);

        if (_items.Count > 0)
        {
            SiftDown(0);
        }

        return top;
    }

    /// <summary>
    /// Removes and returns the top element, then pushes a new element.
    /// More efficient than Pop + Push.
    /// </summary>
    public T Replace(T item)
    {
        if (_items.Count == 0) throw new InvalidOperationException("Heap is empty.");

        var top = _items[0];
        _items[0] = item;
        SiftDown(0);
        return top;
    }

    /// <summary>
    /// Removes all elements.
    /// </summary>
    public void Clear() => _items.Clear();

    /// <summary>
    /// Checks if the heap contains an element.
    /// </summary>
    public bool Contains(T item) => _items.Contains(item);

    /// <summary>
    /// Copies elements to an array (heap order, not sorted).
    /// </summary>
    public T[] ToArray() => _items.ToArray();

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Heapify()
    {
        for (int i = _items.Count / 2 - 1; i >= 0; i--)
        {
            SiftDown(i);
        }
    }

    private void SiftUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (_comparer.Compare(_items[index], _items[parent]) >= 0) break;

            (_items[index], _items[parent]) = (_items[parent], _items[index]);
            index = parent;
        }
    }

    private void SiftDown(int index)
    {
        int count = _items.Count;
        while (true)
        {
            int smallest = index;
            int left = 2 * index + 1;
            int right = 2 * index + 2;

            if (left < count && _comparer.Compare(_items[left], _items[smallest]) < 0)
            {
                smallest = left;
            }
            if (right < count && _comparer.Compare(_items[right], _items[smallest]) < 0)
            {
                smallest = right;
            }

            if (smallest == index) break;

            (_items[index], _items[smallest]) = (_items[smallest], _items[index]);
            index = smallest;
        }
    }
}

/// <summary>
/// Min-heap: smallest element is at the top.
/// </summary>
public class MinHeap<T> : BinaryHeap<T> where T : IComparable<T>
{
    public MinHeap() : base(Comparer<T>.Default) { }
    public MinHeap(IEnumerable<T> items) : base(items, Comparer<T>.Default) { }
}

/// <summary>
/// Max-heap: largest element is at the top.
/// </summary>
public class MaxHeap<T> : BinaryHeap<T> where T : IComparable<T>
{
    private sealed class ReverseComparer : IComparer<T>
    {
        public int Compare(T? x, T? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return 1;
            if (y == null) return -1;
            return y.CompareTo(x);
        }
    }

    public MaxHeap() : base(new ReverseComparer()) { }
    public MaxHeap(IEnumerable<T> items) : base(items, new ReverseComparer()) { }
}
