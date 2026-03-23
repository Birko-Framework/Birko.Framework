using System;

namespace Birko.Structures.Trees;

/// <summary>
/// Binary tree node with exactly two child slots (left and right).
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public class BinaryNode<T>
{
    /// <summary>
    /// The value stored in this node.
    /// </summary>
    public T Value { get; set; }

    /// <summary>
    /// The left child.
    /// </summary>
    public BinaryNode<T>? Left { get; internal set; }

    /// <summary>
    /// The right child.
    /// </summary>
    public BinaryNode<T>? Right { get; internal set; }

    /// <summary>
    /// The parent node.
    /// </summary>
    public BinaryNode<T>? Parent { get; internal set; }

    /// <summary>
    /// Gets whether this is a leaf node.
    /// </summary>
    public bool IsLeaf => Left == null && Right == null;

    /// <summary>
    /// Gets the height of this subtree.
    /// </summary>
    public int Height()
    {
        int leftH = Left?.Height() ?? 0;
        int rightH = Right?.Height() ?? 0;
        return Math.Max(leftH, rightH) + 1;
    }

    /// <summary>
    /// Gets the balance factor (right height - left height).
    /// </summary>
    public int Balance()
    {
        return (Right?.Height() ?? 0) - (Left?.Height() ?? 0);
    }

    /// <summary>
    /// Creates a binary node with the specified value.
    /// </summary>
    public BinaryNode(T value)
    {
        Value = value;
    }

    public override string ToString() => Value?.ToString() ?? string.Empty;
}
