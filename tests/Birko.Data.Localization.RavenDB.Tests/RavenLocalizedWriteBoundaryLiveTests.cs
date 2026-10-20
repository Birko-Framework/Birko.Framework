using System.Globalization;
using System.Linq.Expressions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Models;
using Birko.Data.Patterns.UnitOfWork;
using Birko.Data.RavenDB.Stores;
using Birko.Data.RavenDB.UnitOfWork;
using Birko.Data.Stores;
using FluentAssertions;
using Raven.Client.Documents;
using Raven.Client.ServerWide.Operations;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Localization.RavenDB.Tests;

public sealed class RavenProduct : AbstractModel, ILocalizable
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public IReadOnlyList<string> GetLocalizableFields() => new[] { nameof(Name) };
}

/// <summary>
/// TASK-700, live: <see cref="RavenLocalizedWriteBoundary"/> makes the entity write and its translation writes one
/// session, saved as one cluster-wide transaction (the RavenDB twin of TASK-683's SQL tests). Gated on
/// <c>BIRKO_RAVEN_URL</c>. Each test owns a fresh database. Assertions load by id: a query reads an index that catches up
/// with a write later.
/// </summary>
public sealed class RavenLocalizedWriteBoundaryLiveTests : IDisposable
{
    private const string UrlEnv = "BIRKO_RAVEN_URL";
    private readonly ITestOutputHelper _output;
    private readonly List<string> _databases = new();
    private readonly List<IDisposable> _stores = new();
    private string? _url;

    public RavenLocalizedWriteBoundaryLiveTests(ITestOutputHelper output) => _output = output;

