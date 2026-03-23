using System;
using System.Collections.Generic;

namespace Birko.Structures.Caches;

/// <summary>
/// Least Recently Used (LRU) cache with O(1) get and put.
/// Uses a doubly-linked list + dictionary for constant-time operations.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
public class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map;
    private readonly LinkedList<(TKey Key, TValue Value)> _list = new();

    /// <summary>
    /// Gets the current number of items in the cache.
    /// </summary>
    public int Count => _map.Count;

    /// <summary>
    /// Gets the maximum capacity of the cache.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Creates an LRU cache with the specified capacity.
    /// </summary>
    public LruCache(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<(TKey, TValue)>>(capacity);
    }

    /// <summary>
    /// Gets a value by key. Returns true if found, moves item to most-recently-used.
    /// </summary>
    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _list.Remove(node);
            _list.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Gets a value by key, or default if not found.
    /// </summary>
    public TValue? Get(TKey key)
    {
        return TryGet(key, out var value) ? value : default;
    }

    /// <summary>
    /// Adds or updates a key-value pair. Evicts the least-recently-used item if at capacity.
    /// </summary>
    public void Put(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _list.Remove(existing);
            existing.Value = (key, value);
            _list.AddFirst(existing);
            return;
        }

        if (_map.Count >= _capacity)
        {
            var lru = _list.Last!;
            _map.Remove(lru.Value.Key);
            _list.RemoveLast();
        }

        var node = new LinkedListNode<(TKey, TValue)>((key, value));
        _list.AddFirst(node);
        _map[key] = node;
    }

    /// <summary>
    /// Removes a key from the cache.
    /// </summary>
    public bool Remove(TKey key)
    {
        if (!_map.TryGetValue(key, out var node)) return false;

        _list.Remove(node);
        _map.Remove(key);
        return true;
    }

    /// <summary>
    /// Checks if a key exists without updating recency.
    /// </summary>
    public bool ContainsKey(TKey key) => _map.ContainsKey(key);

    /// <summary>
    /// Clears the cache.
    /// </summary>
    public void Clear()
    {
        _map.Clear();
        _list.Clear();
    }

    /// <summary>
    /// Gets all keys in order from most-recently-used to least.
    /// </summary>
    public IEnumerable<TKey> Keys
    {
        get
        {
            foreach (var (key, _) in _list)
            {
                yield return key;
            }
        }
    }
}
