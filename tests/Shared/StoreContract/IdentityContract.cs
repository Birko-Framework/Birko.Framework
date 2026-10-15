using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>
/// SH-M313 / SH-M314 (TASK-556) — "give this entity an identity" had no single producer and three policies across ~55
/// sites: <c>??=</c> kept <c>Guid.Empty</c> as a real id (stored under the empty key, then unreachable by id), two paths
/// replaced it, and every SQL / RavenDB / TimescaleDB bulk create OVERWROTE the caller's Guid (CR-L190), so entities a
/// caller linked by pre-set ids were stored under different ones on the framework's default provider while JSON and
/// in-memory kept them — and the tests passed. Now <c>AbstractModel.EnsureGuid()</c> is the one rule: null or Empty gets
/// a fresh Guid, any other value is kept, single or bulk.
///
/// <para>Observed state: the Guid each entity is stored under, read back from the store.</para>
/// </summary>
public abstract partial class StoreContract<T>
{
    /// <summary>
    /// Whether this provider refuses a create of a stored Guid. False only where the user decided a duplicate is
    /// accepted: a TimescaleDB hypertable, which cannot carry a Guid key (TASK-567, 2026-10-15). There the contract pins
    /// the acceptance instead, so a change either way is noticed.
    /// </summary>
    protected virtual bool RefusesDuplicateCreate => true;

    [Fact]
    public void Contract_Create_of_an_entity_with_no_guid_assigns_one()
    {
        // Contract pin.
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("fresh");

        var returned = store.Create(entity);

        returned.Should().NotBe(Guid.Empty);
        entity.Guid.Should().Be(returned);
        NameOf(Settled(store).ReadFirst(x => x.Guid == returned)!).Should().Be("fresh");
    }

    [Fact]
    public void Contract_Create_of_an_entity_with_an_empty_guid_assigns_a_fresh_one()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("empty");
        entity.Guid = Guid.Empty;

        var returned = store.Create(entity);

        returned.Should().NotBe(Guid.Empty, "Guid.Empty means 'no identity yet', never a key");
        NameOf(Settled(store).ReadFirst(x => x.Guid == returned)!).Should().Be("empty");
        Settled(store).ReadFirst(x => x.Guid == Guid.Empty).Should().BeNull();
    }

    [Fact]
    public void Contract_Create_keeps_a_caller_supplied_guid()
    {
        // Contract pin (the single-item path always kept it).
        if (!Ready()) return;
        var store = NewStore();
        var clientGuid = Guid.NewGuid();
        var entity = NewEntity("linked");
        entity.Guid = clientGuid;

        store.Create(entity).Should().Be(clientGuid);

        NameOf(Settled(store).ReadFirst(x => x.Guid == clientGuid)!).Should().Be("linked");
    }

    [Fact]
    public void Contract_Bulk_create_keeps_caller_supplied_guids()
    {
        if (!Ready()) return;
        var store = NewStore();
        var parentGuid = Guid.NewGuid();
        var childGuid = Guid.NewGuid();
        var parent = NewEntity("parent");
        parent.Guid = parentGuid;
        var child = NewEntity("child");
        child.Guid = childGuid;

        store.Create(new[] { parent, child });

        parent.Guid.Should().Be(parentGuid, "bulk create must not overwrite an id the caller chose");
        child.Guid.Should().Be(childGuid);
        NameOf(Settled(store).ReadFirst(x => x.Guid == parentGuid)!).Should().Be("parent");
        NameOf(Settled(store).ReadFirst(x => x.Guid == childGuid)!).Should().Be("child");
    }

    [Fact]
    public void Contract_Bulk_create_assigns_fresh_guids_to_null_and_empty()
    {
        if (!Ready()) return;
        var store = NewStore();
        var none = NewEntity("none");
        var empty = NewEntity("empty");
        empty.Guid = Guid.Empty;

        store.Create(new[] { none, empty });

        none.Guid.Should().NotBeNull().And.NotBe(Guid.Empty);
        empty.Guid.Should().NotBe(Guid.Empty);
        none.Guid!.Value.Should().NotBe(empty.Guid!.Value);
        Settled(store).Count().Should().Be(2);
        Settled(store).ReadFirst(x => x.Guid == Guid.Empty).Should().BeNull();
    }

    /// <summary>
    /// TASK-567. Create means "a new entity": a Guid already stored is refused, never stored twice and never
    /// overwritten. A SQL table for a key-less model stored a second row; RavenDB's Store overwrote.
    /// <para>The refusal is the shared <c>StoreException</c> on every provider (TASK-625 unified the five that differed).</para>
    /// </summary>
    [Fact]
    public void Contract_Create_of_a_stored_guid_is_refused_and_the_stored_entity_is_kept()
    {
        if (!Ready()) return;
        var store = NewStore();
        var guid = Guid.NewGuid();
        var first = NewEntity("first");
        first.Guid = guid;
        store.Create(first);
        var duplicate = NewEntity("duplicate");
        duplicate.Guid = guid;

        if (!RefusesDuplicateCreate)
        {
            store.Create(duplicate);
            Settled(store).Count(x => x.Guid == guid).Should().Be(2, "the documented exception: a duplicate is accepted");
            return;
        }
        ((Action)(() => store.Create(duplicate))).Should().Throw<Birko.Data.Exceptions.StoreException>();

        Settled(store).Count(x => x.Guid == guid).Should().Be(1);
        NameOf(Settled(store).ReadFirst(x => x.Guid == guid)!).Should().Be("first");
    }

    [Fact]
    public void Contract_Bulk_create_of_a_stored_guid_is_refused_and_the_stored_entity_is_kept()
    {
        if (!Ready()) return;
        var store = NewStore();
        var guid = Guid.NewGuid();
        var first = NewEntity("first");
        first.Guid = guid;
        store.Create(first);
        var duplicate = NewEntity("duplicate");
        duplicate.Guid = guid;

        if (!RefusesDuplicateCreate)
        {
            store.Create(new[] { duplicate });
            Settled(store).Count(x => x.Guid == guid).Should().Be(2, "the documented exception: a duplicate is accepted");
            return;
        }
        ((Action)(() => store.Create(new[] { duplicate }))).Should().Throw<Birko.Data.Exceptions.StoreException>();

        Settled(store).Count(x => x.Guid == guid).Should().Be(1);
        NameOf(Settled(store).ReadFirst(x => x.Guid == guid)!).Should().Be("first");
    }
}

