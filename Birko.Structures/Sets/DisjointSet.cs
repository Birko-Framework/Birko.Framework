using System;
using System.Collections.Generic;

namespace Birko.Structures.Sets;

/// <summary>
/// Union-Find (disjoint set) with path compression and union by rank.
/// Supports near-constant time union and find operations.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public class DisjointSet<T> where T : notnull
{
    private readonly Dictionary<T, T> _parent = new();
    private readonly Dictionary<T, int> _rank = new();

    /// <summary>
    /// Gets the number of elements.
    /// </summary>
    public int Count => _parent.Count;

    /// <summary>
    /// Gets the number of disjoint sets.
    /// </summary>
    public int SetCount { get; private set; }

    /// <summary>
    /// Makes a new singleton set containing the element.
    /// Returns false if the element already exists.
    /// </summary>
    public bool MakeSet(T element)
    {
        if (_parent.ContainsKey(element)) return false;

        _parent[element] = element;
        _rank[element] = 0;
        SetCount++;
        return true;
    }

    /// <summary>
    /// Finds the representative (root) of the set containing the element.
    /// Uses path compression for amortized near-O(1) performance.
    /// </summary>
    public T Find(T element)
    {
        if (!_parent.ContainsKey(element))
        {
            throw new KeyNotFoundException($"Element '{element}' not found in disjoint set.");
        }

        if (!EqualityComparer<T>.Default.Equals(_parent[element], element))
        {
            _parent[element] = Find(_parent[element]); // Path compression
        }

        return _parent[element];
    }

    /// <summary>
    /// Unions the sets containing the two elements.
    /// Returns true if the elements were in different sets (a merge occurred).
    /// </summary>
    public bool Union(T a, T b)
    {
        var rootA = Find(a);
        var rootB = Find(b);

        if (EqualityComparer<T>.Default.Equals(rootA, rootB)) return false;

        // Union by rank
        if (_rank[rootA] < _rank[rootB])
        {
            _parent[rootA] = rootB;
        }
        else if (_rank[rootA] > _rank[rootB])
        {
            _parent[rootB] = rootA;
        }
        else
        {
            _parent[rootB] = rootA;
            _rank[rootA]++;
        }

        SetCount--;
        return true;
    }

    /// <summary>
    /// Checks if two elements are in the same set.
    /// </summary>
    public bool Connected(T a, T b)
    {
        return EqualityComparer<T>.Default.Equals(Find(a), Find(b));
    }

    /// <summary>
    /// Checks if an element exists.
    /// </summary>
    public bool Contains(T element) => _parent.ContainsKey(element);

    /// <summary>
    /// Gets all elements in the same set as the given element.
    /// </summary>
    public IReadOnlyList<T> GetSetMembers(T element)
    {
        var root = Find(element);
        var members = new List<T>();

        foreach (var item in _parent.Keys)
        {
            if (EqualityComparer<T>.Default.Equals(Find(item), root))
            {
                members.Add(item);
            }
        }

        return members;
    }

    /// <summary>
    /// Gets all disjoint sets as groups.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<T>> GetAllSets()
    {
        var groups = new Dictionary<T, List<T>>();

        foreach (var item in _parent.Keys)
        {
            var root = Find(item);
            if (!groups.TryGetValue(root, out var group))
            {
                group = new List<T>();
                groups[root] = group;
            }
            group.Add(item);
        }

        var result = new List<IReadOnlyList<T>>();
        foreach (var group in groups.Values)
        {
            result.Add(group);
        }

        return result;
    }
}
