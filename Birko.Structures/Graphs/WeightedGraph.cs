using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Structures.Graphs;

/// <summary>
/// Weighted edge.
/// </summary>
/// <typeparam name="T">The vertex value type.</typeparam>
public readonly record struct WeightedEdge<T>(T From, T To, double Weight) where T : notnull;

/// <summary>
/// Weighted directed graph with Dijkstra shortest path.
/// </summary>
/// <typeparam name="T">The vertex value type.</typeparam>
public class WeightedGraph<T> where T : notnull
{
    private readonly Dictionary<T, List<(T Target, double Weight)>> _adjacency = new();

    /// <summary>
    /// Gets the number of vertices.
    /// </summary>
    public int VertexCount => _adjacency.Count;

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
        _adjacency[vertex] = new List<(T, double)>();
        return true;
    }

    /// <summary>
    /// Removes a vertex and all its edges.
    /// </summary>
    public bool RemoveVertex(T vertex)
    {
        if (!_adjacency.Remove(vertex)) return false;

        foreach (var edges in _adjacency.Values)
        {
            edges.RemoveAll(e => EqualityComparer<T>.Default.Equals(e.Target, vertex));
        }
        return true;
    }

    /// <summary>
    /// Adds a weighted directed edge.
    /// </summary>
    public void AddEdge(T from, T to, double weight)
    {
        AddVertex(from);
        AddVertex(to);
        _adjacency[from].Add((to, weight));
    }

    /// <summary>
    /// Adds a weighted undirected edge (both directions).
    /// </summary>
    public void AddUndirectedEdge(T from, T to, double weight)
    {
        AddEdge(from, to, weight);
        AddEdge(to, from, weight);
    }

    /// <summary>
    /// Gets the neighbors of a vertex with weights.
    /// </summary>
    public IReadOnlyList<(T Target, double Weight)> GetNeighbors(T vertex)
    {
        return _adjacency.TryGetValue(vertex, out var neighbors)
            ? neighbors
            : Array.Empty<(T, double)>();
    }

    /// <summary>
    /// Gets all edges.
    /// </summary>
    public IEnumerable<WeightedEdge<T>> GetEdges()
    {
        foreach (var (from, neighbors) in _adjacency)
        {
            foreach (var (to, weight) in neighbors)
            {
                yield return new WeightedEdge<T>(from, to, weight);
            }
        }
    }

    /// <summary>
    /// Dijkstra's algorithm for shortest path.
    /// Returns (distance, path) or null if unreachable.
    /// </summary>
    public (double Distance, IReadOnlyList<T> Path)? ShortestPath(T from, T to)
    {
        if (!_adjacency.ContainsKey(from) || !_adjacency.ContainsKey(to)) return null;

        var distances = new Dictionary<T, double>();
        var parent = new Dictionary<T, T>();
        var visited = new HashSet<T>();

        foreach (var vertex in _adjacency.Keys)
        {
            distances[vertex] = double.PositiveInfinity;
        }
        distances[from] = 0;

        var pq = new PriorityQueue<T, double>();
        pq.Enqueue(from, 0);

        while (pq.Count > 0)
        {
            var current = pq.Dequeue();
            if (!visited.Add(current)) continue;

            if (EqualityComparer<T>.Default.Equals(current, to))
            {
                return (distances[to], ReconstructPath(parent, from, to));
            }

            foreach (var (neighbor, weight) in _adjacency[current])
            {
                if (visited.Contains(neighbor)) continue;

                var newDist = distances[current] + weight;
                if (newDist < distances[neighbor])
                {
                    distances[neighbor] = newDist;
                    parent[neighbor] = current;
                    pq.Enqueue(neighbor, newDist);
                }
            }
        }

        return distances[to] < double.PositiveInfinity
            ? (distances[to], ReconstructPath(parent, from, to))
            : null;
    }

    /// <summary>
    /// Gets shortest distances from a source to all reachable vertices (Dijkstra).
    /// </summary>
    public IReadOnlyDictionary<T, double> ShortestDistances(T from)
    {
        if (!_adjacency.ContainsKey(from)) return new Dictionary<T, double>();

        var distances = new Dictionary<T, double>();
        var visited = new HashSet<T>();

        foreach (var vertex in _adjacency.Keys)
        {
            distances[vertex] = double.PositiveInfinity;
        }
        distances[from] = 0;

        var pq = new PriorityQueue<T, double>();
        pq.Enqueue(from, 0);

        while (pq.Count > 0)
        {
            var current = pq.Dequeue();
            if (!visited.Add(current)) continue;

            foreach (var (neighbor, weight) in _adjacency[current])
            {
                if (visited.Contains(neighbor)) continue;

                var newDist = distances[current] + weight;
                if (newDist < distances[neighbor])
                {
                    distances[neighbor] = newDist;
                    pq.Enqueue(neighbor, newDist);
                }
            }
        }

        return distances;
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
