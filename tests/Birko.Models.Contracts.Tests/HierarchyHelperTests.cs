using System;
using Birko.Models.Contracts;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Contracts.Tests;

/// <summary>
/// CR-H126: HierarchyHelper.ComputePath must not require Birko.Data.Core's AbstractModel — this test
/// project imports ONLY Birko.Models.Contracts, so the fact it compiles and runs proves the
/// zero-dependency contract holds. Also covers Path/Depth/NamePath computation over a plain
/// IHierarchical / INamedHierarchical implementer.
/// </summary>
public class HierarchyHelperTests
{
    private class Node : IHierarchical
    {
        public Guid? ParentGuid { get; set; }
        public string Path { get; set; } = string.Empty;
        public int Depth { get; set; }
    }

    private sealed class NamedNode : Node, INamedHierarchical
    {
        public string HierarchyName { get; set; } = string.Empty;
        public string NamePath { get; set; } = string.Empty;
    }

    [Fact]
    public void ComputePath_Root_SetsPathAndZeroDepth()
    {
        var id = Guid.NewGuid();
        var node = new Node();

        HierarchyHelper.ComputePath(node, id, parent: null);

        node.Path.Should().Be($"/{id}");
        node.Depth.Should().Be(0);
    }

    [Fact]
    public void ComputePath_Child_AppendsToParentPathAndIncrementsDepth()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parent = new Node();
        HierarchyHelper.ComputePath(parent, parentId, null);

        var child = new Node();
        HierarchyHelper.ComputePath(child, childId, parent);

        child.Path.Should().Be($"/{parentId}/{childId}");
        child.Depth.Should().Be(1);
    }

    [Fact]
    public void ComputePath_NullId_Throws()
    {
        var act = () => HierarchyHelper.ComputePath(new Node(), (Guid?)null, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*id must be set*");
    }

    [Fact]
    public void ComputePath_Named_BuildsNamePath()
    {
        var root = new NamedNode { HierarchyName = "Electronics" };
        HierarchyHelper.ComputePath(root, Guid.NewGuid(), null);

        var child = new NamedNode { HierarchyName = "Phones" };
        HierarchyHelper.ComputePath(child, Guid.NewGuid(), root);

        root.NamePath.Should().Be("Electronics");
        child.NamePath.Should().Be("Electronics/Phones");
    }

    [Fact]
    public void DeriveParentAndDepthFromPath_RoundTripsWithComputePath()
    {
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var parent = new Node();
        HierarchyHelper.ComputePath(parent, parentId, null);
        var child = new Node();
        HierarchyHelper.ComputePath(child, childId, parent);

        // Wipe the derived fields and rebuild them from Path alone.
        child.ParentGuid = null;
        child.Depth = 0;
        HierarchyHelper.DeriveParentAndDepthFromPath(child);

        child.Depth.Should().Be(1);
        child.ParentGuid.Should().Be(parentId);
    }

    // ── CR-M215: RewriteDescendantPaths null-safety ──────

    [Fact]
    public void RewriteDescendantPaths_SkipsNullOrNonMatchingPath_DoesNotThrow()
    {
        var match = new Node { Path = "/old/child" };
        var nullPath = new Node { Path = null! };          // never materialized
        var unrelated = new Node { Path = "/somewhere/else" };

        var act = () => HierarchyHelper.RewriteDescendantPaths(
            new[] { match, nullPath, unrelated }, oldPath: "/old", newPath: "/new");

        act.Should().NotThrow();
        match.Path.Should().Be("/new/child", "a matching descendant is rewritten");
        nullPath.Path.Should().BeNull("a null-Path descendant is skipped, not NRE'd");
        unrelated.Path.Should().Be("/somewhere/else", "a non-matching descendant is left alone");
    }

    [Fact]
    public void RewriteDescendantPaths_NullOldOrNewPath_Throws()
    {
        var nodes = new[] { new Node { Path = "/old/x" } };
        ((Action)(() => HierarchyHelper.RewriteDescendantPaths(nodes, null!, "/new")))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => HierarchyHelper.RewriteDescendantPaths(nodes, "/old", null!)))
            .Should().Throw<ArgumentNullException>();
    }

    // ── IsDescendantOf (CR-L305) ─────────────────────────

    [Fact]
    public void IsDescendantOf_ChildPath_ReturnsTrue()
    {
        var ancestor = new Node { Path = "/a" };
        var descendant = new Node { Path = "/a/b" };

        HierarchyHelper.IsDescendantOf(descendant, ancestor).Should().BeTrue();
    }

    [Fact]
    public void IsDescendantOf_SiblingPrefix_ReturnsFalse()
    {
        // "/ab" is not under "/a" — the separator boundary matters.
        var ancestor = new Node { Path = "/a" };
        var other = new Node { Path = "/ab" };

        HierarchyHelper.IsDescendantOf(other, ancestor).Should().BeFalse();
    }

    [Fact]
    public void IsDescendantOf_SameNode_ReturnsFalse()
    {
        var node = new Node { Path = "/a" };

        HierarchyHelper.IsDescendantOf(node, node).Should().BeFalse();
    }

    [Fact]
    public void IsDescendantOf_NullOrEmptyPath_ReturnsFalse()
    {
        // CR-L305: an un-materialized (null/empty) Path must return false, not NRE.
        var withPath = new Node { Path = "/a" };
        var nullPath = new Node { Path = null! };
        var emptyPath = new Node { Path = "" };

        HierarchyHelper.IsDescendantOf(nullPath, withPath).Should().BeFalse();
        HierarchyHelper.IsDescendantOf(withPath, nullPath).Should().BeFalse();
        HierarchyHelper.IsDescendantOf(emptyPath, withPath).Should().BeFalse();
    }
}
