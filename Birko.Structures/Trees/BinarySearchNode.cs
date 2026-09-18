using System;
using System.Collections.Generic;

namespace Birko.Structures.Trees;

/// <summary>
/// Binary search tree with generic comparable values.
/// Stores values in BinaryNode&lt;T&gt; with BST ordering.
/// </summary>
/// <typeparam name="T">A comparable value type.</typeparam>
public class BinarySearchTree<T> where T : IComparable<T>
{
    /// <summary>
    /// The root node.
    /// </summary>
    public BinaryNode<T>? Root { get; protected set; }

    /// <summary>
    /// Gets the number of nodes.
    /// </summary>
    public int Count { get; protected set; }

    /// <summary>
    /// Gets whether the tree is empty.
    /// </summary>
    public bool IsEmpty => Root == null;

    /// <summary>
    /// Gets the height of the tree.
    /// </summary>
    public int Height => Root?.Height() ?? 0;

    /// <summary>
    /// Creates an empty BST.
    /// </summary>
    public BinarySearchTree()
    {
    }

    /// <summary>
    /// Creates a BST from a collection of values.
    /// </summary>
    public BinarySearchTree(IEnumerable<T> values)
    {
        foreach (var value in values)
        {
            Insert(value);
        }
    }

    /// <summary>
    /// Inserts a value. Returns the inserted node.
    /// </summary>
    public virtual BinaryNode<T> Insert(T value)
    {
        var newNode = new BinaryNode<T>(value);

        if (Root == null)
        {
            Root = newNode;
            Count++;
            return Root;
        }

        var current = Root;
        while (true)
        {
            if (value.CompareTo(current.Value) <= 0)
            {
                if (current.Left == null)
                {
                    current.Left = newNode;
                    newNode.Parent = current;
                    Count++;
                    return newNode;
                }
                current = current.Left;
            }
            else
            {
                if (current.Right == null)
                {
                    current.Right = newNode;
                    newNode.Parent = current;
                    Count++;
                    return newNode;
                }
                current = current.Right;
            }
        }
    }

    /// <summary>
    /// Searches for a value. Returns the node or null.
    /// </summary>
    public BinaryNode<T>? Find(T value)
    {
        var current = Root;
        while (current != null)
        {
            int cmp = value.CompareTo(current.Value);
            if (cmp == 0) return current;
            current = cmp < 0 ? current.Left : current.Right;
        }
        return null;
    }

    /// <summary>
    /// Checks if a value exists.
    /// </summary>
    public bool Contains(T value) => Find(value) != null;

    /// <summary>
    /// Removes a value. Returns true if found and removed.
    /// </summary>
    public virtual bool Remove(T value)
    {
        var node = Find(value);
        if (node == null) return false;

        RemoveNode(node);
        Count--;
        return true;
    }

    /// <summary>
    /// Gets the minimum value.
    /// </summary>
    public T Min()
    {
        if (Root == null) throw new InvalidOperationException("Tree is empty.");
        var current = Root;
        while (current.Left != null) current = current.Left;
        return current.Value;
    }

    /// <summary>
    /// Gets the maximum value.
    /// </summary>
    public T Max()
    {
        if (Root == null) throw new InvalidOperationException("Tree is empty.");
        var current = Root;
        while (current.Right != null) current = current.Right;
        return current.Value;
    }

    /// <summary>
    /// In-order traversal (sorted).
    /// </summary>
    public IEnumerable<T> InOrder()
    {
        return InOrder(Root);
    }

    /// <summary>
    /// Pre-order traversal.
    /// </summary>
    public IEnumerable<T> PreOrder()
    {
        return PreOrder(Root);
    }

    /// <summary>
    /// Post-order traversal.
    /// </summary>
    public IEnumerable<T> PostOrder()
    {
        return PostOrder(Root);
    }

    /// <summary>
    /// Level-order (BFS) traversal.
    /// </summary>
    public IEnumerable<T> LevelOrder()
    {
        if (Root == null) yield break;

        var queue = new Queue<BinaryNode<T>>();
        queue.Enqueue(Root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current.Value;
            if (current.Left != null) queue.Enqueue(current.Left);
            if (current.Right != null) queue.Enqueue(current.Right);
        }
    }

    protected void RemoveNode(BinaryNode<T> node)
    {
        if (node.Left != null && node.Right != null)
        {
            var successor = node.Right;
            while (successor.Left != null) successor = successor.Left;
            node.Value = successor.Value;
            RemoveNode(successor);
            return;
        }

        var child = node.Left ?? node.Right;

        if (node.Parent == null)
        {
            Root = child;
        }
        else if (node.Parent.Left == node)
        {
            node.Parent.Left = child;
        }
        else
        {
            node.Parent.Right = child;
        }

        if (child != null)
        {
            child.Parent = node.Parent;
        }
    }

    private static IEnumerable<T> InOrder(BinaryNode<T>? node)
    {
        if (node == null) yield break;
        foreach (var v in InOrder(node.Left)) yield return v;
        yield return node.Value;
        foreach (var v in InOrder(node.Right)) yield return v;
    }

    private static IEnumerable<T> PreOrder(BinaryNode<T>? node)
    {
        if (node == null) yield break;
        yield return node.Value;
        foreach (var v in PreOrder(node.Left)) yield return v;
        foreach (var v in PreOrder(node.Right)) yield return v;
    }

    private static IEnumerable<T> PostOrder(BinaryNode<T>? node)
    {
        if (node == null) yield break;
        foreach (var v in PostOrder(node.Left)) yield return v;
        foreach (var v in PostOrder(node.Right)) yield return v;
        yield return node.Value;
    }
}
