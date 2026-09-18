using Birko.Data.Patterns.Specification;
using FluentAssertions;
using System;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Data.Tests.Specification;

public class SpecificationTests
{
    #region Test Infrastructure

    private class Product
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public bool IsActive { get; set; }
    }

    private class ActiveSpec : Specification<Product>
    {
        public override Expression<Func<Product, bool>> ToExpression()
            => p => p.IsActive;
    }

    private class PriceAboveSpec : Specification<Product>
    {
        private readonly decimal _minPrice;
        public PriceAboveSpec(decimal minPrice) => _minPrice = minPrice;

        public override Expression<Func<Product, bool>> ToExpression()
            => p => p.Price > _minPrice;
    }

    private class NameContainsSpec : Specification<Product>
    {
        private readonly string _term;
        public NameContainsSpec(string term) => _term = term;

        public override Expression<Func<Product, bool>> ToExpression()
            => p => p.Name.Contains(_term);
    }

    #endregion

    [Fact]
    public void IsSatisfiedBy_MatchingEntity_ReturnsTrue()
    {
        var spec = new ActiveSpec();
        var product = new Product { IsActive = true };

        spec.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void IsSatisfiedBy_NonMatchingEntity_ReturnsFalse()
    {
        var spec = new ActiveSpec();
        var product = new Product { IsActive = false };

        spec.IsSatisfiedBy(product).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_CanBeCompiledAndUsed()
    {
        var spec = new PriceAboveSpec(10m);
        var expr = spec.ToExpression();
        var compiled = expr.Compile();

        compiled(new Product { Price = 15m }).Should().BeTrue();
        compiled(new Product { Price = 5m }).Should().BeFalse();
    }

    [Fact]
    public void And_BothTrue_ReturnsTrue()
    {
        var active = new ActiveSpec();
        var expensive = new PriceAboveSpec(10m);
        var combined = active.And(expensive);

        var product = new Product { IsActive = true, Price = 20m };
        combined.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void And_OneFalse_ReturnsFalse()
    {
        var active = new ActiveSpec();
        var expensive = new PriceAboveSpec(10m);
        var combined = active.And(expensive);

        var product = new Product { IsActive = true, Price = 5m };
        combined.IsSatisfiedBy(product).Should().BeFalse();
    }

    [Fact]
    public void Or_OneTrue_ReturnsTrue()
    {
        var active = new ActiveSpec();
        var expensive = new PriceAboveSpec(100m);
        var combined = active.Or(expensive);

        var product = new Product { IsActive = true, Price = 5m };
        combined.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void Or_BothFalse_ReturnsFalse()
    {
        var active = new ActiveSpec();
        var expensive = new PriceAboveSpec(100m);
        var combined = active.Or(expensive);

        var product = new Product { IsActive = false, Price = 5m };
        combined.IsSatisfiedBy(product).Should().BeFalse();
    }

    [Fact]
    public void Not_Negates()
    {
        var active = new ActiveSpec();
        var inactive = active.Not();

        var product = new Product { IsActive = true };
        inactive.IsSatisfiedBy(product).Should().BeFalse();

        var inactiveProduct = new Product { IsActive = false };
        inactive.IsSatisfiedBy(inactiveProduct).Should().BeTrue();
    }

    [Fact]
    public void Operators_AndOperator_Works()
    {
        var active = new ActiveSpec();
        var expensive = new PriceAboveSpec(10m);
        var combined = active & expensive;

        combined.IsSatisfiedBy(new Product { IsActive = true, Price = 20m }).Should().BeTrue();
        combined.IsSatisfiedBy(new Product { IsActive = false, Price = 20m }).Should().BeFalse();
    }

    [Fact]
    public void Operators_OrOperator_Works()
    {
        var active = new ActiveSpec();
        var expensive = new PriceAboveSpec(100m);
        var combined = active | expensive;

        combined.IsSatisfiedBy(new Product { IsActive = true, Price = 5m }).Should().BeTrue();
        combined.IsSatisfiedBy(new Product { IsActive = false, Price = 200m }).Should().BeTrue();
        combined.IsSatisfiedBy(new Product { IsActive = false, Price = 5m }).Should().BeFalse();
    }

    [Fact]
    public void Operators_NotOperator_Works()
    {
        var active = new ActiveSpec();
        var inactive = !active;

        inactive.IsSatisfiedBy(new Product { IsActive = true }).Should().BeFalse();
        inactive.IsSatisfiedBy(new Product { IsActive = false }).Should().BeTrue();
    }

    [Fact]
    public void ComplexComposition_Works()
    {
        // Active AND (Price > 10 OR Name contains "Premium")
        var active = new ActiveSpec();
        var expensive = new PriceAboveSpec(10m);
        var premium = new NameContainsSpec("Premium");
        var combined = active.And(expensive.Or(premium));

        combined.IsSatisfiedBy(new Product { IsActive = true, Price = 20m, Name = "Basic" }).Should().BeTrue();
        combined.IsSatisfiedBy(new Product { IsActive = true, Price = 5m, Name = "Premium Widget" }).Should().BeTrue();
        combined.IsSatisfiedBy(new Product { IsActive = true, Price = 5m, Name = "Basic" }).Should().BeFalse();
        combined.IsSatisfiedBy(new Product { IsActive = false, Price = 20m, Name = "Premium" }).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_And_ProducesValidExpression()
    {
        var active = new ActiveSpec();
        var expensive = new PriceAboveSpec(10m);
        var combined = active.And(expensive);

        var expr = combined.ToExpression();
        var compiled = expr.Compile();

        compiled(new Product { IsActive = true, Price = 20m }).Should().BeTrue();
        compiled(new Product { IsActive = true, Price = 5m }).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_CanBeUsedWithLinq()
    {
        var spec = new PriceAboveSpec(10m);
        var products = new[]
        {
            new Product { Name = "A", Price = 5m },
            new Product { Name = "B", Price = 15m },
            new Product { Name = "C", Price = 25m }
        };

        var expr = spec.ToExpression();
        var result = products.AsQueryable().Where(expr).ToList();

        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Name == "B");
        result.Should().Contain(p => p.Name == "C");
    }

    [Fact]
    public void IsSatisfiedBy_CachesCompiledExpression()
    {
        var spec = new ActiveSpec();
        var product = new Product { IsActive = true };

        // Call twice — second should use cached compiled expression
        spec.IsSatisfiedBy(product).Should().BeTrue();
        spec.IsSatisfiedBy(product).Should().BeTrue();
    }

    [Fact]
    public void And_NullArgument_Throws()
    {
        var spec = new ActiveSpec();
        var act = () => new AndSpecification<Product>(spec, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Or_NullArgument_Throws()
    {
        var spec = new ActiveSpec();
        var act = () => new OrSpecification<Product>(spec, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Not_NullArgument_Throws()
    {
        var act = () => new NotSpecification<Product>(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
