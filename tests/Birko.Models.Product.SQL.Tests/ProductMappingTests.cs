using System.Linq;
using Birko.Models.Product;
using Birko.Models.Product.SQL.Mappings;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Product.SQL.Tests;

/// <summary>
/// CR-L318: the Product.SQL mapping project had no test sibling. These run each Configure() against a
/// fresh ModelMap&lt;T&gt; and assert table names, primary/unique on Guid, MeasureUnit.Code uniqueness,
/// UnitConversion.Factor precision/scale (CR-L317 alignment to 22/6), and string precisions.
/// </summary>
public class ProductMappingTests
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
        Configure(new MeasureUnitMapping()).TableName.Should().Be("MeasureUnits");
        Configure(new UnitConversionMapping()).TableName.Should().Be("UnitConversions");
        Configure(new ProductPartnerCodeMapping()).TableName.Should().Be("ProductPartnerCodes");
    }

    [Fact]
    public void Mappings_MarkGuidAsPrimaryAndUnique()
    {
        AssertGuidKey(Configure(new MeasureUnitMapping()));
        AssertGuidKey(Configure(new UnitConversionMapping()));
        AssertGuidKey(Configure(new ProductPartnerCodeMapping()));

        static void AssertGuidKey<T>(ModelMap<T> map) where T : class
        {
            var guid = map.Properties.Single(p => p.Name == "Guid");
            guid.IsPrimary.Should().BeTrue("Guid must be the primary key");
            guid.IsUnique.Should().BeTrue("Guid must be unique");
        }
    }

    [Fact]
    public void MeasureUnit_Code_IsUniqueAndBounded()
    {
        var code = Configure(new MeasureUnitMapping()).Properties.Single(p => p.Name == "Code");
        code.IsUnique.Should().BeTrue("MeasureUnit.Code is a natural key");
        code.Precision.Should().Be(50);
    }

    [Fact]
    public void UnitConversion_Factor_HasFrameworkDecimalFacet()
    {
        // CR-L317: aligned to the framework 22/6 convention.
        var factor = Configure(new UnitConversionMapping()).Properties.Single(p => p.Name == "Factor");
        factor.Precision.Should().Be(22);
        factor.Scale.Should().Be(6);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("Symbol")]
    public void MeasureUnit_StringColumns_AreBounded(string column)
    {
        Configure(new MeasureUnitMapping()).Properties.Single(p => p.Name == column)
            .Precision.Should().NotBe(0);
    }
}
