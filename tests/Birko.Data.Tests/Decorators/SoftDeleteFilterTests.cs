using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.Tests.Decorators;

public class SoftDeleteFilterTests
{
    private class TestEntity : ISoftDeletable
    {
        public string Name { get; set; } = string.Empty;
        public DateTime? DeletedAt { get; set; }
    }

    [Fact]
    public void CombineWithNotDeleted_NullFilter_ReturnsNotDeletedOnly()
    {
        var filter = SoftDeleteFilter.CombineWithNotDeleted<TestEntity>(null);
        var compiled = filter.Compile();

        var active = new TestEntity { Name = "Active", DeletedAt = null };
        var deleted = new TestEntity { Name = "Deleted", DeletedAt = DateTime.UtcNow };

        compiled(active).Should().BeTrue();
        compiled(deleted).Should().BeFalse();
    }

    [Fact]
    public void CombineWithNotDeleted_WithFilter_CombinesBothConditions()
    {
        var filter = SoftDeleteFilter.CombineWithNotDeleted<TestEntity>(x => x.Name == "Target");
        var compiled = filter.Compile();

        var target = new TestEntity { Name = "Target", DeletedAt = null };
        var deletedTarget = new TestEntity { Name = "Target", DeletedAt = DateTime.UtcNow };
        var other = new TestEntity { Name = "Other", DeletedAt = null };

        compiled(target).Should().BeTrue();
        compiled(deletedTarget).Should().BeFalse();
        compiled(other).Should().BeFalse();
    }
}
