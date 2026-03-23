using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Structures.Graphs;

/// <summary>
/// Undirected graph with adjacency list representation.
/// </summary>
/// <typeparam name="T">The vertex value type.</typeparam>
public class Graph<T> where T : notnull
{
    protected readonly Dictionary<T, HashSet<T>> _adjacency = new();

    /// <summary>
    /// Gets the number of vertices.
    /// </summary>
    public int VertexCount => _adjacency.Count;

    /// <summary>
    /// Gets the number of edges.
    /// </summary>
    public int EdgeCount
    {
        get
        {
            int count = 0;
            foreach (var neighbors in _adjacency.Values)
            {
                count += neighbors.Count;
            }
            return count / 2;
        }
    }

    /// <summary>
    /// Gets all vertices.
    /// </summary>
    public IReadOnlyCollection<T> Vertices => _adjacency.Keys;

    /// <summary>
    /// Adds a vertex.
    /// </summary>
    public bool AddVertex(T vertex)
    {
        if (_adjacency.ContainsKey(vertex)) return false;
        _adjacency[vertex] = new HashSet<T>();
        return true;
    }

    /// <summary>
    /// Removes a vertex and all its edges.
    /// </summary>
    public bool RemoveVertex(T vertex)
    {
        if (!_adjacency.Remove(vertex)) return false;

        foreach (var neighbors in _adjacency.Values)
        {
            neighbors.Remove(vertex);
        }
        return true;
    }

    /// <summary>
    /// Adds an undirected edge between two vertices.
    /// Creates vertices if they don't exist.
    /// </summary>
    public virtual bool AddEdge(T from, T to)
    {
        AddVertex(from);
        AddVertex(to);

        var added = _adjacency[from].Add(to);
        _adjacency[to].Add(from);
        return added;
    }

    /// <summary>
    /// Removes an undirected edge.
    /// </summary>
    public virtual bool RemoveEdge(T from, T to)
    {
        if (!_adjacency.ContainsKey(from) || !_adjacency.ContainsKey(to)) return false;

        var removed = _adjacency[from].Remove(to);
        _adjacency[to].Remove(from);
        return removed;
    }

    /// <summary>
    /// Checks if a vertex exists.
    /// </summary>
    public bool HasVertex(T vertex) => _adjacency.ContainsKey(vertex);

    /// <summary>
    /// Checks if an edge exists.
    /// </summary>
    public virtual bool HasEdge(T from, T to)
    {
        return _adjacency.TryGetValue(from, out var neighbors) && neighbors.Contains(to);
    }

    /// <summary>
    /// Gets the neighbors of a vertex.
    /// </summary>
    public IReadOnlyCollection<T> GetNeighbors(T vertex)
    {
        return _adjacency.TryGetValue(vertex, out var neighbors)
            ? neighbors
            : Array.Empty<T>();
    }

    /// <summary>
    /// Gets the degree (number of edges) of a vertex.
    /// </summary>
    public int Degree(T vertex)
    {
        return _adjacency.TryGetValue(vertex, out var neighbors) ? neighbors.Count : 0;
    }

    /// <summary>
    /// Breadth-first search from a start vertex.
    /// </summary>
    public IEnumerable<T> BreadthFirst(T start)
    {
        if (!_adjacency.ContainsKey(start)) yield break;

        var visited = new HashSet<T>();
        var queue = new Queue<T>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current;

            foreach (var neighbor in _adjacency[current])
            {
                if (visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }
    }

    /// <summary>
    /// Depth-first search from a start vertex.
    /// </summary>
    public IEnumerable<T> DepthFirst(T start)
    {
        if (!_adjacency.ContainsKey(start)) yield break;

        var visited = new HashSet<T>();
        var stack = new Stack<T>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current)) continue;

            yield return current;

            foreach (var neighbor in _adjacency[current])
            {
                if (!visited.Contains(neighbor))
                {
                    stack.Push(neighbor);
                }
            }
        }
    }

    /// <summary>
    /// Finds the shortest path (by hop count) between two vertices using BFS.
    /// Returns null if no path exists.
    /// </summary>
    public IReadOnlyList<T>? ShortestPath(T from, T to)
    {
        if (!_adjacency.ContainsKey(from) || !_adjacency.ContainsKey(to)) return null;
        if (EqualityComparer<T>.Default.Equals(from, to)) return new[] { from };

        var visited = new HashSet<T> { from };
        var queue = new Queue<T>();
        var parent = new Dictionary<T, T>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var neighbor in _adjacency[current])
            {
                if (!visited.Add(neighbor)) continue;

                parent[neighbor] = current;

                if (EqualityComparer<T>.Default.Equals(neighbor, to))
                {
                    return ReconstructPath(parent, from, to);
                }

                queue.Enqueue(neighbor);
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if the graph is connected (all vertices reachable from any vertex).
    /// </summary>
    public bool IsConnected()
    {
        if (_adjacency.Count == 0) return true;

        var start = _adjacency.Keys.First();
        var visited = BreadthFirst(start).Count();
        return visited == _adjacency.Count;
    }

    private static List<T> ReconstructPath(Dictionary<T, T> parent, T from, T to)
    {
        var path = new List<T>();
        var current = to;
        while (!EqualityComparer<T>.Default.Equals(current, from))
        {
            path.Add(current);
            current = parent[current];
        }
        path.Add(from);
        path.Reverse();
        return path;
    }
}
