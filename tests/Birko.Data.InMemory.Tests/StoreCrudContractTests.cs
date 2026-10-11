using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Exceptions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InMemory.Tests;

/// <summary>
/// TASK-163 — the store-crud-contract medium findings fixed in the portable store layer and the in-memory store.
/// </summary>
public class StoreCrudContractTests
{
    #region SH-M308 — an update must not resurrect an entity deleted meanwhile

    [Fact]
    public void Update_does_not_resurrect_an_entity_deleted_while_it_ran()
    {
        var store = new InMemoryStore<TestModel>();
        var entity = new TestModel { Name = "a" };
        store.Create(entity);

        // The store delegate runs between the existence check and the write — exactly the window another thread's
        // delete can land in, made deterministic.
        // TASK-555 (rule 56): this asserted a silent return; the miss is now reported. The row must still stay deleted.
        var act = () => store.Update(entity, e => { store.Delete(e); return e; });

        act.Should().Throw<EntityNotFoundException>().Which.Guids.Should().Equal(entity.Guid!.Value);
        store.Count().Should().Be(0);
        store.Read(entity.Guid!.Value).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_does_not_resurrect_an_entity_deleted_while_it_ran()
    {
        var store = new AsyncInMemoryStore<TestModel>();
        var entity = new TestModel { Name = "a" };
        await store.CreateAsync(entity);

        // TASK-555 (rule 56): this asserted a silent return; the miss is now reported. The row must still stay deleted.
        var act = () => store.UpdateAsync(entity, e => { store.Items.Should().ContainKey(e.Guid!.Value); store.DeleteAsync(e).GetAwaiter().GetResult(); return e; });

        await act.Should().ThrowAsync<EntityNotFoundException>();
        (await store.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void Bulk_update_does_not_resurrect_an_entity_deleted_while_it_ran()
    {
        var store = new InMemoryStore<TestModel>();
        var a = new TestModel { Name = "a" };
        var b = new TestModel { Name = "b" };
        store.Create(new[] { a, b });

        // TASK-555 (rule 56): this asserted a silent return; the miss is now reported. The row must still stay deleted.
        var act = () => store.Update(new[] { a, b }, e => { if (e == a) store.Delete(a); return e; });

        act.Should().Throw<EntityNotFoundException>().Which.Guids.Should().Equal(a.Guid!.Value);
        store.Items.Keys.Should().BeEquivalentTo(new[] { b.Guid!.Value });
    }

    [Fact]
    public async Task Bulk_UpdateAsync_does_not_resurrect_an_entity_deleted_while_it_ran()
    {
        var store = new AsyncInMemoryStore<TestModel>();
        var a = new TestModel { Name = "a" };
        var b = new TestModel { Name = "b" };
        await store.CreateAsync(new[] { a, b });

        // TASK-555 (rule 56): this asserted a silent return; the miss is now reported. The row must still stay deleted.
        var act = () => store.UpdateAsync(new[] { a, b }, e => { if (e == a) store.DeleteAsync(a).GetAwaiter().GetResult(); return e; });

        (await act.Should().ThrowAsync<EntityNotFoundException>()).Which.Guids.Should().Equal(a.Guid!.Value);
        store.Items.Keys.Should().BeEquivalentTo(new[] { b.Guid!.Value });
    }

    [Fact]
    public void Update_still_replaces_a_stored_entity()
    {
        var store = new InMemoryStore<TestModel>();
        var entity = new TestModel { Name = "a" };
        store.Create(entity);

        var replacement = new TestModel { Guid = entity.Guid, Name = "b" };
        store.Update(replacement);

        store.Read(entity.Guid!.Value)!.Name.Should().Be("b");
    }

    #endregion

    #region SH-M310 — create never replaces

    [Fact]
    public void Create_with_an_existing_guid_throws_and_keeps_the_stored_entity()
    {
        var store = new InMemoryStore<TestModel>();
        var original = new TestModel { Name = "original" };
        store.Create(original);

        var act = () => store.Create(new TestModel { Guid = original.Guid, Name = "impostor" });

        act.Should().Throw<StoreException>().WithMessage("*already holds*");
        store.Read(original.Guid!.Value)!.Name.Should().Be("original");
    }

    [Fact]
    public async Task CreateAsync_with_an_existing_guid_throws_and_keeps_the_stored_entity()
    {
        var store = new AsyncInMemoryStore<TestModel>();
        var original = new TestModel { Name = "original" };
        await store.CreateAsync(original);

        var act = () => store.CreateAsync(new TestModel { Guid = original.Guid, Name = "impostor" });

        await act.Should().ThrowAsync<StoreException>();
        (await store.ReadAsync(original.Guid!.Value))!.Name.Should().Be("original");
    }

    [Fact]
    public void Bulk_create_with_one_existing_guid_writes_nothing()
    {
        var store = new InMemoryStore<TestModel>();
        var original = new TestModel { Name = "original" };
        store.Create(original);
        var fresh = new TestModel { Name = "fresh" };

        var act = () => store.Create(new[] { fresh, new TestModel { Guid = original.Guid, Name = "impostor" } });

        act.Should().Throw<StoreException>();
        store.Count().Should().Be(1);
        store.Read(original.Guid!.Value)!.Name.Should().Be("original");
    }

    [Fact]
    public async Task Bulk_CreateAsync_with_a_guid_repeated_inside_the_batch_writes_nothing()
    {
        var store = new AsyncInMemoryStore<TestModel>();
        var guid = Guid.NewGuid();

        var act = () => store.CreateAsync(new[] { new TestModel { Guid = guid, Name = "1" }, new TestModel { Guid = guid, Name = "2" } });

        await act.Should().ThrowAsync<StoreException>();
        (await store.CountAsync()).Should().Be(0);
    }

    #endregion

    #region SH-M312 — Delete(filter) goes through DeleteCore

    private sealed class AuditingStore : InMemoryStore<TestModel>
    {
        public List<string> Deleted { get; } = new();

        protected override void DeleteCore(IEnumerable<TestModel> data)
        {
            var items = data.ToList();
            Deleted.AddRange(items.Select(x => x.Name));
            base.DeleteCore(items);
        }
    }

    private sealed class AsyncAuditingStore : AsyncInMemoryStore<TestModel>
    {
        public List<string> Deleted { get; } = new();

        protected override Task DeleteCoreAsync(IEnumerable<TestModel> data, System.Threading.CancellationToken ct = default)
        {
            var items = data.ToList();
            Deleted.AddRange(items.Select(x => x.Name));
            return base.DeleteCoreAsync(items, ct);
        }
    }

    [Fact]
    public void Delete_by_filter_reaches_an_overridden_DeleteCore()
    {
        var store = new AuditingStore();
        store.Create(new[] { new TestModel { Name = "keep" }, new TestModel { Name = "drop" } });

        store.Delete(x => x.Name == "drop");

        store.Deleted.Should().Equal("drop");
        store.Read().Select(x => x.Name).Should().Equal("keep");
    }

    [Fact]
    public async Task DeleteAsync_by_filter_reaches_an_overridden_DeleteCoreAsync()
    {
        var store = new AsyncAuditingStore();
        await store.CreateAsync(new[] { new TestModel { Name = "keep" }, new TestModel { Name = "drop" } });

        await store.DeleteAsync(x => x.Name == "drop");

        store.Deleted.Should().Equal("drop");
        store.Items.Values.Select(x => x.Name).Should().Equal("keep");
    }

    #endregion

    #region SH-M302 — a wrapper chain is unwrapped to its innermost store

    private sealed class PassThroughWrapper : IStore<TestModel>, IStoreWrapper<TestModel>
    {
        private readonly IStore<TestModel> _inner;
        public PassThroughWrapper(IStore<TestModel> inner) => _inner = inner;

        public object? GetInnerStore() => _inner;
        public TInner? GetInnerStoreAs<TInner>() where TInner : class => _inner as TInner;

        public void Init() => _inner.Init();
        public void Destroy() => _inner.Destroy();
        public long Count(Expression<Func<TestModel, bool>>? filter = null) => _inner.Count(filter);
        public TestModel? Read(Guid guid) => _inner.Read(guid);
        public TestModel? Read(Expression<Func<TestModel, bool>>? filter = null) => _inner.Read(filter);
        public Guid Create(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => _inner.Create(data, storeDelegate);
        public void Update(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => _inner.Update(data, storeDelegate);
        public void Delete(TestModel data) => _inner.Delete(data);
        public TestModel CreateInstance() => _inner.CreateInstance();
        public Guid Save(TestModel data, StoreDataDelegate<TestModel>? storeDelegate = null) => _inner.Save(data, storeDelegate);
    }

    [Fact]
    public void IsStoreOfType_sees_through_two_wrappers()
    {
        IStore<TestModel> chain = new PassThroughWrapper(new PassThroughWrapper(new InMemoryStore<TestModel>()));

        chain.IsStoreOfType<TestModel, InMemoryStore<TestModel>>().Should().BeTrue();
    }

    [Fact]
    public void IsStoreOfType_sees_through_one_wrapper_and_rejects_a_different_backend()
    {
        IStore<TestModel> one = new PassThroughWrapper(new InMemoryStore<TestModel>());

        one.IsStoreOfType<TestModel, InMemoryStore<TestModel>>().Should().BeTrue();
        new PassThroughWrapper(new PassThroughWrapper(new InMemoryStore<TestModel>()))
            .IsStoreOfType<TestModel, AsyncInMemoryStore<TestModel>>().Should().BeFalse();
    }

    #endregion

    #region SH-M304 / SH-M305 — StoreLocator

    // StoreLocator is process-wide; every test uses its own settings id so parallel tests cannot share an entry.
    private static PasswordSettings Settings(string password = "p") => new($"loc-{Guid.NewGuid():N}", "db", password);

    [Fact]
    public void StoreLocator_serves_an_async_store()
    {
        var settings = Settings();

        var first = StoreLocator.GetStore<AsyncInMemoryStore<TestModel>, PasswordSettings>(settings);
        var second = StoreLocator.GetStore<AsyncInMemoryStore<TestModel>, PasswordSettings>(settings);

        first.Should().NotBeNull().And.BeSameAs(second);
    }

    [Fact]
    public void StoreLocator_refuses_a_type_that_is_not_a_store()
    {
        var act = () => StoreLocator.GetStore<TestModel, PasswordSettings>(Settings());

        act.Should().Throw<ArgumentException>().WithMessage("*is not a store*");
    }

    [Fact]
    public void StoreLocator_refuses_a_hit_whose_settings_differ_outside_the_id()
    {
        var first = Settings("alice-secret");
        StoreLocator.GetStore<InMemoryStore<TestModel>, PasswordSettings>(first);
        var other = new PasswordSettings(first.Location!, first.Name!, "bob-secret");
        other.GetId().Should().Be(first.GetId(), "the precondition: the id leaves the password out");

        var act = () => StoreLocator.GetStore<InMemoryStore<TestModel>, PasswordSettings>(other);

        act.Should().Throw<InvalidOperationException>().WithMessage("*different settings*");
    }

    [Fact]
    public void StoreLocator_returns_the_cached_store_for_equal_values_in_a_fresh_instance()
    {
        var first = Settings("same");
        var store = StoreLocator.GetStore<InMemoryStore<TestModel>, PasswordSettings>(first);

        var again = StoreLocator.GetStore<InMemoryStore<TestModel>, PasswordSettings>(new PasswordSettings(first.Location!, first.Name!, "same"));

        again.Should().BeSameAs(store);
    }

    #endregion
}
