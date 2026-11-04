using System;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>
/// TASK-719 — three edges every provider answers the same way: deleting a key that is not stored is a no-op (Cosmos DB
/// threw its raw 404); <c>Save</c> hands its delegate an entity that already has its Guid, as <c>Create</c> does (Cosmos DB
/// and RavenDB ran the delegate first); and a bulk update holding an entity with no Guid is refused, not skipped (Cosmos
/// DB's batch path skipped it).
///
/// <para>Observed state: what the delegate saw, which exception reached the caller, and what the store holds.</para>
/// </summary>
public abstract partial class StoreContract<T>
{
    [Fact]
    public void Contract_Delete_of_a_key_that_is_not_stored_is_a_no_op()
    {
        if (!Ready()) return;
        var store = NewStore();
        store.Create(NewEntity("kept"));
        var orphan = NewEntity("orphan");
        orphan.Guid = Guid.NewGuid();

        FluentActions.Invoking(() => store.Delete(orphan)).Should().NotThrow();

        Settled(store).Count().Should().Be(1);
    }

    [Fact]
    public void Contract_Bulk_delete_holding_a_key_that_is_not_stored_deletes_the_rest()
    {
        if (!Ready()) return;
        var store = NewStore();
        store.Create(NewEntity("kept"));
        var gone = NewEntity("gone");
        store.Create(gone);
        var orphan = NewEntity("orphan");
        orphan.Guid = Guid.NewGuid();

        FluentActions.Invoking(() => store.Delete(new[] { gone, orphan })).Should().NotThrow();

        Settled(store).Count().Should().Be(1);
    }

    [Fact]
    public void Contract_Save_of_a_new_entity_gives_the_delegate_its_guid()
    {
        if (!Ready()) return;
        var store = NewStore();
        Guid? seen = null;

        var guid = store.Save(NewEntity("saved"), e => { seen = e.Guid; return e; });

        seen.Should().NotBeNull().And.NotBe(Guid.Empty);
        seen.Should().Be(guid);
    }

    [Fact]
    public void Contract_Bulk_update_holding_an_entity_with_no_guid_is_refused()
    {
        if (!Ready()) return;
        var store = NewStore();
        var stored = NewEntity("stored");
        store.Create(stored);
        Rename(stored, "renamed");
        var unsaved = NewEntity("unsaved");

        FluentActions.Invoking(() => store.Update(new[] { stored, unsaved })).Should().Throw<EntityNotFoundException>();
    }
}

public abstract partial class AsyncStoreContract<T>
{
    [Fact]
    public async Task Contract_DeleteAsync_of_a_key_that_is_not_stored_is_a_no_op()
    {
        if (!Ready()) return;
        var store = NewStore();
        await store.CreateAsync(NewEntity("kept"));
        var orphan = NewEntity("orphan");
        orphan.Guid = Guid.NewGuid();

        await FluentActions.Awaiting(() => store.DeleteAsync(orphan)).Should().NotThrowAsync();

        (await Settled(store).CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Contract_Bulk_DeleteAsync_holding_a_key_that_is_not_stored_deletes_the_rest()
    {
        if (!Ready()) return;
        var store = NewStore();
        await store.CreateAsync(NewEntity("kept"));
        var gone = NewEntity("gone");
        await store.CreateAsync(gone);
        var orphan = NewEntity("orphan");
        orphan.Guid = Guid.NewGuid();

        await FluentActions.Awaiting(() => store.DeleteAsync(new[] { gone, orphan })).Should().NotThrowAsync();

        (await Settled(store).CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Contract_SaveAsync_of_a_new_entity_gives_the_delegate_its_guid()
    {
        if (!Ready()) return;
        var store = NewStore();
        Guid? seen = null;

        var guid = await store.SaveAsync(NewEntity("saved"), e => { seen = e.Guid; return e; });

        seen.Should().NotBeNull().And.NotBe(Guid.Empty);
        seen.Should().Be(guid);
    }

    [Fact]
    public async Task Contract_Bulk_UpdateAsync_holding_an_entity_with_no_guid_is_refused()
    {
        if (!Ready()) return;
        var store = NewStore();
        var stored = NewEntity("stored");
        await store.CreateAsync(stored);
        Rename(stored, "renamed");
        var unsaved = NewEntity("unsaved");

        await FluentActions.Awaiting(() => store.UpdateAsync(new[] { stored, unsaved })).Should().ThrowAsync<EntityNotFoundException>();
    }
}
