using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>
/// SH-M306 / SH-M309 (TASK-555) — the cross-provider contract for an update that addresses an entity that is not
/// stored. Every backend used to write nothing and report success: <c>Update</c> returned normally and <c>Save</c>
/// handed back the Guid of an entity it never stored. Now <c>Update</c> throws <see cref="EntityNotFoundException"/>
/// (a bulk update after applying the stored entities, naming the rest) and <c>Save</c> creates the entity under the
/// caller's Guid. Each provider detects the miss from its own matched / affected count.
///
/// <para>Shared source: linked into each provider's test project (<c>tests/Shared/StoreContract/</c>), which
/// subclasses it with its own store and model. Observed state is what the store returns afterwards — never "did not
/// throw".</para>
/// </summary>
public abstract partial class StoreContract<T> where T : AbstractModel
{
    /// <summary>
    /// False when the backend is not reachable — a live provider reports its own skip (and fails instead under
    /// <c>BIRKO_REQUIRE_LIVE</c>); every contract test then returns before touching a store.
    /// </summary>
    protected virtual bool Ready() => true;

    /// <summary>
    /// Makes the store's writes visible to its queries before the contract reads them back. A no-op for every store
    /// that reads its own writes; a near-real-time index (Elasticsearch) refreshes here.
    /// </summary>
    protected virtual void Settle(IBulkStore<T> store) { }

    private IBulkStore<T> Settled(IBulkStore<T> store)
    {
        Settle(store);
        return store;
    }

    /// <summary>A fresh, empty, initialised store.</summary>
    protected abstract IBulkStore<T> NewStore();

    /// <summary>A new, unsaved entity carrying <paramref name="name"/> in some persisted property.</summary>
    protected abstract T NewEntity(string name);

    /// <summary>The persisted property <see cref="NewEntity"/> wrote.</summary>
    protected abstract string NameOf(T entity);

    protected abstract void Rename(T entity, string name);

    [Fact]
    public void Contract_Update_of_an_entity_that_is_not_stored_throws_and_writes_nothing()
    {
        if (!Ready()) return;
        var store = NewStore();
        store.Create(NewEntity("kept"));
        var orphan = NewEntity("orphan");
        orphan.Guid = Guid.NewGuid();

        var act = () => store.Update(orphan);

        act.Should().Throw<EntityNotFoundException>().Which.Guids.Should().Equal(orphan.Guid!.Value);
        Settled(store).Count().Should().Be(1);
        Settled(store).ReadFirst(x => x.Guid == orphan.Guid).Should().BeNull();
    }

    [Fact]
    public void Contract_Update_of_a_stored_entity_still_writes()
    {
        if (!Ready()) return;
        // Contract pin.
        var store = NewStore();
        var entity = NewEntity("before");
        store.Create(entity);
        Rename(entity, "after");

        store.Update(entity);

        NameOf(Settled(store).ReadFirst(x => x.Guid == entity.Guid)!).Should().Be("after");
    }

    [Fact]
    public void Contract_Save_of_a_new_entity_with_a_client_guid_stores_it_under_that_guid()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("imported");
        var clientGuid = Guid.NewGuid();
        entity.Guid = clientGuid;

        var returned = store.Save(entity);

        returned.Should().Be(clientGuid);
        var stored = Settled(store).ReadFirst(x => x.Guid == clientGuid);
        stored.Should().NotBeNull("Save must not report the Guid of an entity it never stored");
        NameOf(stored!).Should().Be("imported");
    }

    [Fact]
    public void Contract_Save_that_falls_through_to_create_runs_the_delegate_once()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("x");
        entity.Guid = Guid.NewGuid();
        var calls = 0;

        store.Save(entity, e => { calls++; return e; });

        calls.Should().Be(1, "an update that missed and the create it fell through to are one save");
    }

    [Fact]
    public void Contract_Save_of_a_stored_entity_still_updates_it()
    {
        if (!Ready()) return;
        // Contract pin.
        var store = NewStore();
        var entity = NewEntity("before");
        store.Create(entity);
        Rename(entity, "after");

        store.Save(entity);

        Settled(store).Count().Should().Be(1);
        NameOf(Settled(store).ReadFirst(x => x.Guid == entity.Guid)!).Should().Be("after");
    }

    [Fact]
    public void Contract_Bulk_update_with_one_missing_entity_updates_the_rest_and_names_the_missing()
    {
        if (!Ready()) return;
        var store = NewStore();
        var a = NewEntity("a");
        var b = NewEntity("b");
        store.Create(new[] { a, b });
        Rename(a, "a2");
        Rename(b, "b2");
        var orphan = NewEntity("orphan");
        orphan.Guid = Guid.NewGuid();

        var act = () => store.Update(new[] { a, orphan, b });

        act.Should().Throw<EntityNotFoundException>().Which.Guids.Should().Equal(orphan.Guid!.Value);
        Settled(store).Count().Should().Be(2);
        NameOf(Settled(store).ReadFirst(x => x.Guid == a.Guid)!).Should().Be("a2");
        NameOf(Settled(store).ReadFirst(x => x.Guid == b.Guid)!).Should().Be("b2");
    }
}

