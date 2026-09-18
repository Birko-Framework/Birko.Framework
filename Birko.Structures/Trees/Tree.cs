using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Structures.Trees;

/// <summary>
/// Generic tree container.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public class Tree<T>
{
    /// <summary>
    /// The root node.
    /// </summary>
    public Node<T>? Root { get; protected set; }

    /// <summary>
    /// Gets the number of nodes in the tree.
    /// </summary>
    public int Count => Root?.Count() ?? 0;

    /// <summary>
    /// Gets the height of the tree.
    /// </summary>
    public int Height => Root?.Height() ?? 0;

    /// <summary>
    /// Gets whether the tree is empty.
    /// </summary>
    public bool IsEmpty => Root == null;

    /// <summary>
    /// Creates an empty tree.
    /// </summary>
    public Tree()
    {
    }

    /// <summary>
    /// Creates a tree with a root value.
    /// </summary>
    public Tree(T rootValue)
    {
        Root = new Node<T>(rootValue);
    }

    /// <summary>
    /// Sets the root node. Returns the root.
    /// </summary>
    public Node<T> SetRoot(T value)
    {
        Root = new Node<T>(value);
        return Root;
    }

    /// <summary>
    /// Finds a node by value.
    /// </summary>
    public Node<T>? Find(T value, IEqualityComparer<T>? comparer = null)
    {
        return Root?.Find(value, comparer);
    }

    /// <summary>
    /// Checks if a value exists in the tree.
    /// </summary>
    public bool Contains(T value, IEqualityComparer<T>? comparer = null)
    {
        return Root?.Contains(value, comparer) ?? false;
    }

    /// <summary>
    /// Pre-order traversal (node, then children).
    /// </summary>
    public IEnumerable<T> PreOrder()
    {
        if (Root == null) yield break;
        foreach (var node in PreOrder(Root))
            yield return node.Value;
    }

    /// <summary>
    /// Post-order traversal (children, then node).
    /// </summary>
    public IEnumerable<T> PostOrder()
    {
        if (Root == null) yield break;
        foreach (var node in PostOrder(Root))
            yield return node.Value;
    }

    /// <summary>
    /// Level-order (breadth-first) traversal.
    /// </summary>
    public IEnumerable<T> LevelOrder()
    {
        if (Root == null) yield break;

        var queue = new Queue<Node<T>>();
        queue.Enqueue(Root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current.Value;

            foreach (var child in current.Children)
            {
                queue.Enqueue(child);
            }
        }
    }

    private static IEnumerable<Node<T>> PreOrder(Node<T> node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in PreOrder(child))
                yield return descendant;
        }
    }

    private static IEnumerable<Node<T>> PostOrder(Node<T> node)
    {
        foreach (var child in node.Children)
        {
            foreach (var descendant in PostOrder(child))
                yield return descendant;
        }
        yield return node;
    }
}
