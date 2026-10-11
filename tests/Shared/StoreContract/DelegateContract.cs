using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>
/// SH-M315 (TASK-557) — <c>StoreDataDelegate&lt;T&gt;</c> returns <c>T</c>, but every one of ~110 call sites discarded the
/// result and persisted the object it had passed in, so a delegate written in the functional style — return a modified
/// copy — was silently ignored. A store now persists what the delegate returns (<c>StoreDelegates.Apply</c>); a returned
/// copy with no Guid inherits the original's.
///
/// <para>Observed state: the name the store holds afterwards, set only on the returned copy.</para>
/// </summary>
public abstract partial class StoreContract<T>
{
    private T CopyOf(T original, string name)
    {
        var copy = NewEntity(name);
        copy.Guid = original.Guid;
        return copy;
    }

    [Fact]
    public void Contract_Create_persists_the_entity_the_delegate_returns()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("original");

        var guid = store.Create(entity, e => CopyOf(e, "from-delegate"));

        NameOf(Settled(store).ReadFirst(x => x.Guid == guid)!).Should().Be("from-delegate");
    }

    [Fact]
    public void Contract_Update_persists_the_entity_the_delegate_returns()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("original");
        store.Create(entity);

        store.Update(entity, e => CopyOf(e, "from-delegate"));

        NameOf(Settled(store).ReadFirst(x => x.Guid == entity.Guid)!).Should().Be("from-delegate");
    }

    [Fact]
    public void Contract_Bulk_create_persists_the_entities_the_delegate_returns()
    {
        if (!Ready()) return;
        var store = NewStore();
        var a = NewEntity("a");
        var b = NewEntity("b");

        store.Create(new[] { a, b }, e => CopyOf(e, NameOf(e) + "-copy"));

        NameOf(Settled(store).ReadFirst(x => x.Guid == a.Guid)!).Should().Be("a-copy");
        NameOf(Settled(store).ReadFirst(x => x.Guid == b.Guid)!).Should().Be("b-copy");
    }

    [Fact]
    public void Contract_Bulk_update_persists_the_entities_the_delegate_returns()
    {
        if (!Ready()) return;
        var store = NewStore();
        var a = NewEntity("a");
        var b = NewEntity("b");
        store.Create(new[] { a, b });

        store.Update(new[] { a, b }, e => CopyOf(e, NameOf(e) + "-copy"));

        NameOf(Settled(store).ReadFirst(x => x.Guid == a.Guid)!).Should().Be("a-copy");
        NameOf(Settled(store).ReadFirst(x => x.Guid == b.Guid)!).Should().Be("b-copy");
    }

    [Fact]
    public void Contract_A_delegate_that_mutates_in_place_still_works()
    {
        // Contract pin: the in-place style every existing delegate uses.
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("original");

        var guid = store.Create(entity, e => { Rename(e, "mutated"); return e; });

        NameOf(Settled(store).ReadFirst(x => x.Guid == guid)!).Should().Be("mutated");
    }
}

public abstract partial class AsyncStoreContract<T>
{
    private T CopyOf(T original, string name)
    {
        var copy = NewEntity(name);
        copy.Guid = original.Guid;
        return copy;
    }

    [Fact]
    public async Task Contract_CreateAsync_persists_the_entity_the_delegate_returns()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("original");

        var guid = await store.CreateAsync(entity, e => CopyOf(e, "from-delegate"));

        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == guid))!).Should().Be("from-delegate");
    }

    [Fact]
    public async Task Contract_UpdateAsync_persists_the_entity_the_delegate_returns()
    {
        if (!Ready()) return;
        var store = NewStore();
        var entity = NewEntity("original");
        await store.CreateAsync(entity);

        await store.UpdateAsync(entity, e => CopyOf(e, "from-delegate"));

        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == entity.Guid))!).Should().Be("from-delegate");
    }

    [Fact]
    public async Task Contract_Bulk_CreateAsync_persists_the_entities_the_delegate_returns()
    {
        if (!Ready()) return;
        var store = NewStore();
        var a = NewEntity("a");
        var b = NewEntity("b");

        await store.CreateAsync(new[] { a, b }, e => CopyOf(e, NameOf(e) + "-copy"));

        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == a.Guid))!).Should().Be("a-copy");
        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == b.Guid))!).Should().Be("b-copy");
    }

    [Fact]
    public async Task Contract_Bulk_UpdateAsync_persists_the_entities_the_delegate_returns()
    {
        if (!Ready()) return;
        var store = NewStore();
        var a = NewEntity("a");
        var b = NewEntity("b");
        await store.CreateAsync(new[] { a, b });

        await store.UpdateAsync(new[] { a, b }, e => CopyOf(e, NameOf(e) + "-copy"));

        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == a.Guid))!).Should().Be("a-copy");
        NameOf((await Settled(store).ReadFirstAsync(x => x.Guid == b.Guid))!).Should().Be("b-copy");
    }
}