public abstract partial class AsyncStoreContract<T>
{
    /// <inheritdoc cref="StoreContract{T}.RefusesDuplicateCreate"/>
    protected virtual bool RefusesDuplicateCreate => true;

    [Fact]
    public async Task Contract_CreateAsync_of_an_entity_with_an_empty_guid_assigns_a_fresh_one()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("empty");
        entity.Guid = Guid.Empty;

        var returned = await store.CreateAsync(entity);

        returned.Should().NotBe(Guid.Empty, "Guid.Empty means 'no identity yet', never a key");
        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == returned))!).Should().Be("empty");
        (await Settled(store).ReadFirstAsync(x => x.Guid == Guid.Empty)).Should().BeNull();
    }

    [Fact]
    public async Task Contract_CreateAsync_keeps_a_caller_supplied_guid()
    {
        // Contract pin.
        if (!Ready()) return;
        var store = NewStore();
        var clientGuid = Guid.NewGuid();
        var entity = NewEntity("linked");
        entity.Guid = clientGuid;

        (await store.CreateAsync(entity)).Should().Be(clientGuid);

        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == clientGuid))!).Should().Be("linked");
    }

    [Fact]
    public async Task Contract_Bulk_CreateAsync_keeps_caller_supplied_guids()
    {
        if (!Ready()) return;
        var store = NewStore();
        var parentGuid = Guid.NewGuid();
        var childGuid = Guid.NewGuid();
        var parent = NewEntity("parent");
        parent.Guid = parentGuid;
        var child = NewEntity("child");
        child.Guid = childGuid;

        await store.CreateAsync(new[] { parent, child });

        parent.Guid.Should().Be(parentGuid, "bulk create must not overwrite an id the caller chose");
        child.Guid.Should().Be(childGuid);
        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == parentGuid))!).Should().Be("parent");
        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == childGuid))!).Should().Be("child");
    }

    [Fact]
    public async Task Contract_Bulk_CreateAsync_assigns_fresh_guids_to_null_and_empty()
    {
        if (!Ready()) return;
        var store = NewStore();
        var none = NewEntity("none");
        var empty = NewEntity("empty");
        empty.Guid = Guid.Empty;

        await store.CreateAsync(new[] { none, empty });

        none.Guid.Should().NotBeNull().And.NotBe(Guid.Empty);
        empty.Guid.Should().NotBe(Guid.Empty);
        none.Guid!.Value.Should().NotBe(empty.Guid!.Value);
        (await Settled(store).CountAsync()).Should().Be(2);
        (await Settled(store).ReadFirstAsync(x => x.Guid == Guid.Empty)).Should().BeNull();
    }

    /// <summary>TASK-567, async: a Guid already stored is refused by create, single and bulk.</summary>
    [Fact]
    public async Task Contract_CreateAsync_of_a_stored_guid_is_refused_and_the_stored_entity_is_kept()
    {
        if (!Ready()) return;
        var store = NewStore();
        var guid = Guid.NewGuid();
        var first = NewEntity("first");
        first.Guid = guid;
        await store.CreateAsync(first);
        var duplicate = NewEntity("duplicate");
        duplicate.Guid = guid;

        if (!RefusesDuplicateCreate)
        {
            await store.CreateAsync(duplicate);
            (await Settled(store).CountAsync(x => x.Guid == guid)).Should().Be(2, "the documented exception: a duplicate is accepted");
            return;
        }
        await ((Func<Task>)(() => store.CreateAsync(duplicate))).Should().ThrowAsync<Birko.Data.Exceptions.StoreException>();
        await ((Func<Task>)(() => store.CreateAsync(new[] { duplicate }))).Should().ThrowAsync<Birko.Data.Exceptions.StoreException>();

        (await Settled(store).CountAsync(x => x.Guid == guid)).Should().Be(1);
        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == guid))!).Should().Be("first");
    }
}
