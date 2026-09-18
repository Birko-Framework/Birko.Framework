using Birko.Data.Expressions;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Tests.TestResources.Models;
using Birko.Data.Tenant.Filters;
using FluentAssertions;
using System;
using System.Linq;
using System.Linq.Expressions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.Tests.DataBase;

/// <summary>
/// Tests that SoftDeleteFilter + Tenant filter expressions are correctly
/// parsed by ParseConditionExpression into SQL conditions.
/// Reproduces the "Condition name cannot be null or empty" bug.
/// </summary>
public class SoftDeleteExpressionTests
{
    private readonly ITestOutputHelper _output;

    public SoftDeleteExpressionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void SoftDeleteFilter_Alone_ProducesIsNullCondition()
    {
        // Arrange: just the soft-delete filter (no tenant, no user filter)
        var expr = SoftDeleteFilter.CombineWithNotDeleted<TenantSoftDeleteModel>(null);
        DumpExpression("SoftDelete alone", expr);

        // Act
        var conditions = SQL.DataBase.ParseConditionExpression(expr).ToList();

        // Assert: should produce a single IS NULL condition for DeletedAt
        DumpConditions(conditions);
        conditions.Should().NotBeEmpty();
        var leaf = FindLeafCondition(conditions, "DeletedAt");
        leaf.Should().NotBeNull("DeletedAt IS NULL condition must be present");
        leaf!.Type.Should().Be(ConditionType.IsNull);
    }

    [Fact]
    public void TenantFilter_Alone_ProducesEqualCondition()
    {
        // Arrange: just the tenant filter
        var tenantId = Guid.NewGuid();
        var filter = new ModelByTenant<TenantSoftDeleteModel>(tenantId).Filter();
        filter.Should().NotBeNull();
        DumpExpression("Tenant alone", filter!);

        // Act
        var conditions = SQL.DataBase.ParseConditionExpression(filter).ToList();

        // Assert
        DumpConditions(conditions);
        conditions.Should().NotBeEmpty();
        var leaf = FindLeafCondition(conditions, "TenantGuid");
        leaf.Should().NotBeNull("TenantGuid == <guid> condition must be present");
        leaf!.Type.Should().Be(ConditionType.Equal);
    }

    [Fact]
    public void TenantPlusSoftDelete_ProducesAndCondition()
    {
        // Arrange: tenant filter first, then soft-delete wraps it
        // This is the exact flow in the store wrapper chain:
        //   TenantWrapper → creates tenant filter → passes to SoftDeleteWrapper → combines with DeletedAt == null
        var tenantId = Guid.NewGuid();
        var tenantFilter = new ModelByTenant<TenantSoftDeleteModel>(tenantId).Filter();
        var combined = SoftDeleteFilter.CombineWithNotDeleted(tenantFilter);
        DumpExpression("Tenant + SoftDelete", combined);

        // Act
        var conditions = SQL.DataBase.ParseConditionExpression(combined).ToList();

        // Assert: should have both TenantGuid == X AND DeletedAt IS NULL
        DumpConditions(conditions);
        conditions.Should().NotBeEmpty();

        var tenantLeaf = FindLeafCondition(conditions, "TenantGuid");
        tenantLeaf.Should().NotBeNull("TenantGuid == <guid> condition must be present");
        tenantLeaf!.Type.Should().Be(ConditionType.Equal);

        var deletedLeaf = FindLeafCondition(conditions, "DeletedAt");
        deletedLeaf.Should().NotBeNull("DeletedAt IS NULL condition must be present");
        deletedLeaf!.Type.Should().Be(ConditionType.IsNull);
    }

    [Fact]
    public void NullableProperty_EqualNull_ProducesIsNull()
    {
        // Test: x.OptionalCount == null (another nullable property)
        Expression<Func<TenantSoftDeleteModel, bool>> expr = x => x.OptionalCount == null;
        DumpExpression("OptionalCount == null", expr);

        var conditions = SQL.DataBase.ParseConditionExpression(expr).ToList();
        DumpConditions(conditions);

        var leaf = FindLeafCondition(conditions, "OptionalCount");
        leaf.Should().NotBeNull("OptionalCount IS NULL condition must be present");
        leaf!.Type.Should().Be(ConditionType.IsNull);
    }

