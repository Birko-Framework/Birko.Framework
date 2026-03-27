using FluentAssertions;
using Xunit;

namespace Birko.Data.Views.Tests;

#region Test Mappings

public class CustomerViewMapping : IViewMapping<CustomerView>
{
    public void Configure(ViewDefinitionBuilder<CustomerView> builder)
    {
        builder
            .From<Customer>()
            .Select<Customer, Guid>(c => c.Guid!.Value, v => v.CustomerId)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName);
    }
}

public class CustomerOrderSummaryMapping : IViewMapping<CustomerOrderSummary>
{
    public void Configure(ViewDefinitionBuilder<CustomerOrderSummary> builder)
    {
        builder
            .HasName("customer_order_summary")
            .HasQueryMode(ViewQueryMode.Persistent)
            .From<Customer>()
            .LeftJoin<Customer, Order, Guid>(c => c.Guid!.Value, o => o.CustomerId)
            .Select<Customer, Guid>(c => c.Guid!.Value, v => v.CustomerId)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .GroupBy<Customer, Guid>(c => c.Guid!.Value)
            .GroupBy<Customer, string>(c => c.Name)
            .Count<Order>(v => v.OrderCount)
            .Sum<Order, decimal>(o => o.Total, v => v.TotalSpent);
    }
}

public class ProductCategorySummaryMapping : IViewMapping<ProductCategorySummary>
{
    public void Configure(ViewDefinitionBuilder<ProductCategorySummary> builder)
    {
        builder
            .HasName("product_category_summary")
            .HasQueryMode(ViewQueryMode.Auto)
            .From<Product>()
            .Select<Product, string>(p => p.Category, v => v.Category)
            .GroupBy<Product, string>(p => p.Category)
            .Count<Product>(v => v.ProductCount)
            .Avg<Product, decimal>(p => p.Price, v => v.AvgPrice);
    }
}

#endregion

public class ViewMapRegistryTests
{
    #region Register

    [Fact]
    public void Register_SingleMapping_StoresDefinition()
    {
        var registry = new ViewMapRegistry();
        registry.Register(new CustomerViewMapping());

        registry.HasDefinition<CustomerView>().Should().BeTrue();
        var def = registry.GetDefinition<CustomerView>();
        def.Should().NotBeNull();
        def!.PrimarySource.Should().Be(typeof(Customer));
        def.Fields.Should().HaveCount(2);
    }

    [Fact]
    public void Register_MultipleMappings_StoresAll()
    {
        var registry = new ViewMapRegistry();
        registry.Register(new CustomerViewMapping());
        registry.Register(new CustomerOrderSummaryMapping());

        registry.HasDefinition<CustomerView>().Should().BeTrue();
        registry.HasDefinition<CustomerOrderSummary>().Should().BeTrue();
    }

    [Fact]
    public void GetDefinition_Unregistered_ReturnsNull()
    {
        var registry = new ViewMapRegistry();

        registry.HasDefinition<CustomerView>().Should().BeFalse();
        registry.GetDefinition<CustomerView>().Should().BeNull();
    }

    #endregion

    #region RegisterFromAssembly

    [Fact]
    public void RegisterFromAssembly_DiscoversAllMappings()
    {
        var registry = new ViewMapRegistry();
        registry.RegisterFromAssembly(typeof(CustomerViewMapping).Assembly);

        registry.HasDefinition<CustomerView>().Should().BeTrue();
        registry.HasDefinition<CustomerOrderSummary>().Should().BeTrue();
        registry.HasDefinition<ProductCategorySummary>().Should().BeTrue();
    }

    [Fact]
    public void RegisterFromAssembly_ProducesValidDefinitions()
    {
        var registry = new ViewMapRegistry();
        registry.RegisterFromAssembly(typeof(CustomerOrderSummaryMapping).Assembly);

        var def = registry.GetDefinition<CustomerOrderSummary>();
        def.Should().NotBeNull();
        def!.Name.Should().Be("customer_order_summary");
        def.QueryMode.Should().Be(ViewQueryMode.Persistent);
        def.HasAggregates.Should().BeTrue();
        def.HasJoins.Should().BeTrue();
    }

    #endregion

    #region GetDefinition by Type

    [Fact]
    public void GetDefinition_ByType_Works()
    {
        var registry = new ViewMapRegistry();
        registry.Register(new CustomerViewMapping());

        var def = registry.GetDefinition(typeof(CustomerView));
        def.Should().NotBeNull();
        def!.PrimarySource.Should().Be(typeof(Customer));
    }

    [Fact]
    public void HasDefinition_ByType_Works()
    {
        var registry = new ViewMapRegistry();
        registry.Register(new CustomerViewMapping());

        registry.HasDefinition(typeof(CustomerView)).Should().BeTrue();
        registry.HasDefinition(typeof(ProductCategorySummary)).Should().BeFalse();
    }

    #endregion

    #region GetAll

    [Fact]
    public void GetAll_ReturnsAllRegistered()
    {
        var registry = new ViewMapRegistry();
        registry.Register(new CustomerViewMapping());
        registry.Register(new CustomerOrderSummaryMapping());

        var all = registry.GetAll().ToList();
        all.Should().HaveCount(2);
        all.Select(x => x.Key).Should().Contain(typeof(CustomerView));
        all.Select(x => x.Key).Should().Contain(typeof(CustomerOrderSummary));
    }

    #endregion
}