    private bool Ready()
    {
        _url = Environment.GetEnvironmentVariable(UrlEnv);
        if (!string.IsNullOrWhiteSpace(_url)) return true;
        const string message = "SKIPPED: no live RavenDB. Set " + UrlEnv + " (e.g. http://localhost:8080); "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private string NewDb()
    {
        var db = "birko-task700-" + Guid.NewGuid().ToString("N");
        _databases.Add(db);
        return db;
    }

    private AsyncRavenDBStore<T> Store<T>(string db) where T : AbstractModel
    {
        var store = new AsyncRavenDBStore<T>(_url!, db);
        _stores.Add(store);
        return store;
    }

    private RavenDBStore<T> SyncStore<T>(string db) where T : AbstractModel
    {
        var store = new RavenDBStore<T>(_url!, db);
        _stores.Add(store);
        return store;
    }

    private static Context Slovak() => new() { CurrentCulture = new CultureInfo("sk"), DefaultCulture = new CultureInfo("en") };

    private static Guid KeyOf(Guid entity) => LocalizedTranslationRows.KeyGuid(entity, nameof(RavenProduct), nameof(RavenProduct.Name), "sk");

    // A RavenDB query reads an index that catches up asynchronously; a filter write needs it current.
    private static Task SettleAsync() => Task.Delay(1500);

    private async Task<(AsyncRavenDBStore<RavenProduct> Products, AsyncRavenDBStore<EntityTranslationModel> Rows,
        FailingStore<EntityTranslationModel> Failing, AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<RavenProduct>, RavenProduct> Bulk)> FixtureAsync()
    {
        var db = NewDb();
        var products = Store<RavenProduct>(db);
        var rows = Store<EntityTranslationModel>(db);
        await products.InitAsync();
        await rows.InitAsync();
        var failing = new FailingStore<EntityTranslationModel>(rows);
        var boundary = RavenLocalizedWriteBoundary.For(products, failing);
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<RavenProduct>, RavenProduct>(products, failing, Slovak(), boundary);
        return (products, rows, failing, bulk);
    }

    private static async Task<string?> ValueAsync(AsyncRavenDBStore<EntityTranslationModel> rows, Guid entity)
        => (await rows.ReadAsync(KeyOf(entity)))?.Value;

    [Fact]
    public void The_boundary_is_atomic_only_for_two_raven_stores_on_one_database()
    {
        if (!Ready()) return;
        var db = NewDb();
        var products = Store<RavenProduct>(db);

        RavenLocalizedWriteBoundary.For(products, new FailingStore<EntityTranslationModel>(Store<EntityTranslationModel>(db)))
            .IsAtomic.Should().BeTrue("a decorator over a store on the same database is found through the chain");
        RavenLocalizedWriteBoundary.For(products, Store<EntityTranslationModel>(NewDb()))
            .IsAtomic.Should().BeFalse("another database is another session");
        RavenLocalizedWriteBoundary.For(products, new AsyncInMemoryStore<EntityTranslationModel>()).IsAtomic.Should().BeFalse();
        RavenLocalizedWriteBoundary.For(products, new AsyncRavenDBStore<EntityTranslationModel>())
            .IsAtomic.Should().BeFalse("a store whose settings are not set has no document store");
        var strict = () => RavenLocalizedWriteBoundary.For(products, new AsyncInMemoryStore<EntityTranslationModel>(), requireAtomic: true);
        strict.Should().Throw<InvalidOperationException>().WithMessage("*not RavenDB stores on one database*");
    }

    [Fact]
    public async Task A_create_whose_translation_write_fails_leaves_no_entity()
    {
        if (!Ready()) return;
        var (products, _, failing, bulk) = await FixtureAsync();
        failing.Fail = true;
        var one = new RavenProduct { Guid = Guid.NewGuid(), Name = "stolicka", Code = "c" };
        var a = new RavenProduct { Guid = Guid.NewGuid(), Name = "a", Code = "c" };
        var b = new RavenProduct { Guid = Guid.NewGuid(), Name = "b", Code = "c" };

        var single = async () => await bulk.CreateAsync(one);
        var many = async () => await bulk.CreateAsync(new[] { a, b });

        await single.Should().ThrowAsync<InvalidOperationException>().WithMessage("injected*");
        await many.Should().ThrowAsync<InvalidOperationException>();
        (await products.ReadAsync(one.Guid!.Value)).Should().BeNull();
        (await products.ReadAsync(a.Guid!.Value)).Should().BeNull();
        (await products.ReadAsync(b.Guid!.Value)).Should().BeNull();
    }

    [Fact]
    public async Task An_update_whose_translation_write_fails_rolls_the_entity_write_back()
    {
        if (!Ready()) return;
        var (products, rows, failing, bulk) = await FixtureAsync();
        var a = new RavenProduct { Name = "sk-a", Code = "1" };
        var b = new RavenProduct { Name = "sk-b", Code = "1" };
        await bulk.CreateAsync(new[] { a, b });
        await SettleAsync();
        failing.Fail = true;

        var single = async () => await bulk.UpdateAsync(new RavenProduct { Guid = a.Guid, Name = "sk-a!", Code = "2" });
        var many = async () => await bulk.UpdateAsync(new[]
        {
            new RavenProduct { Guid = a.Guid, Name = "sk-a!", Code = "2" }, new RavenProduct { Guid = b.Guid, Name = "sk-b!", Code = "2" },
        });
        var filtered = async () => await bulk.UpdateAsync(x => x.Code == "1", x => { x.Code = "2"; x.Name += "!"; });

        await single.Should().ThrowAsync<InvalidOperationException>();
        await many.Should().ThrowAsync<InvalidOperationException>();
        await filtered.Should().ThrowAsync<InvalidOperationException>();
        (await products.ReadAsync(a.Guid!.Value))!.Code.Should().Be("1");
        (await products.ReadAsync(b.Guid!.Value))!.Code.Should().Be("1");
        (await ValueAsync(rows, a.Guid!.Value)).Should().Be("sk-a");
        (await ValueAsync(rows, b.Guid!.Value)).Should().Be("sk-b");
    }

    [Fact]
    public async Task A_delete_whose_translation_cleanup_fails_rolls_the_entity_delete_back()
    {
        if (!Ready()) return;
        var (products, rows, failing, bulk) = await FixtureAsync();
        var a = new RavenProduct { Name = "sk-a", Code = "1" };
        var b = new RavenProduct { Name = "sk-b", Code = "1" };
        await bulk.CreateAsync(new[] { a, b });
        await SettleAsync();
        failing.Fail = true;

        var single = async () => await bulk.DeleteAsync(a);
        var many = async () => await bulk.DeleteAsync(new[] { a, b });
        var filtered = async () => await bulk.DeleteAsync(x => x.Code == "1");

        await single.Should().ThrowAsync<InvalidOperationException>();
        await many.Should().ThrowAsync<InvalidOperationException>();
        await filtered.Should().ThrowAsync<InvalidOperationException>();
        (await products.ReadAsync(a.Guid!.Value)).Should().NotBeNull();
        (await products.ReadAsync(b.Guid!.Value)).Should().NotBeNull();
        (await ValueAsync(rows, a.Guid!.Value)).Should().Be("sk-a");
        (await ValueAsync(rows, b.Guid!.Value)).Should().Be("sk-b");
    }

    [Fact]
    public async Task Writes_through_the_boundary_commit_and_a_later_update_of_them_commits_too()
    {
        if (!Ready()) return;
        var (products, rows, _, bulk) = await FixtureAsync();
        var a = new RavenProduct { Name = "sk-a", Code = "1" };
        var b = new RavenProduct { Name = "sk-b", Code = "1" };
        await bulk.CreateAsync(new[] { a, b });
        await SettleAsync();

        // Every document here was written in a cluster transaction (TASK-702 made updating them possible).
        await bulk.UpdateAsync(new RavenProduct { Guid = a.Guid, Name = "sk-a2", Code = "2" });
        await bulk.UpdateAsync(x => x.Code == "1", x => x.Name = "sk-b2");

        (await products.ReadAsync(a.Guid!.Value))!.Code.Should().Be("2");
        (await ValueAsync(rows, a.Guid!.Value)).Should().Be("sk-a2");
        (await ValueAsync(rows, b.Guid!.Value)).Should().Be("sk-b2");
    }

    [Fact]
    public async Task Best_effort_across_two_databases_keeps_the_entity_write()
    {
        if (!Ready()) return;
        var products = Store<RavenProduct>(NewDb());
        var failing = new FailingStore<EntityTranslationModel>(Store<EntityTranslationModel>(NewDb()));
        var boundary = RavenLocalizedWriteBoundary.For(products, failing);
        boundary.IsAtomic.Should().BeFalse();
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<RavenProduct>, RavenProduct>(products, failing, Slovak(), boundary);
        failing.Fail = true;
        var item = new RavenProduct { Guid = Guid.NewGuid(), Name = "sk", Code = "c" };

        var act = async () => await bulk.CreateAsync(item);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await products.ReadAsync(item.Guid!.Value)).Should().NotBeNull("best effort: the entity write stands");
    }

