using System.Linq;
using Birko.Models.Pricing;
using Birko.Models.Pricing.SQL.Mappings;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Pricing.SQL.Tests;

/// <summary>
/// CR-L314: the Pricing.SQL mapping project had no test sibling. These run each Configure() against a
/// fresh ModelMap&lt;T&gt; and assert table names, primary/unique on Guid, Currency.Code uniqueness,
/// decimal precision/scale on the Percentage columns, and string precisions.
/// </summary>
public class PricingMappingTests
{
    private static ModelMap<T> Configure<T>(IModelMapping<T> mapping) where T : class
    {
        var map = new ModelMap<T>();
        mapping.Configure(map);
        return map;
    }

    [Fact]
    public void Mappings_HaveExpectedTableNames()
    {
        Configure(new CurrencyMapping()).TableName.Should().Be("Currencies");
        Configure(new TaxMapping()).TableName.Should().Be("Taxes");
        Configure(new PriceGroupMapping()).TableName.Should().Be("PriceGroups");
    }

    [Fact]
    public void Mappings_MarkGuidAsPrimaryAndUnique()
    {
        AssertGuidKey(Configure(new CurrencyMapping()));
        AssertGuidKey(Configure(new TaxMapping()));
        AssertGuidKey(Configure(new PriceGroupMapping()));

        static void AssertGuidKey<T>(ModelMap<T> map) where T : class
        {
            var guid = map.Properties.Single(p => p.Name == "Guid");
            guid.IsPrimary.Should().BeTrue("Guid must be the primary key");
            guid.IsUnique.Should().BeTrue("Guid must be unique");
        }
    }

    [Fact]
    public void Currency_Code_IsUniqueAndBounded()
    {
        var code = Configure(new CurrencyMapping()).Properties.Single(p => p.Name == "Code");
        code.IsUnique.Should().BeTrue("Currency.Code is a natural key");
        code.Precision.Should().Be(8);
    }

    [Fact]
    public void PercentageColumns_HavePrecisionAndScale()
    {
        var tax = Configure(new TaxMapping()).Properties.Single(p => p.Name == "Percentage");
        tax.Precision.Should().Be(22);
        tax.Scale.Should().Be(6);

        var priceGroup = Configure(new PriceGroupMapping()).Properties.Single(p => p.Name == "Percentage");
        priceGroup.Precision.Should().Be(22);
        priceGroup.Scale.Should().Be(6);
    }

    [Fact]
    public void StringColumns_AreBounded()
    {
        Configure(new CurrencyMapping()).Properties.Single(p => p.Name == "Name").Precision.Should().Be(256);
        Configure(new CurrencyMapping()).Properties.Single(p => p.Name == "Symbol").Precision.Should().Be(8);
        Configure(new TaxMapping()).Properties.Single(p => p.Name == "ShortCut").Precision.Should().Be(16);
        Configure(new PriceGroupMapping()).Properties.Single(p => p.Name == "Name").Precision.Should().Be(256);
    }
}
