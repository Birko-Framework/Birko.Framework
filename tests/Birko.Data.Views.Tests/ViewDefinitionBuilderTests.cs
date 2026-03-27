using FluentAssertions;
using Xunit;

namespace Birko.Data.Views.Tests;

public class ViewDefinitionBuilderTests
{
    #region From

    [Fact]
    public void Build_WithoutFrom_Throws()
    {
        var builder = new ViewDefinitionBuilder<CustomerView>();
        builder.Select<Customer, string>(c => c.Name, v => v.CustomerName);

        var act = () => builder.Build();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*From*");
    }

    [Fact]
    public void Build_WithFrom_SetsPrimarySource()
    {
        var builder = new ViewDefinitionBuilder<CustomerView>();
        builder.From<Customer>()
            .Select<Customer, string>(c => c.Name, v => v.CustomerName);

        var def = builder.Build();
        def.PrimarySource.Should().Be(typeof(Customer));
        def.ViewType.Should().Be(typeof(CustomerView));
    }

    #endregion

    #region Select

    [Fact]
    public void Select_AddsFieldSelector()
    {
        var def = new ViewDefinitionBuilder<CustomerView>()
            .From<Customer>()
            .Select<Customer, Guid>(c => c.Guid!.Value, v => v.CustomerId)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .Build();

        def.Fields.Should().HaveCount(2);
        def.Fields[0].SourceType.Should().Be(typeof(Customer));
        def.Fields[0].SourceProperty.Should().Be("Value");
        def.Fields[0].ViewProperty.Should().Be("CustomerId");
        def.Fields[1].SourceProperty.Should().Be("Name");
        def.Fields[1].ViewProperty.Should().Be("CustomerName");
    }

    #endregion

    #region Join

    [Fact]
    public void Join_AddsJoinClause()
    {
        var def = new ViewDefinitionBuilder<CustomerView>()
            .From<Customer>()
            .Join<Customer, Order, Guid>(c => c.Guid!.Value, o => o.CustomerId)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .Build();

        def.Joins.Should().HaveCount(1);
        def.Joins[0].LeftType.Should().Be(typeof(Customer));
        def.Joins[0].RightType.Should().Be(typeof(Order));
        def.Joins[0].JoinType.Should().Be(JoinType.Inner);
        def.HasJoins.Should().BeTrue();
    }

    [Fact]
    public void LeftJoin_SetsLeftOuterType()
    {
        var def = new ViewDefinitionBuilder<CustomerView>()
            .From<Customer>()
            .LeftJoin<Customer, Order, Guid>(c => c.Guid!.Value, o => o.CustomerId)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .Build();

        def.Joins[0].JoinType.Should().Be(JoinType.LeftOuter);
    }

    #endregion

    #region Aggregates

    [Fact]
    public void Count_AddsAggregateClause()
    {
        var def = new ViewDefinitionBuilder<CustomerOrderSummary>()
            .From<Customer>()
            .GroupBy<Customer, Guid>(c => c.Guid!.Value)
            .GroupBy<Customer, string>(c => c.Name)
            .Select<Customer, Guid>(c => c.Guid!.Value, v => v.CustomerId)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .Count<Order>(v => v.OrderCount)
            .Build();

        def.Aggregates.Should().HaveCount(1);
        def.Aggregates[0].Function.Should().Be(AggregateFunction.Count);
        def.Aggregates[0].SourceProperty.Should().BeNull();
        def.Aggregates[0].ViewProperty.Should().Be("OrderCount");
        def.HasAggregates.Should().BeTrue();
    }

    [Fact]
    public void Sum_AddsAggregateClause()
    {
        var def = new ViewDefinitionBuilder<CustomerOrderSummary>()
            .From<Customer>()
            .GroupBy<Customer, Guid>(c => c.Guid!.Value)
            .GroupBy<Customer, string>(c => c.Name)
            .Select<Customer, Guid>(c => c.Guid!.Value, v => v.CustomerId)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .Sum<Order, decimal>(o => o.Total, v => v.TotalSpent)
            .Build();

        def.Aggregates[0].Function.Should().Be(AggregateFunction.Sum);
        def.Aggregates[0].SourceProperty.Should().Be("Total");
    }

