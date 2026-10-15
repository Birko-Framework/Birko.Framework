using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InMemory.Tests;

/// <summary>
/// TASK-170, the medium findings in <c>bulk-filter-operations</c>, on the portable bulk path (the one every backend
/// without a native override inherits). The in-memory store is the canonical portable backend.
/// </summary>
public class BulkFilterTriageTests
{
    public class Item : AbstractModel
    {
        public int A { get; set; }
        public Owner Child { get; set; } = new();
    }

    public class Owner
    {
        public int A { get; set; }
    }

    /// <summary>Counts single-entity writes, which is what an empty update must not cause.</summary>
    private sealed class CountingStore : InMemoryStore<Item>
    {
        public int Writes { get; private set; }

        protected override void UpdateCore(Item data, StoreDataDelegate<Item>? storeDelegate = null)
        {
            Writes++;
            base.UpdateCore(data, storeDelegate);
        }
    }

    private sealed class AsyncCountingStore : AsyncInMemoryStore<Item>
    {
        public int Writes { get; private set; }

        protected override Task UpdateCoreAsync(Item data, StoreDataDelegate<Item>? processDelegate = null, System.Threading.CancellationToken ct = default)
        {
            Writes++;
            return base.UpdateCoreAsync(data, processDelegate, ct);
        }
    }

    /// <summary>SH-M032: nothing to assign still rewrote every matched row, moving audit fields and versions.</summary>
    [Fact]
    public async Task An_empty_update_writes_nothing()
    {
        var store = new CountingStore();
        store.Create(new Item { A = 1 });
        store.Create(new Item { A = 2 });
        store.Update(x => x.A > 0, new PropertyUpdate<Item>());
        store.UpdateAll(new PropertyUpdate<Item>());
        store.Writes.Should().Be(0);

        var asyncStore = new AsyncCountingStore();
        await asyncStore.CreateAsync(new Item { A = 1 });
        await asyncStore.UpdateAsync(x => x.A > 0, new PropertyUpdate<Item>());
        await asyncStore.UpdateAllAsync(new PropertyUpdate<Item>());
        asyncStore.Writes.Should().Be(0);
    }

    /// <summary>
    /// SH-M434: a matched row deleted between the read and its write threw EntityNotFoundException, leaving the rows
    /// before it written and the rows after it not. It no longer matches the filter, so it is skipped.
    /// </summary>
    [Fact]
    public void A_row_deleted_during_a_filtered_update_is_skipped_and_the_rest_are_written()
    {
        var store = new InMemoryStore<Item>();
        var ids = Enumerable.Range(1, 3).Select(i => store.Create(new Item { A = i })).ToList();
        var deleted = false;

        store.Update(x => x.A > 0, item =>
        {
            if (!deleted) { deleted = true; store.Delete(store.ReadFirst(x => x.Guid != item.Guid)!); }
            item.A = 9;
        });

        store.Read(x => x.A > 0, null, null, null).Select(x => x.A).Should().Equal(9, 9);
    }

    [Fact]
    public async Task A_row_deleted_during_an_async_filtered_update_is_skipped_and_the_rest_are_written()
    {
        var store = new AsyncInMemoryStore<Item>();
        for (var i = 1; i <= 3; i++) await store.CreateAsync(new Item { A = i });
        var others = (await store.ReadAsync(x => x.A > 1, null, null, null)).ToList();
        var deleted = false;

        await store.UpdateAsync(x => x.A > 0, item =>
        {
            if (!deleted) { deleted = true; store.DeleteAsync(others.First(o => o.Guid != item.Guid)).GetAwaiter().GetResult(); }
            item.A = 9;
        });

        (await store.ReadAsync(x => x.A > 0, null, null, null)).Select(x => x.A).Should().Equal(9, 9);
    }

    /// <summary>SH-M026: `Set(x => x.Child.A, v)` resolved only the leaf; SQL wrote T's own A. Refused at the builder.</summary>
    [Fact]
    public void Set_of_a_nested_member_is_refused()
    {
        var act = () => new PropertyUpdate<Item>().Set(x => x.Child.A, 5);
        act.Should().Throw<ArgumentException>().WithMessage("*top-level property*");
        new PropertyUpdate<Item>().Set(x => x.A, 5).Should().NotBeNull();
    }
}
