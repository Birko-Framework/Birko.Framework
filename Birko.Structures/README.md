# Birko.Structures

General-purpose data structure library for the Birko Framework.

## Features

- **Trees** — Node, BinaryNode, BinarySearchNode, AVLTree, IntervalTree
- **Graphs** — Graph, DirectedGraph (topological sort), WeightedGraph (Dijkstra)
- **Heaps** — BinaryHeap, MinHeap, MaxHeap
- **Tries** — Trie, CompressedTrie (radix tree)
- **Caches** — LruCache (O(1) get/put with eviction)
- **Filters** — BloomFilter (probabilistic membership)
- **Buffers** — RingBuffer (circular, overwrite-oldest)
- **Sets** — DisjointSet (Union-Find with path compression)
- **Lists** — SkipList (sorted, O(log n)), Deque (double-ended queue)
- **Traversals** — PreOrder, PostOrder, InOrder, LevelOrder (BFS)

## Usage

```csharp
// Graph with BFS/DFS
var graph = new Graph<string>();
graph.AddEdge("A", "B");
graph.AddEdge("B", "C");
var path = graph.ShortestPath("A", "C"); // ["A", "B", "C"]

// Directed graph with topological sort
var dag = new DirectedGraph<string>();
dag.AddEdge("compile", "link");
dag.AddEdge("link", "deploy");
var order = dag.TopologicalSort(); // ["compile", "link", "deploy"]

// Weighted shortest path (Dijkstra)
var wg = new WeightedGraph<string>();
wg.AddEdge("A", "B", 1.0);
wg.AddEdge("B", "C", 2.0);
wg.AddEdge("A", "C", 5.0);
var (dist, path) = wg.ShortestPath("A", "C")!.Value; // dist=3.0

// Min/Max heap
var minHeap = new MinHeap<int>();
minHeap.Push(5); minHeap.Push(1); minHeap.Push(3);
var min = minHeap.Pop(); // 1

// Trie autocomplete
var trie = new Trie();
trie.Insert("hello"); trie.Insert("help"); trie.Insert("world");
var matches = trie.GetWordsWithPrefix("hel"); // ["hello", "help"]

// LRU cache
var cache = new LruCache<string, int>(100);
cache.Put("key", 42);
cache.TryGet("key", out var value); // true, value=42

// Bloom filter
var filter = new BloomFilter<string>(1000, 0.01);
filter.Add("item1");
filter.MayContain("item1"); // true
filter.MayContain("item2"); // false (probably)

// Ring buffer
var ring = new RingBuffer<int>(3);
ring.Write(1); ring.Write(2); ring.Write(3); ring.Write(4);
ring.Read(); // 2 (oldest after overwrite)

// Interval tree
var intervals = new IntervalTree<int>();
intervals.Insert(1, 5);
intervals.Insert(3, 8);
var overlapping = intervals.QueryOverlapping(4, 6); // both intervals

// Union-Find
var uf = new DisjointSet<string>();
uf.MakeSet("A"); uf.MakeSet("B"); uf.MakeSet("C");
uf.Union("A", "B");
uf.Connected("A", "B"); // true

// Skip list (sorted)
var sl = new SkipList<int>();
sl.Insert(3); sl.Insert(1); sl.Insert(5);
var range = sl.Range(1, 4); // [1, 3]

// Skip list with custom random source (e.g. Birko.Random)
// var rng = new Birko.Random.SystemRandomProvider();
// var sl2 = new SkipList<int>(rng.NextDouble);

// Deque
var dq = new Deque<int>();
dq.PushBack(1); dq.PushFront(0); dq.PushBack(2);
dq.PopFront(); // 0
dq.PopBack();  // 2
```

## Dependencies

- .NET 10.0
- Birko.Data.Core (for tree Node base class)

## Related Projects

- [Birko.Structures.Tests](../Birko.Structures.Tests/) - Unit tests

## License

Part of the Birko Framework.