    [Fact]
    public void AllAggregateFunctions_Work()
    {
        var def = new ViewDefinitionBuilder<CustomerOrderSummary>()
            .From<Customer>()
            .GroupBy<Customer, Guid>(c => c.Guid!.Value)
            .GroupBy<Customer, string>(c => c.Name)
            .Select<Customer, Guid>(c => c.Guid!.Value, v => v.CustomerId)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .Count<Order>(v => v.OrderCount)
            .Sum<Order, decimal>(o => o.Total, v => v.TotalSpent)
            .Avg<Order, decimal>(o => o.Total, v => v.AvgOrder)
            .Min<Order, decimal>(o => o.Total, v => v.MinOrder)
            .Max<Order, decimal>(o => o.Total, v => v.MaxOrder)
            .Build();

        def.Aggregates.Should().HaveCount(5);
        def.Aggregates.Select(a => a.Function).Should().BeEquivalentTo(new[]
        {
            AggregateFunction.Count,
            AggregateFunction.Sum,
            AggregateFunction.Avg,
            AggregateFunction.Min,
            AggregateFunction.Max
        });
    }

    [Fact]
    public void CountLong_AddsAggregateClause()
    {
        var def = new ViewDefinitionBuilder<OrderStatusView>()
            .From<Order>()
            .GroupBy<Order, string>(o => o.Status)
            .Select<Order, string>(o => o.Status, v => v.Status)
            .Count<Order>(v => v.Count)
            .Build();

        def.Aggregates[0].Function.Should().Be(AggregateFunction.Count);
        def.Aggregates[0].ViewProperty.Should().Be("Count");
    }

    #endregion

    #region GroupBy Validation

    [Fact]
    public void Build_AggregatesWithoutGroupBy_WhenFieldsPresent_Throws()
    {
        var builder = new ViewDefinitionBuilder<CustomerOrderSummary>()
            .From<Customer>()
            .Select<Customer, Guid>(c => c.Guid!.Value, v => v.CustomerId)
            .Count<Order>(v => v.OrderCount);

        var act = () => builder.Build();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*GroupBy*");
    }

    [Fact]
    public void Build_FieldNotInGroupBy_Throws()
    {
        var builder = new ViewDefinitionBuilder<CustomerOrderSummary>()
            .From<Customer>()
            .Select<Customer, Guid>(c => c.Guid!.Value, v => v.CustomerId)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .GroupBy<Customer, Guid>(c => c.Guid!.Value)
            // Missing GroupBy for Name
            .Count<Order>(v => v.OrderCount);

        var act = () => builder.Build();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Name*not in GroupBy*");
    }

    [Fact]
    public void Build_AggregatesWithoutFields_NoGroupByRequired()
    {
        // Pure aggregate (no selected fields, just aggregates) should work without GroupBy
        var def = new ViewDefinitionBuilder<OrderStatusView>()
            .From<Order>()
            .Count<Order>(v => v.Count)
            .Build();

        def.Aggregates.Should().HaveCount(1);
        def.Fields.Should().BeEmpty();
        def.GroupBy.Should().BeEmpty();
    }

    #endregion

    #region Numeric Validation

    [Fact]
    public void Sum_OnNonNumericProperty_Throws()
    {
        var builder = new ViewDefinitionBuilder<BadAggregateView>()
            .From<Order>()
            .Sum<Order, string>(o => o.Status, v => v.TotalAsString);

        var act = () => builder.Build();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*numeric*");
    }

    [Fact]
    public void Avg_OnNonNumericProperty_Throws()
    {
        var builder = new ViewDefinitionBuilder<BadAggregateView>()
            .From<Order>()
            .Avg<Order, string>(o => o.Status, v => v.TotalAsString);

        var act = () => builder.Build();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*numeric*");
    }

