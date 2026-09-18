# Birko.Structures

## Overview
General-purpose data structure library for the Birko Framework — trees, graphs, heaps, tries, caches, filters, buffers, sets, and lists.

## Project Location
`C:\Source\Birko.Structures\`

## Purpose
- Tree hierarchies (general, binary, AVL, interval)
- Graph algorithms (BFS, DFS, Dijkstra, topological sort)
- Priority queues (min/max heaps)
- String indexing (trie, compressed/radix trie)
- Lightweight caching (LRU)
- Probabilistic data structures (Bloom filter, skip list)
- Circular buffers and double-ended queues
- Union-Find for disjoint set operations

## Components

### Trees (`Trees/`)
- `Node<T>` — Generic tree node with parent/children, find, depth, height, count
- `BinaryNode<T>` — Binary tree node (left/right children, balance factor)
- `BinarySearchTree<T>` — BST with insert, find, remove, in-order/pre-order/post-order/level-order traversal
- `AVLTree<T>` — Self-balancing AVL tree with rotations (extends BinarySearchTree)
- `Tree<T>` — Generic N-ary tree container with traversals
- `IntervalTree<T>` — Augmented BST for interval overlap queries (`Interval<T>`, `QueryOverlapping`, `QueryPoint`)

### Graphs (`Graphs/`)
- `Graph<T>` — Undirected graph: BFS, DFS, shortest path (hop count), connectivity check
- `DirectedGraph<T>` — Digraph: topological sort, cycle detection, in/out degree, reverse
- `WeightedGraph<T>` — Weighted directed graph: Dijkstra shortest path, distance map

### Heaps (`Heaps/`)
- `BinaryHeap<T>` — Array-backed binary heap with configurable comparer, push/pop/peek/replace, heapify
- `MinHeap<T>` — Convenience: smallest element on top
- `MaxHeap<T>` — Convenience: largest element on top

### Tries (`Tries/`)
- `Trie` — Standard prefix tree: insert, search, prefix search, delete, get all words
- `CompressedTrie` — Radix/Patricia tree: edge compression for sparse key sets

### Caches (`Caches/`)
- `LruCache<TKey, TValue>` — O(1) get/put with capacity-based eviction (doubly-linked list + dictionary)

### Filters (`Filters/`)
- `BloomFilter<T>` — Probabilistic membership test: configurable false positive rate, double hashing

### Buffers (`Buffers/`)
- `RingBuffer<T>` — Fixed-capacity circular buffer: overwrite-oldest, read/write/peek

### Sets (`Sets/`)
- `DisjointSet<T>` — Union-Find: path compression + union by rank, connected check, get set members

### Lists (`Lists/`)
- `SkipList<T>` — Probabilistic sorted list: O(log n) insert/search/remove, range queries. Accepts optional `Func<double>` for pluggable randomness (e.g. `new SkipList<T>(myProvider.NextDouble)` with Birko.Random)
- `Deque<T>` — Double-ended queue: O(1) amortized push/pop from both ends, circular array

## Usage

```csharp
// N-ary tree
var tree = new Tree<string>("Root");
var child1 = tree.Root.AddChild("Child 1");
var child2 = tree.Root.AddChild("Child 2");
foreach (var val in tree.PreOrder()) { /* Root, Child 1, Child 2 */ }

// Binary search tree / AVL tree
var avl = new AVLTree<int>();
avl.Insert(5); avl.Insert(3); avl.Insert(7);
var found = avl.Find(3);       // BinaryNode<int>
foreach (var v in avl.InOrder()) { /* 3, 5, 7 */ }

// Skip list with custom random (Birko.Random integration)
// var rng = new Birko.Random.SystemRandomProvider();
// var sl = new SkipList<int>(rng.NextDouble);
var sl = new SkipList<int>();   // default System.Random
sl.Insert(3); sl.Insert(1); sl.Insert(5);
var range = sl.Range(1, 4);    // [1, 3]
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
- **Trees** — Category hierarchies, organization charts, file systems, comment threads
- **Graphs** — Workflow routing, dependency resolution, migration ordering, network topology
- **Heaps** — Job scheduling, event ordering, top-K queries
- **Tries** — Autocomplete, localization key lookup, IP routing
- **LruCache** — Lightweight eviction without full Birko.Caching dependency
- **BloomFilter** — Deduplication in event bus, cache prefetch decisions
- **RingBuffer** — Telemetry sampling, sliding window metrics
- **IntervalTree** — Business calendar overlap, time-range queries
- **DisjointSet** — Tenant grouping, data sync partitioning, Kruskal's MST
- **SkipList** — Concurrent ordered collections, sorted set operations
- **Deque** — Work-stealing queues, BFS with bidirectional expansion

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