    [Fact]
    public void NullableProperty_NotNull_ProducesIsNotNull()
    {
        // Test: x.DeletedAt != null
        Expression<Func<TenantSoftDeleteModel, bool>> expr = x => x.DeletedAt != null;
        DumpExpression("DeletedAt != null", expr);

        var conditions = SQL.DataBase.ParseConditionExpression(expr).ToList();
        DumpConditions(conditions);
        conditions.Should().NotBeEmpty();
    }

    [Fact]
    public void NullableProperty_HasValue_ProducesIsNotNull()
    {
        // Test: x.DeletedAt.HasValue (bare HasValue = IS NOT NULL)
        Expression<Func<TenantSoftDeleteModel, bool>> expr = x => x.DeletedAt.HasValue;
        DumpExpression("DeletedAt.HasValue", expr);

        var conditions = SQL.DataBase.ParseConditionExpression(expr).ToList();
        DumpConditions(conditions);
        conditions.Should().NotBeEmpty();
    }

    [Fact]
    public void NullableProperty_NotHasValue_ProducesIsNull()
    {
        // Test: !x.DeletedAt.HasValue (Not + HasValue = IS NULL)
        Expression<Func<TenantSoftDeleteModel, bool>> expr = x => !x.DeletedAt.HasValue;
        DumpExpression("!DeletedAt.HasValue", expr);

        var conditions = SQL.DataBase.ParseConditionExpression(expr).ToList();
        DumpConditions(conditions);

        var leaf = FindLeafCondition(conditions, "DeletedAt");
        leaf.Should().NotBeNull("DeletedAt IS NULL condition must be present");
        leaf!.Type.Should().Be(ConditionType.IsNull);
    }

    // --- Helpers ---

    private void DumpExpression(string label, LambdaExpression expr)
    {
        _output.WriteLine($"=== {label} ===");
        _output.WriteLine($"  Body type: {expr.Body.NodeType}");
        _output.WriteLine($"  Body: {expr.Body}");
        DumpExpressionTree(expr.Body, "  ");
    }

    private void DumpExpressionTree(Expression expr, string indent)
    {
        switch (expr)
        {
            case BinaryExpression bin:
                _output.WriteLine($"{indent}Binary({bin.NodeType}):");
                DumpExpressionTree(bin.Left, indent + "  L: ");
                DumpExpressionTree(bin.Right, indent + "  R: ");
                break;
            case UnaryExpression un:
                _output.WriteLine($"{indent}Unary({un.NodeType}):");
                DumpExpressionTree(un.Operand, indent + "  ");
                break;
            case MemberExpression mem:
                _output.WriteLine($"{indent}Member({mem.Member.Name}, ReflectedType={mem.Member.ReflectedType?.Name}, ExprNodeType={mem.Expression?.NodeType})");
                if (mem.Expression != null && mem.Expression.NodeType != ExpressionType.Parameter)
                    DumpExpressionTree(mem.Expression, indent + "  ");
                break;
            case ConstantExpression c:
                _output.WriteLine($"{indent}Constant({c.Value ?? "null"}, Type={c.Type.Name})");
                break;
            case ParameterExpression p:
                _output.WriteLine($"{indent}Param({p.Name}, Type={p.Type.Name})");
                break;
            default:
                _output.WriteLine($"{indent}{expr.NodeType}({expr.GetType().Name})");
                break;
        }
    }

    private void DumpConditions(System.Collections.Generic.List<Condition> conditions, string indent = "  ")
    {
        foreach (var c in conditions)
        {
            _output.WriteLine($"{indent}Condition: Name={c.Name ?? "NULL"}, Type={c.Type}, IsOr={c.IsOr}, IsNot={c.IsNot}, " +
                $"Values=[{string.Join(",", c.Values?.Cast<object>().Select(v => v?.ToString() ?? "null") ?? [])}], " +
                $"SubCount={c.SubConditions?.Count() ?? 0}");
            if (c.SubConditions?.Any() == true)
                DumpConditions(c.SubConditions.ToList(), indent + "  ");
        }
    }

    private static Condition? FindLeafCondition(System.Collections.Generic.IEnumerable<Condition> conditions, string name)
    {
        foreach (var c in conditions)
        {
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
                || c.Name?.EndsWith("." + name, StringComparison.OrdinalIgnoreCase) == true)
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