    [Fact]
    public void Min_OnStringProperty_Allowed()
    {
        // Min/Max on non-numeric should be allowed (e.g., min date, max string)
        var def = new ViewDefinitionBuilder<BadAggregateView>()
            .From<Order>()
            .Min<Order, string>(o => o.Status, v => v.TotalAsString)
            .Build();

        def.Aggregates.Should().HaveCount(1);
    }

    #endregion

    #region QueryMode & Name

    [Fact]
    public void Build_PersistentWithoutName_Throws()
    {
        var builder = new ViewDefinitionBuilder<CustomerView>()
            .From<Customer>()
            .HasQueryMode(ViewQueryMode.Persistent)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName);

        var act = () => builder.Build();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HasName*");
    }

    [Fact]
    public void Build_AutoWithoutName_Throws()
    {
        var builder = new ViewDefinitionBuilder<CustomerView>()
            .From<Customer>()
            .HasQueryMode(ViewQueryMode.Auto)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName);

        var act = () => builder.Build();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*HasName*");
    }

    [Fact]
    public void Build_OnTheFlyWithoutName_Succeeds()
    {
        var def = new ViewDefinitionBuilder<CustomerView>()
            .From<Customer>()
            .HasQueryMode(ViewQueryMode.OnTheFly)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .Build();

        def.QueryMode.Should().Be(ViewQueryMode.OnTheFly);
        def.Name.Should().BeNull();
    }

    [Fact]
    public void Build_PersistentWithName_Succeeds()
    {
        var def = new ViewDefinitionBuilder<CustomerView>()
            .From<Customer>()
            .HasName("customer_view")
            .HasQueryMode(ViewQueryMode.Persistent)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .Build();

        def.QueryMode.Should().Be(ViewQueryMode.Persistent);
        def.Name.Should().Be("customer_view");
    }

    #endregion

    #region Hints

    [Fact]
    public void Hint_AddsToHintsDictionary()
    {
        var def = new ViewDefinitionBuilder<CustomerView>()
            .From<Customer>()
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .Hint("MaterializedViewType", "PostgreSqlMaterialized")
            .Hint("PartitionKey", "/tenantId")
            .Build();

        def.Hints.Should().HaveCount(2);
        def.Hints["MaterializedViewType"].Should().Be("PostgreSqlMaterialized");
        def.Hints["PartitionKey"].Should().Be("/tenantId");
    }

    #endregion

    #region Full Integration

    [Fact]
    public void Build_FullViewDefinition_ProducesCorrectResult()
    {
        var def = new ViewDefinitionBuilder<CustomerOrderSummary>()
            .HasName("customer_order_summary")
            .HasQueryMode(ViewQueryMode.Auto)
            .From<Customer>()
            .LeftJoin<Customer, Order, Guid>(c => c.Guid!.Value, o => o.CustomerId)
            .Select<Customer, Guid>(c => c.Guid!.Value, v => v.CustomerId)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .GroupBy<Customer, Guid>(c => c.Guid!.Value)
            .GroupBy<Customer, string>(c => c.Name)
            .Count<Order>(v => v.OrderCount)
            .Sum<Order, decimal>(o => o.Total, v => v.TotalSpent)
            .Avg<Order, decimal>(o => o.Total, v => v.AvgOrder)
            .Build();

        def.Name.Should().Be("customer_order_summary");
        def.QueryMode.Should().Be(ViewQueryMode.Auto);
        def.PrimarySource.Should().Be(typeof(Customer));
        def.ViewType.Should().Be(typeof(CustomerOrderSummary));
        def.Fields.Should().HaveCount(2);
        def.Joins.Should().HaveCount(1);
        def.Aggregates.Should().HaveCount(3);
        def.GroupBy.Should().HaveCount(2);
        def.HasAggregates.Should().BeTrue();
        def.HasJoins.Should().BeTrue();
        def.HasGroupBy.Should().BeTrue();
    }

    #endregion
}