    [Fact]
    public async Task A_callers_unit_of_work_is_joined_and_a_failed_write_poisons_it()
    {
        if (!Ready()) return;
        var (products, rows, failing, bulk) = await FixtureAsync();
        var ok = new RavenProduct { Guid = Guid.NewGuid(), Name = "sk-ok", Code = "c" };

        await using (var uow = RavenDbUnitOfWork.FromStore(products))
        {
            await uow.BeginAsync();
            await bulk.CreateAsync(ok);
            failing.Fail = true;
            var act = async () => await bulk.CreateAsync(new RavenProduct { Name = "sk-fails", Code = "c" });
            await act.Should().ThrowAsync<InvalidOperationException>();

            var commit = async () => await uow.CommitAsync();
            await commit.Should().ThrowAsync<TransactionRollbackOnlyException>("half a write must not be committable");
            uow.IsActive.Should().BeTrue("the refused commit leaves the session for RollbackAsync");
            await uow.RollbackAsync();
        }

        (await products.ReadAsync(ok.Guid!.Value)).Should().BeNull("the joined write before the failure went with the rollback");
        (await ValueAsync(rows, ok.Guid!.Value)).Should().BeNull();
    }

    [Fact]
    public async Task A_callers_unit_of_work_decides_the_joined_writes()
    {
        if (!Ready()) return;
        var (products, rows, _, bulk) = await FixtureAsync();
        var item = new RavenProduct { Guid = Guid.NewGuid(), Name = "sk-a", Code = "c" };

        await using (var uow = RavenDbUnitOfWork.FromStore(products))
        {
            await uow.BeginAsync();
            await bulk.CreateAsync(item);
            await uow.RollbackAsync();
        }
        (await products.ReadAsync(item.Guid!.Value)).Should().BeNull("the boundary joined the caller's session, it did not save its own");
        (await ValueAsync(rows, item.Guid!.Value)).Should().BeNull();

        await using (var uow = RavenDbUnitOfWork.FromStore(products))
        {
            await uow.BeginAsync();
            await bulk.CreateAsync(item);
            await uow.CommitAsync();
        }
        (await products.ReadAsync(item.Guid!.Value)).Should().NotBeNull();
        (await ValueAsync(rows, item.Guid!.Value)).Should().Be("sk-a");
    }

