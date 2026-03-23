using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Structures.Graphs;

/// <summary>
/// Directed graph (digraph) with adjacency list representation.
/// </summary>
/// <typeparam name="T">The vertex value type.</typeparam>
public class DirectedGraph<T> : Graph<T> where T : notnull
{
    /// <summary>
    /// Gets the number of directed edges.
    /// </summary>
    public new int EdgeCount
    {
        get
        {
            int count = 0;
            foreach (var neighbors in _adjacency.Values)
            {
                count += neighbors.Count;
            }
            return count;
        }
    }

    /// <summary>
    /// Adds a directed edge from source to target.
    /// </summary>
    public override bool AddEdge(T from, T to)
    {
        AddVertex(from);
        AddVertex(to);
        return _adjacency[from].Add(to);
    }

    /// <summary>
    /// Removes a directed edge.
    /// </summary>
    public override bool RemoveEdge(T from, T to)
    {
        if (!_adjacency.ContainsKey(from)) return false;
        return _adjacency[from].Remove(to);
    }

    /// <summary>
    /// Gets the in-degree of a vertex (number of edges pointing to it).
    /// </summary>
    public int InDegree(T vertex)
    {
        if (!_adjacency.ContainsKey(vertex)) return 0;

        int count = 0;
        foreach (var neighbors in _adjacency.Values)
        {
            if (neighbors.Contains(vertex)) count++;
        }
        return count;
    }

    /// <summary>
    /// Gets the out-degree of a vertex (number of edges from it).
    /// </summary>
    public int OutDegree(T vertex)
    {
        return _adjacency.TryGetValue(vertex, out var neighbors) ? neighbors.Count : 0;
    }

    /// <summary>
    /// Topological sort using Kahn's algorithm.
    /// Returns null if the graph has a cycle.
    /// </summary>
    public IReadOnlyList<T>? TopologicalSort()
    {
        var inDegree = new Dictionary<T, int>();
        foreach (var vertex in _adjacency.Keys)
        {
            inDegree[vertex] = 0;
        }

        foreach (var neighbors in _adjacency.Values)
        {
            foreach (var neighbor in neighbors)
            {
                inDegree[neighbor]++;
            }
        }

        var queue = new Queue<T>();
        foreach (var (vertex, degree) in inDegree)
        {
            if (degree == 0) queue.Enqueue(vertex);
        }

        var result = new List<T>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);

            foreach (var neighbor in _adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return result.Count == _adjacency.Count ? result : null;
    }

    /// <summary>
    /// Detects if the directed graph contains a cycle.
    /// </summary>
    public bool HasCycle()
    {
        return TopologicalSort() == null;
    }

    /// <summary>
    /// Gets all vertices reachable from the given vertex.
    /// </summary>
    public IReadOnlySet<T> GetReachable(T from)
    {
        var visited = new HashSet<T>();
        foreach (var vertex in DepthFirst(from))
        {
            visited.Add(vertex);
        }
        visited.Remove(from);
        return visited;
    }

    /// <summary>
    /// Reverses all edges in the graph.
    /// </summary>
    public DirectedGraph<T> Reverse()
    {
        var reversed = new DirectedGraph<T>();
        foreach (var vertex in _adjacency.Keys)
        {
            reversed.AddVertex(vertex);
        }

        foreach (var (from, neighbors) in _adjacency)
        {
            foreach (var to in neighbors)
            {
                reversed.AddEdge(to, from);
            }
        }

        return reversed;
    }
}
