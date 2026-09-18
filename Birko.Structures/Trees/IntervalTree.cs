using System;
using System.Collections.Generic;

namespace Birko.Structures.Trees;

/// <summary>
/// Represents a closed interval [Low, High].
/// </summary>
/// <typeparam name="T">A comparable type for interval bounds.</typeparam>
public readonly record struct Interval<T>(T Low, T High) where T : IComparable<T>
{
    /// <summary>
    /// Checks if this interval overlaps with another.
    /// </summary>
    public bool Overlaps(Interval<T> other)
    {
        return Low.CompareTo(other.High) <= 0 && other.Low.CompareTo(High) <= 0;
    }

    /// <summary>
    /// Checks if this interval contains a point.
    /// </summary>
    public bool Contains(T point)
    {
        return Low.CompareTo(point) <= 0 && point.CompareTo(High) <= 0;
    }
}

/// <summary>
/// Augmented BST for efficient interval overlap queries.
/// Each node stores an interval and the maximum high value in its subtree.
/// </summary>
/// <typeparam name="T">A comparable type for interval bounds.</typeparam>
public class IntervalTree<T> where T : IComparable<T>
{
    private sealed class Node
    {
        public Interval<T> Interval;
        public T MaxHigh;
        public Node? Left;
        public Node? Right;

        public Node(Interval<T> interval)
        {
            Interval = interval;
            MaxHigh = interval.High;
        }
    }

    private Node? _root;

    /// <summary>
    /// Gets the number of intervals stored.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Inserts an interval.
    /// </summary>
    public void Insert(Interval<T> interval)
    {
        _root = Insert(_root, interval);
        Count++;
    }

    /// <summary>
    /// Inserts an interval specified by low and high bounds.
    /// </summary>
    public void Insert(T low, T high) => Insert(new Interval<T>(low, high));

    /// <summary>
    /// Finds all intervals that overlap with the query interval.
    /// </summary>
    public IReadOnlyList<Interval<T>> QueryOverlapping(Interval<T> query)
    {
        var results = new List<Interval<T>>();
        QueryOverlapping(_root, query, results);
        return results;
    }

    /// <summary>
    /// Finds all intervals that overlap with [low, high].
    /// </summary>
    public IReadOnlyList<Interval<T>> QueryOverlapping(T low, T high)
        => QueryOverlapping(new Interval<T>(low, high));

    /// <summary>
    /// Finds all intervals that contain a specific point.
    /// </summary>
    public IReadOnlyList<Interval<T>> QueryPoint(T point)
        => QueryOverlapping(new Interval<T>(point, point));

    /// <summary>
    /// Checks if any stored interval overlaps with the query interval.
    /// </summary>
    public bool AnyOverlapping(Interval<T> query)
    {
        return AnyOverlapping(_root, query);
    }

    /// <summary>
    /// Gets all intervals in sorted order (by low bound).
    /// </summary>
    public IReadOnlyList<Interval<T>> GetAll()
    {
        var results = new List<Interval<T>>();
        InOrder(_root, results);
        return results;
    }

    /// <summary>
    /// Removes all intervals.
    /// </summary>
    public void Clear()
    {
        _root = null;
        Count = 0;
    }

    private static Node Insert(Node? node, Interval<T> interval)
    {
        if (node == null) return new Node(interval);

        if (interval.Low.CompareTo(node.Interval.Low) <= 0)
        {
            node.Left = Insert(node.Left, interval);
        }
        else
        {
            node.Right = Insert(node.Right, interval);
        }

        if (interval.High.CompareTo(node.MaxHigh) > 0)
        {
            node.MaxHigh = interval.High;
        }

        return node;
    }

    private static void QueryOverlapping(Node? node, Interval<T> query, List<Interval<T>> results)
    {
        if (node == null) return;

        if (node.Interval.Overlaps(query))
        {
            results.Add(node.Interval);
        }

        if (node.Left != null && node.Left.MaxHigh.CompareTo(query.Low) >= 0)
        {
            QueryOverlapping(node.Left, query, results);
        }

        if (node.Right != null && node.Interval.Low.CompareTo(query.High) <= 0)
        {
            QueryOverlapping(node.Right, query, results);
        }
    }

    private static bool AnyOverlapping(Node? node, Interval<T> query)
    {
        if (node == null) return false;

        if (node.Interval.Overlaps(query)) return true;

        if (node.Left != null && node.Left.MaxHigh.CompareTo(query.Low) >= 0)
        {
            if (AnyOverlapping(node.Left, query)) return true;
        }

        if (node.Right != null && node.Interval.Low.CompareTo(query.High) <= 0)
        {
            if (AnyOverlapping(node.Right, query)) return true;
        }

        return false;
    }

    private static void InOrder(Node? node, List<Interval<T>> results)
    {
        if (node == null) return;
        InOrder(node.Left, results);
        results.Add(node.Interval);
        InOrder(node.Right, results);
    }
}
