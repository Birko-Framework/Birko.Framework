# Data Structures Guide

## Overview

`Birko.Structures` provides a library of general-purpose data structures for .NET — trees, graphs, heaps, tries, caching, filters, and specialized lists. Zero external dependencies; pure BCL implementations. Distributed as a shared project, so files compile directly into the consuming project.

## Namespace layout

```
Birko.Structures.Trees     — Binary/BST/AVL, N-ary tree, interval tree
Birko.Structures.Graphs    — Undirected, directed, weighted graphs
Birko.Structures.Heaps     — Binary heap (min/max)
Birko.Structures.Tries     — Standard + compressed (radix) trie
Birko.Structures.Caches    — LRU cache
Birko.Structures.Filters   — Bloom filter
Birko.Structures.Buffers   — Ring buffer
Birko.Structures.Sets      — Disjoint set (union-find)
Birko.Structures.Lists     — Skip list, deque
```

## Category overview

| Category | Type | When to use |
|---|---|---|
| **Trees** | `BinarySearchTree<T>` | Sorted lookup, in/pre/post/level-order traversal |
| | `AVLTree<T>` | Same as BST but guaranteed O(log n) via self-balancing |
| | `Tree<T>` / `Node<T>` | Generic N-ary tree (file trees, org charts) |
| | `IntervalTree<T>` | Overlapping-range queries (scheduling, bookings, timelines) |
| **Graphs** | `Graph<T>` | Undirected, BFS/DFS, shortest hop count |
| | `DirectedGraph<T>` | DAGs, topological sort, cycle detection |
| | `WeightedGraph<T>` | Dijkstra shortest path |
| **Heaps** | `MinHeap<T>` / `MaxHeap<T>` | Priority queues, top-K, event schedulers |
| **Tries** | `Trie` | Autocomplete, prefix search over dense dictionaries |
| | `CompressedTrie` | Same as Trie but memory-efficient for sparse datasets |
| **Caches** | `LruCache<TKey, TValue>` | Bounded cache with O(1) get/put |
| **Filters** | `BloomFilter<T>` | Probabilistic membership check (cache pre-filter, URL dedup) |
| **Buffers** | `RingBuffer<T>` | Fixed-size FIFO with overwrite semantics (logs, metrics windows) |
| **Sets** | `DisjointSet<T>` | Union-find (connectivity, Kruskal MST) |
| **Lists** | `SkipList<T>` | Sorted list with O(log n) range queries |
| | `Deque<T>` | Double-ended queue (sliding window, undo/redo) |

## Trees

### BinarySearchTree<T>

```csharp
var bst = new BinarySearchTree<int>();
bst.Insert(5); bst.Insert(3); bst.Insert(7); bst.Insert(1);

bst.Contains(3);          // true
bst.Min();                // 1
bst.Max();                // 7
bst.InOrder();            // [1, 3, 5, 7] (lazy IEnumerable)
bst.PreOrder();           // [5, 3, 1, 7]
bst.PostOrder();          // [1, 3, 7, 5]
bst.LevelOrder();         // [5, 3, 7, 1]
bst.Remove(3);
```

Requires `T : IComparable<T>`. Traversals return `IEnumerable<T>` (lazy).

### AVLTree<T>

Drop-in replacement for `BinarySearchTree<T>` — same API, automatic rotations on Insert/Remove keep the tree balanced. Use it when insertion order is unknown or adversarial.

### IntervalTree<T>

```csharp
var tree = new IntervalTree<int>();
tree.Insert(1, 5);
tree.Insert(3, 8);
tree.Insert(10, 15);

tree.QueryOverlapping(new Interval<int>(4, 6));  // [1..5, 3..8]
tree.QueryPoint(12);                              // [10..15]
```

Augmented BST with max-endpoint tracking for O(log n + k) overlap queries (k = matches).

## Graphs