    [Fact]
    public async Task A_session_given_to_the_entity_store_carries_both_writes()
    {
        if (!Ready()) return;
        var (products, rows, _, bulk) = await FixtureAsync();
        var item = new RavenProduct { Guid = Guid.NewGuid(), Name = "sk-given", Code = "c" };

        using (var session = products.DocumentStore!.OpenAsyncSession())
        {
            products.SetTransactionContext(session);
            await bulk.CreateAsync(item);
            products.SetTransactionContext(null);
            // Disposed unsaved: the caller owns the session, and nothing was saved behind its back.
        }
        (await products.ReadAsync(item.Guid!.Value)).Should().BeNull();
        (await ValueAsync(rows, item.Guid!.Value)).Should().BeNull("the translation was written into the caller's session too");

        using (var session = products.DocumentStore!.OpenAsyncSession())
        {
            products.SetTransactionContext(session);
            await bulk.CreateAsync(item);
            products.SetTransactionContext(null);
            await session.SaveChangesAsync();
        }
        (await ValueAsync(rows, item.Guid!.Value)).Should().Be("sk-given");
    }

    [Fact]
    public async Task A_writer_saving_the_key_before_our_session_is_saved_is_retried_once()
    {
        if (!Ready()) return;
        var db = NewDb();
        var products = Store<RavenProduct>(db);
        var rows = Store<EntityTranslationModel>(db);
        await products.InitAsync();
        await rows.InitAsync();
        var guid = Guid.NewGuid();
        var other = Store<EntityTranslationModel>(db);
        // After our create went into the session (the id was free), the other writer saves the same key in a cluster-wide
        // transaction of its own; our save then meets its atomic guard.
        var racing = new RaceStore<EntityTranslationModel>(rows, afterFirstCreate: async () =>
        {
            Task saved;
            using (ExecutionContext.SuppressFlow())
            {
                saved = Task.Run(async () =>
                {
                    await using var unit = RavenDbUnitOfWork.FromStore(other);
                    await unit.BeginAsync();
                    other.SetTransactionContext(unit.Context);
                    await other.CreateAsync(Row(guid, "other writer"));
                    other.SetTransactionContext(null);
                    await unit.CommitAsync();
                });
            }
            await saved;
        });
        var boundary = RavenLocalizedWriteBoundary.For(products, racing);
        boundary.IsAtomic.Should().BeTrue();
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<RavenProduct>, RavenProduct>(products, racing, Slovak(), boundary);

        await bulk.CreateAsync(new RavenProduct { Guid = guid, Name = "ours", Code = "c" });

        racing.Raced.Should().BeTrue("the test must actually have raced");
        _output.WriteLine($"translation reads: {racing.Reads}, collection creates: {racing.Creates}");
        // Each pass reads the existing translation rows once; the second pass may find the other writer's row and update
        // it rather than create, so the passes are counted by their reads.
        racing.Reads.Should().Be(2, "the save was refused and the whole write re-ran once");
        (await products.ReadAsync(guid)).Should().NotBeNull();
        (await ValueAsync(rows, guid)).Should().Be("ours");
    }

    [Fact]
    public void The_sync_wrapper_rolls_back_the_same_way()
    {
        if (!Ready()) return;
        var db = NewDb();
        var products = SyncStore<RavenProduct>(db);
        var rows = SyncStore<EntityTranslationModel>(db);
        products.Init();
        rows.Init();
        var failing = new FailingSyncStore<EntityTranslationModel>(rows);
        var boundary = RavenLocalizedWriteBoundary.For(products, failing);
        boundary.IsAtomic.Should().BeTrue();
        var bulk = new LocalizedBulkStoreWrapper<IBulkStore<RavenProduct>, RavenProduct>(products, failing, Slovak(), boundary);
        var kept = new RavenProduct { Guid = Guid.NewGuid(), Name = "sk-kept", Code = "c" };
        bulk.Create(kept);
        failing.Fail = true;
        var created = new RavenProduct { Guid = Guid.NewGuid(), Name = "sk-new", Code = "c" };

        var create = () => bulk.Create(created);
        var delete = () => bulk.Delete(kept);

        create.Should().Throw<InvalidOperationException>();
        products.Read(created.Guid!.Value).Should().BeNull();
        delete.Should().Throw<InvalidOperationException>();
        products.Read(kept.Guid!.Value).Should().NotBeNull();
        rows.Read(KeyOf(kept.Guid!.Value))!.Value.Should().Be("sk-kept");
    }

