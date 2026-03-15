# Birko.Structures

## Overview
Data structure implementations for the Birko data layer providing trees and other structures.

## Project Location
`C:\Source\Birko.Structures\`

## Purpose
- Common data structures
- Tree implementations
- Hierarchical data support
- Structure utilities

## Components

### Trees
- `TreeNode<T>` - Generic tree node
- `Tree<T>` - Tree structure
- `BinaryTree<T>` - Binary tree
- `BinarySearchTree<T>` - Binary search tree

### Extensions
- Structure extensions for LINQ
- Structure serialization
- Structure traversal helpers

## Tree Node

```csharp
using Birko.Structures.Trees;

public class TreeNode<T>
{
    public T Value { get; set; }
    public TreeNode<T> Parent { get; set; }
    public IList<TreeNode<T>> Children { get; set; }

    public bool IsRoot => Parent == null;
    public bool IsLeaf => Children.Count == 0;
    public int Depth { get; }
    public int Height { get; }
}
```

## Tree Implementation

```csharp
var tree = new Tree<string>();

var root = new TreeNode<string> { Value = "Root" };
var child1 = new TreeNode<string> { Value = "Child 1" };
var child2 = new TreeNode<string> { Value = "Child 2" };

root.Children.Add(child1);
root.Children.Add(child2);
child1.Parent = root;
child2.Parent = root;

tree.Root = root;
```

## Tree Traversals

### Depth-First (Pre-Order)
```csharp
public IEnumerable<TreeNode<T>> TraversePreOrder(TreeNode<T> node)
{
    yield return node;
    foreach (var child in node.Children)
    {
        foreach (var descendant in TraversePreOrder(child))
        {
            yield return descendant;
        }
    }
}
```

### Depth-First (Post-Order)
```csharp
public IEnumerable<TreeNode<T>> TraversePostOrder(TreeNode<T> node)
{
    foreach (var child in node.Children)
    {
        foreach (var descendant in TraversePostOrder(child))
        {
            yield return descendant;
        }
    }
    yield return node;
}
```

### Breadth-First
```csharp
public IEnumerable<TreeNode<T>> TraverseBreadthFirst(TreeNode<T> root)
{
    var queue = new Queue<TreeNode<T>>();
    queue.Enqueue(root);

    while (queue.Count > 0)
    {
        var node = queue.Dequeue();
        yield return node;

        foreach (var child in node.Children)
        {
            queue.Enqueue(child);
        }
    }
}
```

## Hierarchical Data in Database

### Adjacency List
```sql
CREATE TABLE categories (
    id UUID PRIMARY KEY,
    parent_id UUID REFERENCES categories(id),
    name TEXT NOT NULL
);
```

### Nested Set (Modified Preorder Tree Traversal)
```sql
CREATE TABLE categories (
    id UUID PRIMARY KEY,
    lft INT NOT NULL,
    rgt INT NOT NULL,
    name TEXT NOT NULL
);
```

### Materialized Path
```sql
CREATE TABLE categories (
    id UUID PRIMARY KEY,
    path TEXT NOT NULL, -- e.g., "/1/4/7/"
    name TEXT NOT NULL
);
```

## Dependencies
- Birko.Data.Core

## Use Cases
- Category hierarchies
- Organization charts
- File systems
- Comment threads
- Menu structures
- Bill of materials

## Related Projects
- [Birko.Models.Category](../Birko.Models.Category/CLAUDE.md) - Category models with hierarchy

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
