using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Models.Inventory;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Inventory.Tests;

/// <summary>
/// CR-L309: StockMovement, InventoryDocument, InventoryDocumentLine and StorageLocation now implement
/// ICopyable&lt;T&gt; with a typed CopyTo (previously they inherited only the base CopyTo, which copies no
/// domain fields — cloning silently dropped everything). These pin the field-copy behavior.
/// </summary>
public class InventoryCopyToTests
{
    [Fact]
    public void StorageLocation_CopyTo_CopiesOwnFields()
    {
        var src = new StorageLocation
        {
            Title = "Bin A1", SortOrder = 5, ParentGuid = Guid.NewGuid(),
            Path = "/a/b", Depth = 2, TenantGuid = Guid.NewGuid()
        };

        var clone = src.CopyTo(new StorageLocation());

        clone.Title.Should().Be("Bin A1");
        clone.SortOrder.Should().Be(5);
        clone.ParentGuid.Should().Be(src.ParentGuid);
        clone.Path.Should().Be("/a/b");
        clone.Depth.Should().Be(2);
        clone.TenantGuid.Should().Be(src.TenantGuid);
    }

    [Fact]
    public void StorageLocation_CopyTo_NullClone_Instantiates()
    {
        var src = new StorageLocation { Title = "X" };

        var clone = src.CopyTo(null!);

        clone.Should().NotBeNull();
        clone.Title.Should().Be("X");
    }

    [Fact]
    public void StockMovement_CopyTo_CopiesOwnFields()
    {
        var src = new StockMovement
        {
            StockItemGuid = Guid.NewGuid(), Quantity = 7m, UnitPrice = 3.5m,
            Batch = "B1", MovementDate = new DateTime(2026, 1, 1), TenantGuid = Guid.NewGuid()
        };

        var clone = src.CopyTo(new StockMovement());

        clone.StockItemGuid.Should().Be(src.StockItemGuid);
        clone.Quantity.Should().Be(7m);
        clone.UnitPrice.Should().Be(3.5m);
        clone.Batch.Should().Be("B1");
        clone.MovementDate.Should().Be(new DateTime(2026, 1, 1));
        clone.TenantGuid.Should().Be(src.TenantGuid);
    }

    [Fact]
    public void InventoryDocumentLine_CopyTo_CopiesOwnFields()
    {
        var src = new InventoryDocumentLine
        {
            Name = "Item", Quantity = 2m, UnitPrice = 10m, TotalPrice = 20m,
            InventoryDocumentGuid = Guid.NewGuid()
        };

        var clone = src.CopyTo(new InventoryDocumentLine());

        clone.Name.Should().Be("Item");
        clone.Quantity.Should().Be(2m);
        clone.UnitPrice.Should().Be(10m);
        clone.TotalPrice.Should().Be(20m);
        clone.InventoryDocumentGuid.Should().Be(src.InventoryDocumentGuid);
    }

    [Fact]
    public void InventoryDocument_CopyTo_CopiesFieldsAndDeepCopiesLines()
    {
        var src = new InventoryDocument
        {
            DocumentNumber = "DOC-1", Status = "Open", DocumentType = InventoryDocumentType.Receipt,
            CurrencySymbol = "EUR", TenantGuid = Guid.NewGuid(),
            Lines = new List<InventoryDocumentLine> { new() { Name = "L1", Quantity = 3m } }
        };

        var clone = src.CopyTo(new InventoryDocument());

        clone.DocumentNumber.Should().Be("DOC-1");
        clone.Status.Should().Be("Open");
        clone.DocumentType.Should().Be(InventoryDocumentType.Receipt);
        clone.CurrencySymbol.Should().Be("EUR");
        clone.TenantGuid.Should().Be(src.TenantGuid);
        clone.Lines.Should().HaveCount(1);
        clone.Lines.First().Name.Should().Be("L1");
        clone.Lines.First().Should().NotBeSameAs(src.Lines.First(), "lines are deep-copied, not aliased");
    }
}
