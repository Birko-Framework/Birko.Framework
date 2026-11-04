using System;
using System.Threading.Tasks;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>
/// TASK-812 — a store whose settings were never set answered as if it worked: <c>Create</c> returned <c>Guid.Empty</c>,
/// <c>Update</c> and <c>Delete</c> returned quietly, reads answered null, empty or 0 (measured on MongoDB, RavenDB, Cosmos
/// DB, InfluxDB and every SQL provider). Decided 2026-11-04 (the user): reads refuse too, as an empty answer from a store
/// that cannot look is the same false answer. Every operation is now <c>InvalidOperationException</c>.
///
/// <para>The unconfigured store is a fresh instance of the provider's own store type, built with no arguments; a type
/// with no parameterless constructor cannot be unconfigured that way, and the test passes over it.</para>
/// </summary>
public abstract partial class StoreContract<T>
{
    /// <summary>False for a store that needs no settings (in-memory).</summary>
    protected virtual bool NeedsSettings => true;

    private IBulkStore<T>? Unconfigured()
    {
        var type = NewStore().GetType();
        return type.GetConstructor(Type.EmptyTypes) == null ? null : (IBulkStore<T>)Activator.CreateInstance(type)!;
    }

    [Fact]
    public void Contract_An_unconfigured_store_refuses_reads_and_writes()
    {
        if (!NeedsSettings || !Ready()) return;
        var store = Unconfigured();
        if (store == null) return;
        var stored = NewEntity("x");
        stored.Guid = Guid.NewGuid();

        FluentActions.Invoking(() => store.Create(NewEntity("new"))).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => store.Update(stored)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => store.Delete(stored)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => store.Save(NewEntity("saved"))).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => store.Read(stored.Guid!.Value)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => store.Count()).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => store.ReadFirst(x => x.Guid == stored.Guid)).Should().Throw<InvalidOperationException>();
    }
}

public abstract partial class AsyncStoreContract<T>
{
    /// <summary>False for a store that needs no settings (in-memory).</summary>
    protected virtual bool NeedsSettings => true;

    private IAsyncBulkStore<T>? Unconfigured()
    {
        var type = NewStore().GetType();
        return type.GetConstructor(Type.EmptyTypes) == null ? null : (IAsyncBulkStore<T>)Activator.CreateInstance(type)!;
    }

    [Fact]
    public async Task Contract_An_unconfigured_store_refuses_reads_and_writes_async()
    {
        if (!NeedsSettings || !Ready()) return;
        var store = Unconfigured();
        if (store == null) return;
        var stored = NewEntity("x");
        stored.Guid = Guid.NewGuid();

        await FluentActions.Awaiting(() => store.CreateAsync(NewEntity("new"))).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => store.UpdateAsync(stored)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => store.DeleteAsync(stored)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => store.SaveAsync(NewEntity("saved"))).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => store.ReadAsync(stored.Guid!.Value)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => store.CountAsync()).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => store.ReadFirstAsync(x => x.Guid == stored.Guid)).Should().ThrowAsync<InvalidOperationException>();
    }
}