/// <summary>The async twin of <see cref="StoreContract{T}"/>.</summary>
public abstract partial class AsyncStoreContract<T> where T : AbstractModel
{
    /// <summary>
    /// False when the backend is not reachable — a live provider reports its own skip (and fails instead under
    /// <c>BIRKO_REQUIRE_LIVE</c>); every contract test then returns before touching a store.
    /// </summary>
    protected virtual bool Ready() => true;

    /// <summary>
    /// Makes the store's writes visible to its queries before the contract reads them back. A no-op for every store
    /// that reads its own writes; a near-real-time index (Elasticsearch) refreshes here.
    /// </summary>
    protected virtual void Settle(IAsyncBulkStore<T> store) { }

    private IAsyncBulkStore<T> Settled(IAsyncBulkStore<T> store)
    {
        Settle(store);
        return store;
    }

    protected abstract IAsyncBulkStore<T> NewStore();

    protected abstract T NewEntity(string name);

    protected abstract string NameOf(T entity);

    protected abstract void Rename(T entity, string name);

    [Fact]
    public async Task Contract_UpdateAsync_of_an_entity_that_is_not_stored_throws_and_writes_nothing()
    {
        if (!Ready()) return;
        var store = NewStore();
        await store.CreateAsync(NewEntity("kept"));
        var orphan = NewEntity("orphan");
        orphan.Guid = Guid.NewGuid();

        var act = () => store.UpdateAsync(orphan);

        (await act.Should().ThrowAsync<EntityNotFoundException>()).Which.Guids.Should().Equal(orphan.Guid!.Value);
        (await Settled(store).CountAsync()).Should().Be(1);
        (await Settled(store).ReadFirstAsync(x => x.Guid == orphan.Guid)).Should().BeNull();
    }

    [Fact]
    public async Task Contract_UpdateAsync_of_a_stored_entity_still_writes()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("before");
        await store.CreateAsync(entity);
        Rename(entity, "after");

        await store.UpdateAsync(entity);

        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == entity.Guid))!).Should().Be("after");
    }

    [Fact]
    public async Task Contract_SaveAsync_of_a_new_entity_with_a_client_guid_stores_it_under_that_guid()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("imported");
        var clientGuid = Guid.NewGuid();
        entity.Guid = clientGuid;

        var returned = await store.SaveAsync(entity);

        returned.Should().Be(clientGuid);
        var stored = await Settled(store).ReadFirstAsync(x => x.Guid == clientGuid);
        stored.Should().NotBeNull("SaveAsync must not report the Guid of an entity it never stored");
        NameOf(stored!).Should().Be("imported");
    }

    [Fact]
    public async Task Contract_SaveAsync_that_falls_through_to_create_runs_the_delegate_once()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("x");
        entity.Guid = Guid.NewGuid();
        var calls = 0;

        await store.SaveAsync(entity, e => { calls++; return e; });

        calls.Should().Be(1);
    }

    [Fact]
    public async Task Contract_SaveAsync_of_a_stored_entity_still_updates_it()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("before");
        await store.CreateAsync(entity);
        Rename(entity, "after");

        await store.SaveAsync(entity);

        (await Settled(store).CountAsync()).Should().Be(1);
        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == entity.Guid))!).Should().Be("after");
    }

    [Fact]
    public async Task Contract_Bulk_UpdateAsync_with_one_missing_entity_updates_the_rest_and_names_the_missing()
    {
        if (!Ready()) return;
        var store = NewStore();
        var a = NewEntity("a");
        var b = NewEntity("b");
        await store.CreateAsync(new[] { a, b });
        Rename(a, "a2");
        Rename(b, "b2");
        var orphan = NewEntity("orphan");
        orphan.Guid = Guid.NewGuid();

        var act = () => store.UpdateAsync(new[] { a, orphan, b });

        (await act.Should().ThrowAsync<EntityNotFoundException>()).Which.Guids.Should().Equal(orphan.Guid!.Value);
        (await Settled(store).CountAsync()).Should().Be(2);
        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == a.Guid))!).Should().Be("a2");
        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == b.Guid))!).Should().Be("b2");
    }
}
