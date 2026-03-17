using Birko.Data.Aggregates.Core;

namespace Birko.Data.Aggregates.Tests.TestResources;

public class ProductAggregate : AggregateDefinition<Product>
{
    public ProductAggregate()
    {
        HasMany(p => p.Categories)
            .Through<ProductCategory>(j => j.ProductGuid, j => j.CategoryGuid);

        HasMany(p => p.Tags)
            .Via(t => t.ProductGuid);

        HasOne(p => p.DefaultImage)
            .Via(i => i.ProductGuid);
    }
}

public class OrderAggregate : AggregateDefinition<Order>
{
    public OrderAggregate()
    {
        HasMany(o => o.Lines)
            .Via(l => l.OrderGuid);

        HasOne(o => o.Customer)
            .Via(c => c.OrderGuid);
    }
}
