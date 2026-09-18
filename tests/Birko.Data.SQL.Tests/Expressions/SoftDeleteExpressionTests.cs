using Birko.Data.Expressions;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.SQL;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.Tenant.Filters;
using Birko.Data.Tenant.Models;
using FluentAssertions;
using System;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Data.SQL.Tests.Expressions;

[Table("TestEntities")]
public class TestTenantEntity : Birko.Data.Models.AbstractLogModel, ITenant, ISoftDeletable
{
    public Guid TenantGuid { get; set; }
    public string? TenantName { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? OptionalCount { get; set; }
}

/// <summary>
/// Tests that SoftDeleteFilter + Tenant filter expressions are correctly
/// parsed by ParseConditionExpression into SQL conditions.
/// Imported from Symbio.Tests.Unit — validates the "Condition name cannot be null or empty" fix.
/// </summary>
public class SoftDeleteExpressionTests
{
    [Fact]
    public void SoftDeleteFilter_Alone_ProducesIsNullCondition()
    {
        var expr = SoftDeleteFilter.CombineWithNotDeleted<TestTenantEntity>(null);

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();

        conditions.Should().NotBeEmpty();
        var leaf = FindLeafCondition(conditions, "DeletedAt");
        leaf.Should().NotBeNull("DeletedAt IS NULL condition must be present");
        leaf!.Type.Should().Be(ConditionType.IsNull);
    }

    [Fact]
    public void TenantFilter_Alone_ProducesEqualCondition()
    {
        var tenantId = Guid.NewGuid();
        var filter = new ModelByTenant<TestTenantEntity>(tenantId).Filter();
        filter.Should().NotBeNull();

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(filter!).ToList();

        conditions.Should().NotBeEmpty();
        var leaf = FindLeafCondition(conditions, "TenantGuid");
        leaf.Should().NotBeNull("TenantGuid == <guid> condition must be present");
        leaf!.Type.Should().Be(ConditionType.Equal);
    }

    [Fact]
    public void TenantPlusSoftDelete_ProducesAndCondition()
    {
        var tenantId = Guid.NewGuid();
        var tenantFilter = new ModelByTenant<TestTenantEntity>(tenantId).Filter();
        var combined = SoftDeleteFilter.CombineWithNotDeleted(tenantFilter);

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(combined).ToList();

        conditions.Should().NotBeEmpty();
        var tenantLeaf = FindLeafCondition(conditions, "TenantGuid");
        tenantLeaf.Should().NotBeNull("TenantGuid condition must be present");
        tenantLeaf!.Type.Should().Be(ConditionType.Equal);

        var deletedLeaf = FindLeafCondition(conditions, "DeletedAt");
        deletedLeaf.Should().NotBeNull("DeletedAt IS NULL condition must be present");
        deletedLeaf!.Type.Should().Be(ConditionType.IsNull);
    }

    [Fact]
    public void NullableProperty_EqualNull_ProducesIsNull()
    {
        Expression<Func<TestTenantEntity, bool>> expr = x => x.OptionalCount == null;

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();

        var leaf = FindLeafCondition(conditions, "OptionalCount");
        leaf.Should().NotBeNull("OptionalCount IS NULL condition must be present");
        leaf!.Type.Should().Be(ConditionType.IsNull);
    }

    [Fact]
    public void NullableProperty_NotEqualNull_Works()
    {
        Expression<Func<TestTenantEntity, bool>> expr = x => x.DeletedAt != null;

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();
        conditions.Should().NotBeEmpty();
    }

    [Fact]
    public void NullableProperty_NotHasValue_ProducesIsNull()
    {
        Expression<Func<TestTenantEntity, bool>> expr = x => !x.DeletedAt.HasValue;

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();

        var leaf = FindLeafCondition(conditions, "DeletedAt");
        leaf.Should().NotBeNull("DeletedAt IS NULL condition must be present");
        leaf!.Type.Should().Be(ConditionType.IsNull);
    }

    private static Condition? FindLeafCondition(System.Collections.Generic.IEnumerable<Condition> conditions, string name)
    {
        foreach (var c in conditions)
        {
            if (c.Name != null && (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
                || c.Name.EndsWith("." + name, StringComparison.OrdinalIgnoreCase)))
                return c;
            if (c.SubConditions != null)
            {
                var found = FindLeafCondition(c.SubConditions, name);
                if (found != null) return found;
            }
        }
        return null;
    }
}
