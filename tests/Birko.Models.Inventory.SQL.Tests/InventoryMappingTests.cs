using System.Linq;
using Birko.Models.Inventory;
using Birko.Models.Inventory.SQL.Mappings;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Inventory.SQL.Tests;

/// <summary>
/// CR-L310: the Inventory.SQL mapping project had no test sibling. These run each Configure() against a
/// fresh ModelMap&lt;T&gt; and assert table names, primary/unique on Guid, decimal precision/scale, and
/// string precisions — catching silent drift if a model property is renamed.
/// </summary>
public class InventoryMappingTests
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
        Configure(new StockItemMapping()).TableName.Should().Be("Items");
        Configure(new StorageLocationMapping()).TableName.Should().Be("Repositories");
        Configure(new InventoryDocumentLineMapping()).TableName.Should().Be("WareHouseDocumentItems");
    }

    [Fact]
    public void Mappings_MarkGuidAsPrimaryAndUnique()
    {
        AssertGuidKey(Configure(new StockItemMapping()));
        AssertGuidKey(Configure(new StorageLocationMapping()));
        AssertGuidKey(Configure(new InventoryDocumentLineMapping()));

        static void AssertGuidKey<T>(ModelMap<T> map) where T : class
        {
            var guid = map.Properties.Single(p => p.Name == "Guid");
            guid.IsPrimary.Should().BeTrue("Guid must be the primary key");
            guid.IsUnique.Should().BeTrue("Guid must be unique");
        }
    }

    [Theory]
    [InlineData("Quantity")]
    [InlineData("UnitPrice")]
    [InlineData("UnitPriceVAT")]
    [InlineData("VAT")]
    [InlineData("TotalPrice")]
    [InlineData("TotalPriceVAT")]
    public void InventoryDocumentLine_DecimalColumns_HavePrecisionAndScale(string column)
    {
        var field = Configure(new InventoryDocumentLineMapping()).Properties.Single(p => p.Name == column);
        field.Precision.Should().Be(22);
        field.Scale.Should().Be(6);
    }

    [Theory]
    [InlineData("Code")]
    [InlineData("BarCode")]
    [InlineData("Name")]
    [InlineData("ShortName")]
    [InlineData("Type")]
    public void StockItem_StringColumns_AreBounded(string column)
    {
        Configure(new StockItemMapping()).Properties.Single(p => p.Name == column)
            .Precision.Should().NotBe(0);
    }
}
