# Birko.Structures

Generic data structure implementations for the Birko Framework.

## Features

- Tree structures (TreeNode, Tree)
- Binary trees (BinaryTree, BinarySearchTree, AVL Tree)
- Traversal methods (PreOrder, PostOrder, InOrder, BreadthFirst)
- Hierarchical data support with multiple storage patterns

## Installation

```bash
dotnet add package Birko.Structures
```

## Dependencies

- .NET 10.0

## Usage

```csharp
using Birko.Structures;

var tree = new BinarySearchTree<int>();
tree.Insert(5);
tree.Insert(3);
tree.Insert(7);

var inOrder = tree.InOrderTraversal(); // [3, 5, 7]
```

## API Reference

- **TreeNode\<T\>** / **Tree\<T\>** - Generic tree
- **BinaryTree\<T\>** - Binary tree
- **BinarySearchTree\<T\>** - BST with search/insert
- **AVLTree\<T\>** - Self-balancing AVL tree

## Related Projects

- [Birko.Structures.Tests](../Birko.Structures.Tests/) - Unit tests

## License

Part of the Birko Framework.
