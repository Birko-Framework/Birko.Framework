using Birko.Data.Aggregates.Tests.TestResources;
using Birko.Helpers;
using FluentAssertions;
using System;
using System.Linq;
using Xunit;

namespace Birko.Data.Aggregates.Tests;

public class DiffByKeyTests
{
    private static readonly Guid Guid1 = Guid.NewGuid();
    private static readonly Guid Guid2 = Guid.NewGuid();
    private static readonly Guid Guid3 = Guid.NewGuid();

    // --- Empty collections ---

    [Fact]
    public void DiffByKey_BothEmpty_AllEmpty()
    {
        var result = EnumerableHelper.DiffByKey(
            Array.Empty<Tag>(),
            Array.Empty<Tag>(),
            t => t.Guid);

        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Unchanged.Should().BeEmpty();
    }

    // --- All added ---

    [Fact]
    public void DiffByKey_CurrentEmpty_AllAdded()
    {
        var desired = new Tag[]
        {
            new() { Guid = Guid1, Name = "a" },
            new() { Guid = Guid2, Name = "b" }
        };

        var result = EnumerableHelper.DiffByKey(Array.Empty<Tag>(), desired, t => t.Guid);

        result.Added.Should().HaveCount(2);
        result.Removed.Should().BeEmpty();
        result.Unchanged.Should().BeEmpty();
    }

    // --- All removed ---

    [Fact]
    public void DiffByKey_DesiredEmpty_AllRemoved()
    {
        var current = new Tag[]
        {
            new() { Guid = Guid1, Name = "a" },
            new() { Guid = Guid2, Name = "b" }
        };

        var result = EnumerableHelper.DiffByKey(current, Array.Empty<Tag>(), t => t.Guid);

        result.Added.Should().BeEmpty();
        result.Removed.Should().HaveCount(2);
        result.Unchanged.Should().BeEmpty();
    }

    // --- All unchanged ---

    [Fact]
    public void DiffByKey_SameKeys_AllUnchanged()
    {
        var current = new Tag[] { new() { Guid = Guid1 }, new() { Guid = Guid2 } };
        var desired = new Tag[] { new() { Guid = Guid1 }, new() { Guid = Guid2 } };

        var result = EnumerableHelper.DiffByKey(current, desired, t => t.Guid);

        result.Added.Should().BeEmpty();
        result.Removed.Should().BeEmpty();
        result.Unchanged.Should().HaveCount(2);
    }

    // --- Mixed ---

    [Fact]
    public void DiffByKey_Mixed_CorrectlyCategorizes()
    {
        var current = new Tag[]
        {
            new() { Guid = Guid1, Name = "keep" },
            new() { Guid = Guid2, Name = "remove" }
        };
        var desired = new Tag[]
        {
            new() { Guid = Guid1, Name = "keep" },
            new() { Guid = Guid3, Name = "add" }
        };

        var result = EnumerableHelper.DiffByKey(current, desired, t => t.Guid);

        result.Added.Should().HaveCount(1);
        result.Added[0].Guid.Should().Be(Guid3);
        result.Removed.Should().HaveCount(1);
        result.Removed[0].Guid.Should().Be(Guid2);
        result.Unchanged.Should().HaveCount(1);
        result.Unchanged[0].Guid.Should().Be(Guid1);
    }

    // --- Null key handling ---

    [Fact]
    public void DiffByKey_NullKeys_AreExcluded()
    {
        var current = new Tag[] { new() { Guid = null, Name = "no-guid" } };
        var desired = new Tag[] { new() { Guid = Guid1, Name = "has-guid" } };

        var result = EnumerableHelper.DiffByKey(current, desired, t => t.Guid);

        result.Added.Should().HaveCount(1);
        result.Removed.Should().BeEmpty();
        result.Unchanged.Should().BeEmpty();
    }

    // --- Custom key selector (non-Guid) ---

    [Fact]
    public void DiffByKey_WithStringKey_Works()
    {
        var current = new Tag[] { new() { Guid = Guid1, Name = "alpha" }, new() { Guid = Guid2, Name = "beta" } };
        var desired = new Tag[] { new() { Guid = Guid3, Name = "beta" }, new() { Guid = Guid1, Name = "gamma" } };

        var result = EnumerableHelper.DiffByKey(current, desired, t => t.Name);

        result.Added.Should().HaveCount(1);
        result.Added[0].Name.Should().Be("gamma");
        result.Removed.Should().HaveCount(1);
        result.Removed[0].Name.Should().Be("alpha");
        result.Unchanged.Should().HaveCount(1);
        result.Unchanged[0].Name.Should().Be("beta");
    }

    // --- Filter parameter ---

    [Fact]
    public void DiffByKey_WithFilter_ExcludesFilteredItems()
    {
        var current = new Tag[]
        {
            new() { Guid = Guid1, Name = "active" },
            new() { Guid = Guid2, Name = "inactive" }
        };
        var desired = new Tag[]
        {
            new() { Guid = Guid3, Name = "active-new" }
        };

        // Only diff items whose Name starts with "active"
        var result = EnumerableHelper.DiffByKey(
            current, desired,
            t => t.Guid,
            filter: t => t.Name.StartsWith("active"));

        result.Added.Should().HaveCount(1);
        result.Removed.Should().HaveCount(1);
        result.Removed[0].Guid.Should().Be(Guid1); // "inactive" filtered out, not in removed
        result.Unchanged.Should().BeEmpty();
    }

    // --- Integer key ---

    [Fact]
    public void DiffByKey_WithIntKey_Works()
    {
        var current = new[] { (id: 1, val: "a"), (id: 2, val: "b") };
        var desired = new[] { (id: 2, val: "b"), (id: 3, val: "c") };

        var result = EnumerableHelper.DiffByKey(current, desired, t => (int?)t.id);

        result.Added.Should().HaveCount(1);
        result.Added[0].id.Should().Be(3);
        result.Removed.Should().HaveCount(1);
        result.Removed[0].id.Should().Be(1);
        result.Unchanged.Should().HaveCount(1);
    }
}
