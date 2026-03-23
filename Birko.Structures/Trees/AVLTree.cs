using System;
using System.Collections.Generic;

namespace Birko.Structures.Trees;

/// <summary>
/// Self-balancing AVL tree. Maintains O(log n) height via rotations after every insert/remove.
/// </summary>
/// <typeparam name="T">A comparable value type.</typeparam>
public class AVLTree<T> : BinarySearchTree<T> where T : IComparable<T>
{
    /// <summary>
    /// Creates an empty AVL tree.
    /// </summary>
    public AVLTree()
    {
    }

    /// <summary>
    /// Creates an AVL tree from a collection of values.
    /// </summary>
    public AVLTree(IEnumerable<T> values)
    {
        foreach (var value in values)
        {
            Insert(value);
        }
    }

    /// <summary>
    /// Inserts a value and rebalances the tree.
    /// </summary>
    public override BinaryNode<T> Insert(T value)
    {
        var node = base.Insert(value);
        RebalanceFrom(node);
        return node;
    }

    /// <summary>
    /// Removes a value and rebalances the tree.
    /// </summary>
    public override bool Remove(T value)
    {
        var node = Find(value);
        if (node == null) return false;

        var rebalanceFrom = node.Parent;
        RemoveNode(node);
        Count--;

        if (rebalanceFrom != null)
        {
            RebalanceFrom(rebalanceFrom);
        }
        else if (Root != null)
        {
            Root = Rebalance(Root);
        }

        return true;
    }

    private void RebalanceFrom(BinaryNode<T> node)
    {
        var current = node;
        while (current != null)
        {
            var balanced = Rebalance(current);
            if (balanced.Parent == null)
            {
                Root = balanced;
            }
            current = balanced.Parent;
        }
    }

    private static BinaryNode<T> Rebalance(BinaryNode<T> node)
    {
        int balance = node.Balance();

        if (balance > 1) // Right-heavy
        {
            if (node.Right != null && node.Right.Balance() < 0)
            {
                node.Right = RotateRight(node.Right);
            }
            return RotateLeft(node);
        }

        if (balance < -1) // Left-heavy
        {
            if (node.Left != null && node.Left.Balance() > 0)
            {
                node.Left = RotateLeft(node.Left);
            }
            return RotateRight(node);
        }

        return node;
    }

    private static BinaryNode<T> RotateLeft(BinaryNode<T> node)
    {
        var newRoot = node.Right!;
        var newRootLeft = newRoot.Left;

        newRoot.Left = node;
        node.Right = newRootLeft;

        newRoot.Parent = node.Parent;
        node.Parent = newRoot;
        if (newRootLeft != null) newRootLeft.Parent = node;

        if (newRoot.Parent != null)
        {
            if (newRoot.Parent.Left == node)
                newRoot.Parent.Left = newRoot;
            else
                newRoot.Parent.Right = newRoot;
        }

        return newRoot;
    }

    private static BinaryNode<T> RotateRight(BinaryNode<T> node)
    {
        var newRoot = node.Left!;
        var newRootRight = newRoot.Right;

        newRoot.Right = node;
        node.Left = newRootRight;

        newRoot.Parent = node.Parent;
        node.Parent = newRoot;
        if (newRootRight != null) newRootRight.Parent = node;

        if (newRoot.Parent != null)
        {
            if (newRoot.Parent.Left == node)
                newRoot.Parent.Left = newRoot;
            else
                newRoot.Parent.Right = newRoot;
        }

        return newRoot;
    }
}