```csharp
// Directed acyclic graph with topological sort
var dag = new DirectedGraph<string>();
dag.AddEdge("compile", "link");
dag.AddEdge("link", "deploy");
dag.AddEdge("test", "deploy");

dag.TopologicalSort();     // ["compile", "test", "link", "deploy"]
dag.HasCycle();            // false

// Weighted graph with Dijkstra
var wg = new WeightedGraph<string>();
wg.AddEdge("A", "B", 5);
wg.AddEdge("B", "C", 3);
wg.AddEdge("A", "C", 10);

var (path, distance) = wg.ShortestPath("A", "C");  // (["A","B","C"], 8)
```

All graphs support `BFS`, `DFS`, `AddVertex`/`RemoveVertex`, `AddEdge`/`RemoveEdge`, `Connected`.

## Heaps

```csharp
var minHeap = new MinHeap<int>();
minHeap.Push(5); minHeap.Push(2); minHeap.Push(8);
minHeap.Peek();            // 2
minHeap.Pop();             // 2
minHeap.Replace(10);       // pops and pushes in one operation (more efficient)

// Custom comparer:
var byDeadline = new BinaryHeap<Task>(Comparer<Task>.Create((a, b) => a.Deadline.CompareTo(b.Deadline)));
```

## Tries

```csharp
var trie = new Trie();
trie.Insert("cat");
trie.Insert("car");
trie.Insert("cart");

trie.Search("cat");                         // true (exact match)
trie.StartsWith("ca");                      // true (prefix)
trie.GetWordsWithPrefix("car");             // ["car", "cart"]
trie.Remove("car");
```

Use `CompressedTrie` for sparse datasets (many long strings sharing no prefixes) — it collapses single-child chains into edge labels.

## LRU Cache

```csharp
var cache = new LruCache<string, User>(capacity: 1000);
cache.Put("user-1", user);

if (cache.TryGet("user-1", out var cached))
{
    // cache hit — user-1 moved to front
}
// When capacity is exceeded, the least-recently-used entry is evicted.
```

O(1) get/put via doubly-linked list + dictionary.

## Bloom Filter

```csharp
var filter = new BloomFilter<string>(expectedItems: 10_000, falsePositiveRate: 0.01);
filter.Add("user@example.com");

filter.MayContain("user@example.com");   // true (definitely seen or false positive)
filter.MayContain("other@example.com");  // false (definitely never added)
```

**Semantics:** `MayContain` returning `false` is a guarantee; `true` is probabilistic (~1% false-positive rate by default). No false negatives, no removal, memory-efficient.

## Skip List

```csharp
var sl = new SkipList<int>();
sl.Insert(5); sl.Insert(2); sl.Insert(8); sl.Insert(1);

sl.Search(5);               // true
sl.Range(2, 6);             // [2, 5] — O(log n + k) range query

// Inject Birko.Random for test determinism:
var rng = new Birko.Random.XorShiftProvider(seed: 42);
var deterministic = new SkipList<int>(rng.NextDouble);
```

## Deque, Ring Buffer, Disjoint Set

```csharp
// Double-ended queue — O(1) both ends
var dq = new Deque<int>();
dq.PushFront(1); dq.PushBack(2);
dq.PopFront();              // 1

// Fixed-capacity circular buffer — overwrites oldest when full
var rb = new RingBuffer<LogEntry>(capacity: 1000);
rb.Write(entry);            // silently overwrites oldest when full

// Union-find — near O(1) amortized with path compression + union by rank
var ds = new DisjointSet<string>();
ds.MakeSet("a"); ds.MakeSet("b"); ds.MakeSet("c");
ds.Union("a", "b");
ds.Connected("a", "b");     // true
ds.Connected("a", "c");     // false
```

## Thread safety

**None of these structures are thread-safe.** They target single-writer scenarios. For concurrent access, wrap externally with `lock` or use the BCL's `ConcurrentDictionary` / `Channel<T>`.

## Generic constraints

| Constraint | Types |
|---|---|
| `T : IComparable<T>` | Trees (BST, AVL), heaps, skip list, interval tree |
| `T : notnull` | Graphs, disjoint set, trie |
| `TKey : notnull` | LRU cache |
| (no constraint) | Ring buffer, deque |

## See also

- [Random Guide](random.md) — pluggable RNG that pairs well with SkipList and probabilistic structures
- [Caching Guide](caching.md) — distributed caching (Redis, Hybrid) on top of LruCache semantics
