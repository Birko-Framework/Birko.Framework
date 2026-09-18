using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Structures.Trees;

/// <summary>
/// Generic tree node that stores a value and maintains parent/children relationships.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public class Node<T>
{
    /// <summary>
    /// The value stored in this node.
    /// </summary>
    public T Value { get; set; }

    /// <summary>
    /// The parent node, or null if this is the root.
    /// </summary>
    public Node<T>? Parent { get; internal set; }

    /// <summary>
    /// The child nodes. Null slots represent empty positions (used by binary trees).
    /// </summary>
    internal List<Node<T>?> ChildList { get; set; } = new();

    /// <summary>
    /// Gets the children (non-null only).
    /// </summary>
    public IEnumerable<Node<T>> Children => ChildList.Where(c => c != null)!;

    /// <summary>
    /// Gets whether this is a root node (no parent).
    /// </summary>
    public bool IsRoot => Parent == null;

    /// <summary>
    /// Gets whether this is a leaf node (no children).
    /// </summary>
    public bool IsLeaf => !ChildList.Any(c => c != null);

    /// <summary>
    /// Creates a node with the specified value.
    /// </summary>
    public Node(T value)
    {
        Value = value;
    }

    /// <summary>
    /// Adds a child node at the end.
    /// </summary>
    public Node<T> AddChild(T value)
    {
        var child = new Node<T>(value) { Parent = this };
        ChildList.Add(child);
        return child;
    }

    /// <summary>
    /// Adds an existing node as a child.
    /// </summary>
    public void AddChild(Node<T> child)
    {
        child.Parent = this;
        ChildList.Add(child);
    }

    /// <summary>
    /// Removes a child node by reference.
    /// </summary>
    public bool RemoveChild(Node<T> child)
    {
        var index = ChildList.IndexOf(child);
        if (index < 0) return false;

        child.Parent = null;
        ChildList[index] = null;
        CleanupNullTail();
        return true;
    }

    /// <summary>
    /// Finds a node by value using DFS.
    /// </summary>
    public Node<T>? Find(T value, IEqualityComparer<T>? comparer = null)
    {
        comparer ??= EqualityComparer<T>.Default;

        if (comparer.Equals(Value, value)) return this;

        foreach (var child in Children)
        {
            var found = child.Find(value, comparer);
            if (found != null) return found;
        }

        return null;
    }

    /// <summary>
    /// Checks if a value exists in this subtree.
    /// </summary>
    public bool Contains(T value, IEqualityComparer<T>? comparer = null)
    {
        return Find(value, comparer) != null;
    }

    /// <summary>
    /// Gets the depth of this node (distance from root).
    /// </summary>
    public int Depth()
    {
        int d = 0;
        var current = Parent;
        while (current != null) { d++; current = current.Parent; }
        return d;
    }

    /// <summary>
    /// Gets the height of this subtree.
    /// </summary>
    public int Height()
    {
        if (IsLeaf) return 1;
        return Children.Max(c => c.Height()) + 1;
    }

    /// <summary>
    /// Gets the total count of nodes in this subtree.
    /// </summary>
    public int Count()
    {
        return 1 + Children.Sum(c => c.Count());
    }

    public override string ToString() => Value?.ToString() ?? string.Empty;

    private void CleanupNullTail()
    {
        while (ChildList.Count > 0 && ChildList[^1] == null)
        {
            ChildList.RemoveAt(ChildList.Count - 1);
        }
    }
}
