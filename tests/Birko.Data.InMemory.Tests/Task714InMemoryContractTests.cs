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
/// TASK-714 on the in-memory stores and the store helpers. Measured before the fix: a bulk create checked keys before the
/// store delegate ran, so a delegate replacement carrying a stored Guid failed after the items before it were stored; a
/// refused bulk create had already given the caller's entities Guids; an update's delegate could swap the entity for one
/// with another key (SQL then rewrote the original row's key); <c>GetUnwrappedStore</c> looped forever on a wrapper cycle.
/// </summary>
public class Task714InMemoryContractTests
{
    public class Item : AbstractModel
    {
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task A_bulk_create_whose_delegate_changes_a_key_is_refused_and_stores_nothing()
    {
        var store = new AsyncInMemoryStore<Item>();
        var stored = await store.CreateAsync(new Item { Name = "old" });

        var act = () => store.CreateAsync(new[] { new Item { Name = "a" }, new Item { Name = "b" } },
            x => x.Name == "b" ? new Item { Guid = stored, Name = "b" } : x);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await store.CountAsync()).Should().Be(1, "nothing of the refused batch is stored");
    }

    [Fact]
    public void A_sync_bulk_create_whose_delegate_changes_a_key_is_refused_and_stores_nothing()
    {
        var store = new InMemoryStore<Item>();
        var stored = store.Create(new Item { Name = "old" });

        var act = () => store.Create(new[] { new Item { Name = "a" }, new Item { Name = "b" } },
            x => x.Name == "b" ? new Item { Guid = stored, Name = "b" } : x);

        act.Should().Throw<InvalidOperationException>();
        store.Count().Should().Be(1);
    }

    [Fact]
    public async Task A_refused_bulk_create_leaves_the_caller_s_new_entities_without_a_guid()
    {
        var store = new AsyncInMemoryStore<Item>();
        var stored = await store.CreateAsync(new Item { Name = "old" });
        var fresh = new Item { Name = "new" };

        var act = () => store.CreateAsync(new[] { fresh, new Item { Guid = stored, Name = "dup" } });

        await act.Should().ThrowAsync<Exception>();
        fresh.Guid.Should().BeNull("a refused create changes nothing the caller holds");
        (await store.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_update_whose_delegate_changes_the_key_is_refused()
    {
        var store = new AsyncInMemoryStore<Item>();
        var a = await store.CreateAsync(new Item { Name = "a" });
        var b = await store.CreateAsync(new Item { Name = "b" });

        var act = () => store.UpdateAsync(new Item { Guid = a, Name = "x" }, _ => new Item { Guid = b, Name = "x" });

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await store.ReadAsync(b))!.Name.Should().Be("b");
    }

    [Fact]
    public void A_delegate_returning_a_copy_without_a_guid_keeps_the_entity_s_key()
    {
        var item = new Item { Guid = Guid.NewGuid(), Name = "a" };

        var result = StoreDelegates.Apply<Item>(_ => new Item { Name = "copy" }, item);

        result.Guid.Should().Be(item.Guid);
    }

    private sealed class Loop : IStoreWrapper
    {
        public Loop? Next { get; set; }
        public object? GetInnerStore() => Next;
    }

    [Fact]
    public void Unwrapping_a_wrapper_cycle_is_refused()
    {
        var a = new Loop();
        var b = new Loop { Next = a };
        a.Next = b;

        var act = () => ((object)a).GetUnwrappedStore();

        act.Should().Throw<InvalidOperationException>();
    }
}