    private static EntityTranslationModel Row(Guid entity, string value) => new()
    {
        Guid = KeyOf(entity),
        EntityGuid = entity,
        EntityType = nameof(RavenProduct),
        FieldName = nameof(RavenProduct.Name),
        Culture = "sk",
        Value = value,
        ValueHash = LocalizedValueHash.Of(value),
    };

    public void Dispose()
    {
        foreach (var s in _stores) { try { s.Dispose(); } catch { } }
        if (string.IsNullOrWhiteSpace(_url) || _databases.Count == 0) return;
        try
        {
            using var admin = new DocumentStore { Urls = new[] { _url! } }.Initialize();
            foreach (var db in _databases)
                admin.Maintenance.Server.Send(new DeleteDatabasesOperation(db, hardDelete: true));
        }
        catch
        {
            // Best effort: the names are unique per test.
        }
    }

    /// <summary>
    /// Delegates to a real store. After the first collection create returns it runs <c>afterFirstCreate</c>: another
    /// writer saves the same key before our session is saved.
    /// </summary>
    private sealed class RaceStore<T> : IAsyncBulkStore<T>, IStoreWrapper where T : AbstractModel
    {
        private readonly IAsyncBulkStore<T> _inner;
        private Func<Task>? _afterFirstCreate;

        public RaceStore(IAsyncBulkStore<T> inner, Func<Task> afterFirstCreate)
        {
            _inner = inner;
            _afterFirstCreate = afterFirstCreate;
        }

        public bool Raced { get; private set; }

        /// <summary>Collection creates made through this store.</summary>
        public int Creates { get; private set; }

        /// <summary>Filtered collection reads made through this store.</summary>
        public int Reads { get; private set; }

        public async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            Creates++;
            await _inner.CreateAsync(data, storeDelegate, ct);
            var after = _afterFirstCreate;
            if (after == null) return;
            _afterFirstCreate = null;
            await after();
            Raced = true;
        }

