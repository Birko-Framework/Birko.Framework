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
        // TASK-444: a NEW table, not a preserved legacy name — the concept had no model, so there is no
        // old schema to stay compatible with. The three above keep their Warehouse-era names on purpose.
        Configure(new StockBalanceMapping()).TableName.Should().Be("StockBalances");
        Configure(new StockMovementMapping()).TableName.Should().Be("StockMovements");
    }

    [Fact]
    public void Mappings_MarkGuidAsPrimaryAndUnique()
    {
        AssertGuidKey(Configure(new StockItemMapping()));
        AssertGuidKey(Configure(new StorageLocationMapping()));
        AssertGuidKey(Configure(new InventoryDocumentLineMapping()));
        AssertGuidKey(Configure(new StockBalanceMapping()));
        AssertGuidKey(Configure(new StockMovementMapping()));

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

    [Fact]
    public void StockBalance_Quantity_HasTheSamePrecisionAsADocumentLine()
    {
        // The reason StockBalance is mapped at all while StockMovement is not: an unmapped decimal takes
        // the provider default, which for several is 18,2 — silently truncating the fractional
        // quantities this domain exists to track. 22,6 here matches InventoryDocumentLine so a quantity
        // survives a line and a balance identically.
        var field = Configure(new StockBalanceMapping()).Properties.Single(p => p.Name == "Quantity");
        field.Precision.Should().Be(22);
        field.Scale.Should().Be(6);
    }

    [Theory]
    [InlineData("Quantity")]
    [InlineData("UnitPrice")]
    public void StockMovement_DecimalColumns_HavePrecisionAndScale(string column)
    {
        // StockMovement was unmapped until TASK-444, so both decimals took the provider default —
        // 18,2 on several, silently truncating fractional quantities and unit prices.
        var field = Configure(new StockMovementMapping()).Properties.Single(p => p.Name == column);
        field.Precision.Should().Be(22);
        field.Scale.Should().Be(6);
    }

    [Theory]
    [InlineData("StockBalances")]
    [InlineData("StockMovements")]
    public void TheNewMappings_DoNotClaimARetiredTableName(string table)
    {
        // Both concepts DID have retired tables — ItemRepositories and ItemRepositoryMovements — but
        // every coordinate was renamed (ItemGuid to StockItemGuid, RepositoryGuid to StorageLocationGuid,
        // AgendaGuid to TenantGuid, Batch to BatchNumber, Amount to Quantity) and a column name defaults
        // to the property name. Reusing either name would promise a drop-in compatibility the columns
        // break. The three legacy names above are kept precisely because their columns did survive.
        table.Should().NotBe("ItemRepositories").And.NotBe("ItemRepositoryMovements");
    }

    [Fact]
    public void StockBalance_BatchNumber_IsBounded()
    {
        Configure(new StockBalanceMapping()).Properties.Single(p => p.Name == "BatchNumber")
            .Precision.Should().NotBe(0);
    }
}
