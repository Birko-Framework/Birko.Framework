using System;
using System.Collections;
using System.Collections.Generic;

namespace Birko.Structures.Lists;

/// <summary>
/// Probabilistic sorted list with O(log n) average search, insert, and delete.
/// </summary>
/// <typeparam name="T">A comparable element type.</typeparam>
public class SkipList<T> : IEnumerable<T> where T : IComparable<T>
{
    private sealed class SkipNode
    {
        public T Value;
        public SkipNode[] Forward;

        public SkipNode(T value, int level)
        {
            Value = value;
            Forward = new SkipNode[level + 1];
        }
    }

    private const int MaxLevel = 32;
    private const double Probability = 0.5;

    private readonly SkipNode _head;
    private readonly Func<double> _nextDouble;
    private int _level;

    /// <summary>
    /// Gets the number of elements.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Creates an empty skip list using <see cref="System.Random"/> for level generation.
    /// </summary>
    public SkipList() : this(new System.Random().NextDouble)
    {
    }

    /// <summary>
    /// Creates an empty skip list with a custom random source.
    /// Pass any <c>() =&gt; double</c> that returns values in [0.0, 1.0),
    /// e.g. <c>new SkipList&lt;T&gt;(myRandomProvider.NextDouble)</c>.
    /// </summary>
    public SkipList(Func<double> nextDouble)
    {
        _nextDouble = nextDouble ?? throw new ArgumentNullException(nameof(nextDouble));
        _head = new SkipNode(default!, MaxLevel);
        _level = 0;
    }

    /// <summary>
    /// Inserts an element. Returns true if inserted (no duplicates).
    /// </summary>
    public bool Insert(T value)
    {
        var update = new SkipNode[MaxLevel + 1];
        var current = _head;

        for (int i = _level; i >= 0; i--)
        {
            while (current.Forward[i] != null && current.Forward[i].Value.CompareTo(value) < 0)
            {
                current = current.Forward[i];
            }
            update[i] = current;
        }

        current = current.Forward[0];

        if (current != null && current.Value.CompareTo(value) == 0)
        {
            return false; // Duplicate
        }

        int newLevel = RandomLevel();
        if (newLevel > _level)
        {
            for (int i = _level + 1; i <= newLevel; i++)
            {
                update[i] = _head;
            }
            _level = newLevel;
        }

        var newNode = new SkipNode(value, newLevel);
        for (int i = 0; i <= newLevel; i++)
        {
            newNode.Forward[i] = update[i].Forward[i];
            update[i].Forward[i] = newNode;
        }

        Count++;
        return true;
    }

    /// <summary>
    /// Searches for an element. Returns true if found.
    /// </summary>
    public bool Search(T value)
    {
        var current = _head;

        for (int i = _level; i >= 0; i--)
        {
            while (current.Forward[i] != null && current.Forward[i].Value.CompareTo(value) < 0)
            {
                current = current.Forward[i];
            }
        }

        current = current.Forward[0];
        return current != null && current.Value.CompareTo(value) == 0;
    }

    /// <summary>
    /// Removes an element. Returns true if found and removed.
    /// </summary>
    public bool Remove(T value)
    {
        var update = new SkipNode[MaxLevel + 1];
        var current = _head;

        for (int i = _level; i >= 0; i--)
        {
            while (current.Forward[i] != null && current.Forward[i].Value.CompareTo(value) < 0)
            {
                current = current.Forward[i];
            }
            update[i] = current;
        }

        current = current.Forward[0];

        if (current == null || current.Value.CompareTo(value) != 0)
        {
            return false;
        }

        for (int i = 0; i <= _level; i++)
        {
            if (update[i].Forward[i] != current) break;
            update[i].Forward[i] = current.Forward[i];
        }

        while (_level > 0 && _head.Forward[_level] == null)
        {
            _level--;
        }

        Count--;
        return true;
    }

    /// <summary>
    /// Gets the minimum element.
    /// </summary>
    public T Min()
    {
        if (Count == 0) throw new InvalidOperationException("Skip list is empty.");
        return _head.Forward[0].Value;
    }

    /// <summary>
    /// Gets the maximum element.
    /// </summary>
    public T Max()
    {
        if (Count == 0) throw new InvalidOperationException("Skip list is empty.");

        var current = _head;
        for (int i = _level; i >= 0; i--)
        {
            while (current.Forward[i] != null)
            {
                current = current.Forward[i];
            }
        }
        return current.Value;
    }

    /// <summary>
    /// Gets elements in a range [low, high] inclusive.
    /// </summary>
    public IReadOnlyList<T> Range(T low, T high)
    {
        var results = new List<T>();
        var current = _head;

        for (int i = _level; i >= 0; i--)
        {
            while (current.Forward[i] != null && current.Forward[i].Value.CompareTo(low) < 0)
            {
                current = current.Forward[i];
            }
        }

        current = current.Forward[0];

        while (current != null && current.Value.CompareTo(high) <= 0)
        {
            results.Add(current.Value);
            current = current.Forward[0];
        }

        return results;
    }

    /// <summary>
    /// Enumerates elements in sorted order.
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        var current = _head.Forward[0];
        while (current != null)
        {
            yield return current.Value;
            current = current.Forward[0];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private int RandomLevel()
    {
        int lvl = 0;
        while (_nextDouble() < Probability && lvl < MaxLevel)
        {
            lvl++;
        }
        return lvl;
    }
}