        public object? GetInnerStore() => _inner;
        public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            Reads++;
            return _inner.ReadAsync(filter, orderBy, limit, offset, ct);
        }
        public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default) => _inner.ReadAsync(guid, ct);
        public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => ((IAsyncReadStore<T>)_inner).ReadAsync(filter, ct);
        public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default) => _inner.ReadAsync(ct);
        public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _inner.CountAsync(filter, ct);
        public Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default) => _inner.CreateAsync(data, processDelegate, ct);
        public Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default) => _inner.UpdateAsync(data, processDelegate, ct);
        public Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default) => _inner.UpdateAsync(data, storeDelegate, ct);
        public Task DeleteAsync(T data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);
        public Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default) => _inner.UpdateAsync(filter, updateAction, ct);
        public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default) => _inner.UpdateAsync(filter, updates, ct);
        public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);
        public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default) => _inner.DeleteAsync(filter, ct);
        public Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default) => _inner.SaveAsync(data, processDelegate, ct);
        public Task InitAsync(CancellationToken ct = default) => _inner.InitAsync(ct);
        public Task DestroyAsync(CancellationToken ct = default) => _inner.DestroyAsync(ct);
        public T CreateInstance() => _inner.CreateInstance();
    }

    private sealed class Context : IEntityLocalizationContext
    {
        public CultureInfo CurrentCulture { get; set; } = CultureInfo.InvariantCulture;
        public CultureInfo DefaultCulture { get; set; } = CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// TASK-683: delegates to a real store and, once <see cref="Fail"/> is set, throws on every write, client side, so the
    /// transaction stays usable on every provider. An <see cref="IStoreWrapper"/>, so the boundary must find the RavenDB
    /// store under it.
    /// </summary>
    private sealed class FailingStore<T> : IAsyncBulkStore<T>, IStoreWrapper where T : AbstractModel
    {
        private readonly IAsyncBulkStore<T> _inner;

        public FailingStore(IAsyncBulkStore<T> inner) => _inner = inner;

        public bool Fail { get; set; }

        private Task Write(Func<Task> write) => Fail ? throw new InvalidOperationException("injected translation write failure") : write();

        public object? GetInnerStore() => _inner;
        public Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default) => _inner.ReadAsync(filter, orderBy, limit, offset, ct);
        public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default) => _inner.ReadAsync(guid, ct);
        public Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => ((IAsyncReadStore<T>)_inner).ReadAsync(filter, ct);
        public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default) => _inner.ReadAsync(ct);
        public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _inner.CountAsync(filter, ct);
        public Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default) => Fail ? throw new InvalidOperationException("injected translation write failure") : _inner.CreateAsync(data, processDelegate, ct);
        public Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default) => Write(() => _inner.CreateAsync(data, storeDelegate, ct));
        public Task UpdateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default) => Write(() => _inner.UpdateAsync(data, processDelegate, ct));
        public Task UpdateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default) => Write(() => _inner.UpdateAsync(data, storeDelegate, ct));
        public Task DeleteAsync(T data, CancellationToken ct = default) => Write(() => _inner.DeleteAsync(data, ct));
        public Task UpdateAsync(Expression<Func<T, bool>> filter, Action<T> updateAction, CancellationToken ct = default) => Write(() => _inner.UpdateAsync(filter, updateAction, ct));
        public Task UpdateAsync(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates, CancellationToken ct = default) => Write(() => _inner.UpdateAsync(filter, updates, ct));
        public Task DeleteAsync(IEnumerable<T> data, CancellationToken ct = default) => Write(() => _inner.DeleteAsync(data, ct));
        public Task DeleteAsync(Expression<Func<T, bool>> filter, CancellationToken ct = default) => Write(() => _inner.DeleteAsync(filter, ct));
        public Task<Guid> SaveAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default) => _inner.SaveAsync(data, processDelegate, ct);
        public Task InitAsync(CancellationToken ct = default) => _inner.InitAsync(ct);
        public Task DestroyAsync(CancellationToken ct = default) => _inner.DestroyAsync(ct);
        public T CreateInstance() => _inner.CreateInstance();
    }

    /// <summary>The sync twin of <see cref="FailingStore{T}"/>.</summary>
    private sealed class FailingSyncStore<T> : IBulkStore<T>, IStoreWrapper where T : AbstractModel
    {
        private readonly IBulkStore<T> _inner;

        public FailingSyncStore(IBulkStore<T> inner) => _inner = inner;

        public bool Fail { get; set; }

        private void Write(Action write)
        {
            if (Fail) throw new InvalidOperationException("injected translation write failure");
            write();
        }

        public object? GetInnerStore() => _inner;
        public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null) => _inner.Read(filter, orderBy, limit, offset);
        public T? Read(Guid guid) => _inner.Read(guid);
        public T? Read(Expression<Func<T, bool>>? filter = null) => ((IReadStore<T>)_inner).Read(filter);
        public IEnumerable<T> Read() => _inner.Read();
        public long Count(Expression<Func<T, bool>>? filter = null) => _inner.Count(filter);
        public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null) => Fail ? throw new InvalidOperationException("injected translation write failure") : _inner.Create(data, storeDelegate);
        public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null) => Write(() => _inner.Create(data, storeDelegate));
        public void Update(T data, StoreDataDelegate<T>? storeDelegate = null) => Write(() => _inner.Update(data, storeDelegate));
        public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null) => Write(() => _inner.Update(data, storeDelegate));
        public void Delete(T data) => Write(() => _inner.Delete(data));
        public void Update(Expression<Func<T, bool>> filter, Action<T> updateAction) => Write(() => _inner.Update(filter, updateAction));
        public void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates) => Write(() => _inner.Update(filter, updates));
        public void Delete(IEnumerable<T> data) => Write(() => _inner.Delete(data));
        public void Delete(Expression<Func<T, bool>> filter) => Write(() => _inner.Delete(filter));
        public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null) => _inner.Save(data, storeDelegate);
        public void Init() => _inner.Init();
        public void Destroy() => _inner.Destroy();
        public T CreateInstance() => _inner.CreateInstance();
    }
}
