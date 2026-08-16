using System;
using System.Linq;
using Birko.Models.Contracts;
using Birko.Models.Inventory;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Inventory.Tests;

/// <summary>
/// TASK-444 — <c>Birko.Models.Inventory</c> had no model for "how much of this item is here right now".
/// The retired <c>Warehouse.AbstractItemRepository</c> was an abstract coordinate base whose concrete
/// <c>ItemRepository</c> descendant (coordinates + <c>Amount</c>) was the balance; the migration mapped
/// the abstract base onto <see cref="StockMovement"/> and the balance was lost with it. Two consumers
/// then re-added the concept independently.
///
/// <para>
/// These pin the model's shape as much as its behaviour, because the shape is the deliverable: the
/// natural key, the absence of cost, and that it actually satisfies <see cref="IBatchable"/>.
/// </para>
/// </summary>
public class StockBalanceTests
{
    [Fact]
    public void CopyTo_CopiesOwnFields()
    {
        var src = new StockBalance
        {
            StockItemGuid = Guid.NewGuid(),
            StockItemVariantGuid = Guid.NewGuid(),
            StorageLocationGuid = Guid.NewGuid(),
            Quantity = 12.5m,
            BatchNumber = "L-7742",
            ExpiryDate = new DateTime(2027, 3, 1),
            TenantGuid = Guid.NewGuid(),
        };

        var clone = src.CopyTo(new StockBalance());

        clone.StockItemGuid.Should().Be(src.StockItemGuid);
        clone.StockItemVariantGuid.Should().Be(src.StockItemVariantGuid);
        clone.StorageLocationGuid.Should().Be(src.StorageLocationGuid);
        clone.Quantity.Should().Be(12.5m);
        clone.BatchNumber.Should().Be("L-7742");
        clone.ExpiryDate.Should().Be(new DateTime(2027, 3, 1));
        clone.TenantGuid.Should().Be(src.TenantGuid);
    }

    [Fact]
    public void CopyTo_NullClone_Instantiates()
    {
        var clone = new StockBalance { Quantity = 3m }.CopyTo(null!);

        clone.Should().NotBeNull();
        clone.Quantity.Should().Be(3m);
    }

    [Fact]
    public void LoadFrom_ViewModel_CopiesOwnFields()
    {
        var vm = new ViewModels.StockBalance
        {
            StockItemGuid = Guid.NewGuid(),
            StockItemVariantGuid = Guid.NewGuid(),
            StorageLocationGuid = Guid.NewGuid(),
            Quantity = -4m,
            BatchNumber = "L-1",
            ExpiryDate = new DateTime(2026, 12, 31),
            TenantGuid = Guid.NewGuid(),
        };

        var model = new StockBalance();
        model.LoadFrom(vm);

        model.StockItemGuid.Should().Be(vm.StockItemGuid);
        model.StockItemVariantGuid.Should().Be(vm.StockItemVariantGuid);
        model.StorageLocationGuid.Should().Be(vm.StorageLocationGuid);
        model.Quantity.Should().Be(-4m, "a negative balance is a representable state, not a rejected one");
        model.BatchNumber.Should().Be("L-1");
        model.ExpiryDate.Should().Be(new DateTime(2026, 12, 31));
        model.TenantGuid.Should().Be(vm.TenantGuid);
    }

    [Fact]
    public void LoadFrom_NullViewModel_IsIgnored()
    {
        var model = new StockBalance { Quantity = 9m };

        model.LoadFrom(null!);

        model.Quantity.Should().Be(9m, "a null source must not blank the model");
    }

    [Fact]
    public void Is_batchable_and_the_batch_is_optional()
    {
        // The contract's whole point: batch tracking is available, not mandatory. Assigning through the
        // interface reference is what a non-nullable BatchNumber would have made impossible.
        IBatchable batchable = new StockBalance();

        batchable.BatchNumber = null;
        batchable.ExpiryDate = null;

        batchable.BatchNumber.Should().BeNull();
        batchable.ExpiryDate.Should().BeNull();
    }

    [Fact]
    public void Carries_no_cost_or_valuation_field()
    {
        // Pins the DECISION, not an accident. A balance's value depends on a costing policy (FIFO / LIFO
        // / weighted average), so it is derived rather than stored, and the retired design agreed —
        // ItemRepository carried Amount alone while every price lived on ItemRepositoryMovement. If
        // someone later adds a cost here, this fails and they have to argue the case.
        var names = typeof(StockBalance).GetProperties().Select(p => p.Name).ToArray();

        names.Should().NotContain(n =>
            n.Contains("Cost", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Price", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Value", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_natural_key_is_item_variant_location_batch_within_a_tenant()
    {
        // The coordinate set the retired AbstractItemRepository carried, minus its Repository naming.
        var names = typeof(StockBalance).GetProperties().Select(p => p.Name).ToArray();

        names.Should().Contain(new[]
        {
            nameof(StockBalance.StockItemGuid),
            nameof(StockBalance.StockItemVariantGuid),
            nameof(StockBalance.StorageLocationGuid),
            nameof(StockBalance.BatchNumber),
            nameof(StockBalance.TenantGuid),
        });
        names.Should().NotContain("FromLocationGuid", "a balance sits at one location; two is a movement");
        names.Should().NotContain("ToLocationGuid");
    }
}
