using Birko.Structures.Graphs;
using FluentAssertions;
using Xunit;

namespace Birko.Structures.Tests.Graphs;

public class GraphTests
{
    [Fact]
    public void AddEdge_CreatesVerticesAndEdge()
    {
        var g = new Graph<string>();
        g.AddEdge("A", "B");
        g.VertexCount.Should().Be(2);
        g.EdgeCount.Should().Be(1);
        g.HasEdge("A", "B").Should().BeTrue();
        g.HasEdge("B", "A").Should().BeTrue();
    }

    [Fact]
    public void ShortestPath_FindsPath()
    {
        var g = new Graph<string>();
        g.AddEdge("A", "B");
        g.AddEdge("B", "C");
        g.AddEdge("A", "D");
        g.AddEdge("D", "C");

        var path = g.ShortestPath("A", "C");
        path.Should().NotBeNull();
        path!.Count.Should().Be(3);
        path[0].Should().Be("A");
        path[^1].Should().Be("C");
    }

    [Fact]
    public void BreadthFirst_VisitsAllConnected()
    {
        var g = new Graph<int>();
        g.AddEdge(1, 2);
        g.AddEdge(2, 3);
        g.AddEdge(1, 4);

        var visited = g.BreadthFirst(1).ToList();
        visited.Should().HaveCount(4);
        visited.Should().Contain(new[] { 1, 2, 3, 4 });
    }

    [Fact]
    public void IsConnected_ReturnsTrueForConnectedGraph()
    {
        var g = new Graph<int>();
        g.AddEdge(1, 2);
        g.AddEdge(2, 3);
        g.IsConnected().Should().BeTrue();
    }

    [Fact]
    public void IsConnected_ReturnsFalseForDisconnected()
    {
        var g = new Graph<int>();
        g.AddVertex(1);
        g.AddVertex(2);
        g.IsConnected().Should().BeFalse();
    }
}

public class DirectedGraphTests
{
    [Fact]
    public void TopologicalSort_ReturnsValidOrder()
    {
        var g = new DirectedGraph<string>();
        g.AddEdge("compile", "link");
        g.AddEdge("link", "deploy");

        var order = g.TopologicalSort();
        order.Should().NotBeNull();
        order!.Should().ContainInOrder("compile", "link", "deploy");
    }

    [Fact]
    public void HasCycle_DetectsCycle()
    {
        var g = new DirectedGraph<int>();
        g.AddEdge(1, 2);
        g.AddEdge(2, 3);
        g.AddEdge(3, 1);

        g.HasCycle().Should().BeTrue();
    }

    [Fact]
    public void HasCycle_ReturnsFalseForDag()
    {
        var g = new DirectedGraph<int>();
        g.AddEdge(1, 2);
        g.AddEdge(1, 3);
        g.AddEdge(2, 3);

        g.HasCycle().Should().BeFalse();
    }
}

public class WeightedGraphTests
{
    [Fact]
    public void ShortestPath_FindsOptimalRoute()
    {
        var g = new WeightedGraph<string>();
        g.AddEdge("A", "B", 1);
        g.AddEdge("B", "C", 2);
        g.AddEdge("A", "C", 10);

        var result = g.ShortestPath("A", "C");
        result.Should().NotBeNull();
        result!.Value.Distance.Should().Be(3);
        result.Value.Path.Should().ContainInOrder("A", "B", "C");
    }

    [Fact]
    public void DirectedGraph_EdgeCount_CorrectViaBaseReference()
    {
        // CR-M250: EdgeCount was `new` (shadowing), so a Graph<T> reference to a DirectedGraph got the
        // base's undirected count/2. It's now virtual+override, so both references agree.
        var dg = new DirectedGraph<string>();
        dg.AddEdge("A", "B");
        dg.AddEdge("A", "C");
        dg.AddEdge("B", "C");

        dg.EdgeCount.Should().Be(3, "three directed edges");
        Graph<string> baseRef = dg;
        baseRef.EdgeCount.Should().Be(3, "the override must win through a base-typed reference (CR-M250)");
    }
}
