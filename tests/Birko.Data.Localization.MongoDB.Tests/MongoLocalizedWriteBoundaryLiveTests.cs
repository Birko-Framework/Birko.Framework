using System.Globalization;
using System.Linq.Expressions;
using Birko.Data.Exceptions;
using Birko.Data.Localization.Boundaries;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Models;
using Birko.Data.MongoDB.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.UnitOfWork;
using Birko.Data.InMemory.Stores;
using Birko.Data.Patterns.UnitOfWork;
using Birko.Data.Stores;
using FluentAssertions;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Localization.MongoDB.Tests;

public sealed class MongoProduct : AbstractModel, ILocalizable
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public IReadOnlyList<string> GetLocalizableFields() => new[] { nameof(Name) };
}

/// <summary>The sync <c>MongoDBStore</c> takes only <see cref="MongoDBModel"/>s, so the sync path has its own models.</summary>
public sealed class SyncMongoProduct : MongoDBModel, ILocalizable
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public IReadOnlyList<string> GetLocalizableFields() => new[] { nameof(Name) };
}

public sealed class SyncMongoTranslation : MongoDBModel, IEntityTranslation
{
    public Guid EntityGuid { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string Culture { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string ValueHash { get; set; } = string.Empty;
}

/// <summary>
/// TASK-700, live: <see cref="MongoLocalizedWriteBoundary"/> makes the entity write and its translation writes one
/// transaction (the MongoDB twin of TASK-683's SQL tests). Gated on <c>BIRKO_MONGO_HOST</c>; needs a replica set, as the
/// live-tests container is. Each test owns a fresh database.
/// </summary>
public sealed class MongoLocalizedWriteBoundaryLiveTests : IDisposable
{
    private const string HostEnv = "BIRKO_MONGO_HOST";
    private readonly ITestOutputHelper _output;
    private readonly List<string> _databases = new();
    private string? _host;

    public MongoLocalizedWriteBoundaryLiveTests(ITestOutputHelper output) => _output = output;

    private bool Ready()
    {
        _host = Environment.GetEnvironmentVariable(HostEnv);
        if (!string.IsNullOrWhiteSpace(_host)) return true;
        const string message = "SKIPPED: no live MongoDB. Set " + HostEnv + " to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private Settings NewSettings()
    {
        var name = "birko_task700_" + Guid.NewGuid().ToString("N");
        _databases.Add(name);
        return new Settings(_host!, name);
    }

    private static AsyncMongoDBStore<T> Store<T>(Settings settings) where T : AbstractModel
    {
        var store = new AsyncMongoDBStore<T>();
        store.SetSettings(settings);
        return store;
    }

    private static Context Slovak() => new() { CurrentCulture = new CultureInfo("sk"), DefaultCulture = new CultureInfo("en") };

    private async Task<(AsyncMongoDBStore<MongoProduct> Products, AsyncMongoDBStore<EntityTranslationModel> Rows,
        FailingStore<EntityTranslationModel> Failing, AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<MongoProduct>, MongoProduct> Bulk)> FixtureAsync()
    {
        var settings = NewSettings();
        var products = Store<MongoProduct>(settings);
        var rows = Store<EntityTranslationModel>(settings);
        // Collections and indexes first: creating them inside a transaction is not what these tests are about.
        await products.InitAsync();
        await rows.InitAsync();
        await LocalizationMongoIndexes.EnsureAsync(rows);
        var failing = new FailingStore<EntityTranslationModel>(rows);
        var boundary = MongoLocalizedWriteBoundary.For(products, failing);
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<MongoProduct>, MongoProduct>(products, failing, Slovak(), boundary);
        return (products, rows, failing, bulk);
    }

    private static async Task<List<string>> ValuesAsync(AsyncMongoDBStore<EntityTranslationModel> rows)
        => (await rows.ReadAsync(null, null, null, null)).Select(t => t.Value).OrderBy(v => v).ToList();

    [Fact]
    public void The_boundary_is_atomic_only_for_two_mongo_stores_on_one_database()
    {
        if (!Ready()) return;
        var settings = NewSettings();
        var products = Store<MongoProduct>(settings);

        MongoLocalizedWriteBoundary.For(products, new FailingStore<EntityTranslationModel>(Store<EntityTranslationModel>(settings)))
            .IsAtomic.Should().BeTrue("a decorator over a store on the same settings is found through the chain");
        MongoLocalizedWriteBoundary.For(products, Store<EntityTranslationModel>(NewSettings()))
            .IsAtomic.Should().BeFalse("another database is another transaction");
        MongoLocalizedWriteBoundary.For(products, new AsyncInMemoryStore<EntityTranslationModel>()).IsAtomic.Should().BeFalse();
        MongoLocalizedWriteBoundary.For(products, new AsyncMongoDBStore<EntityTranslationModel>())
            .IsAtomic.Should().BeFalse("a store whose settings are not set has no client");
        var strict = () => MongoLocalizedWriteBoundary.For(products, new AsyncInMemoryStore<EntityTranslationModel>(), requireAtomic: true);
        strict.Should().Throw<InvalidOperationException>().WithMessage("*not MongoDB stores on one database*");
    }

    [Fact]
    public async Task A_create_whose_translation_write_fails_leaves_no_entity()
    {
        if (!Ready()) return;
        var (products, _, failing, bulk) = await FixtureAsync();
        failing.Fail = true;

        var single = async () => await bulk.CreateAsync(new MongoProduct { Name = "stolicka", Code = "c" });
        var many = async () => await bulk.CreateAsync(new[] { new MongoProduct { Name = "a", Code = "c" }, new MongoProduct { Name = "b", Code = "c" } });

        await single.Should().ThrowAsync<InvalidOperationException>().WithMessage("injected*");
        await many.Should().ThrowAsync<InvalidOperationException>();
        (await products.CountAsync(null)).Should().Be(0);
    }

    [Fact]
    public async Task An_update_whose_translation_write_fails_rolls_the_entity_write_back()
    {
        if (!Ready()) return;
        var (products, rows, failing, bulk) = await FixtureAsync();
        var a = new MongoProduct { Name = "sk-a", Code = "1" };
        var b = new MongoProduct { Name = "sk-b", Code = "1" };
        await bulk.CreateAsync(new[] { a, b });
        failing.Fail = true;

        var single = async () => await bulk.UpdateAsync(new MongoProduct { Guid = a.Guid, Name = "sk-a!", Code = "2" });
        var many = async () => await bulk.UpdateAsync(new[]
        {
            new MongoProduct { Guid = a.Guid, Name = "sk-a!", Code = "2" }, new MongoProduct { Guid = b.Guid, Name = "sk-b!", Code = "2" },
        });
        var filtered = async () => await bulk.UpdateAsync(x => x.Code == "1", x => { x.Code = "2"; x.Name += "!"; });

        await single.Should().ThrowAsync<InvalidOperationException>();
        await many.Should().ThrowAsync<InvalidOperationException>();
        await filtered.Should().ThrowAsync<InvalidOperationException>();
        (await products.ReadAsync(null, null, null, null)).Select(x => x.Code).Should().Equal("1", "1");
        (await ValuesAsync(rows)).Should().Equal("sk-a", "sk-b");
    }

    [Fact]
    public async Task A_delete_whose_translation_cleanup_fails_rolls_the_entity_delete_back()
    {
        if (!Ready()) return;
        var (products, rows, failing, bulk) = await FixtureAsync();
        var a = new MongoProduct { Name = "sk-a", Code = "1" };
        var b = new MongoProduct { Name = "sk-b", Code = "1" };
        await bulk.CreateAsync(new[] { a, b });
        failing.Fail = true;

        var single = async () => await bulk.DeleteAsync(a);
        var many = async () => await bulk.DeleteAsync(new[] { a, b });
        var filtered = async () => await bulk.DeleteAsync(x => x.Code == "1");

        await single.Should().ThrowAsync<InvalidOperationException>();
        await many.Should().ThrowAsync<InvalidOperationException>();
        await filtered.Should().ThrowAsync<InvalidOperationException>();
        (await products.CountAsync(null)).Should().Be(2);
        (await ValuesAsync(rows)).Should().Equal("sk-a", "sk-b");
    }

    [Fact]
    public async Task Best_effort_across_two_databases_keeps_the_entity_write()
    {
        if (!Ready()) return;
        var products = Store<MongoProduct>(NewSettings());
        var failing = new FailingStore<EntityTranslationModel>(Store<EntityTranslationModel>(NewSettings()));
        var boundary = MongoLocalizedWriteBoundary.For(products, failing);
        boundary.IsAtomic.Should().BeFalse();
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<MongoProduct>, MongoProduct>(products, failing, Slovak(), boundary);
        failing.Fail = true;

        var act = async () => await bulk.CreateAsync(new MongoProduct { Name = "sk", Code = "c" });

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await products.CountAsync(null)).Should().Be(1, "best effort: the entity write stands");
    }

    [Fact]
    public async Task A_callers_unit_of_work_is_joined_and_a_failed_write_poisons_it()
    {
        if (!Ready()) return;
        var (products, rows, failing, bulk) = await FixtureAsync();

        await using (var uow = MongoDbUnitOfWork.FromStore(products))
        {
            await uow.BeginAsync();
            await bulk.CreateAsync(new MongoProduct { Name = "sk-ok", Code = "c" });
            failing.Fail = true;
            var act = async () => await bulk.CreateAsync(new MongoProduct { Name = "sk-fails", Code = "c" });
            await act.Should().ThrowAsync<InvalidOperationException>();

            var commit = async () => await uow.CommitAsync();
            await commit.Should().ThrowAsync<TransactionRollbackOnlyException>("half a write must not be committable");
            uow.IsActive.Should().BeTrue("the refused commit leaves the transaction for RollbackAsync");
            await uow.RollbackAsync();
        }

        (await products.CountAsync(null)).Should().Be(0, "the joined write before the failure went with the rollback");
        (await rows.CountAsync(null)).Should().Be(0);
    }

    [Fact]
    public async Task A_callers_unit_of_work_decides_the_joined_writes()
    {
        if (!Ready()) return;
        var (products, rows, _, bulk) = await FixtureAsync();
        var item = new MongoProduct { Name = "sk-a", Code = "c" };

        await using (var uow = MongoDbUnitOfWork.FromStore(products))
        {
            await uow.BeginAsync();
            await bulk.CreateAsync(item);
            await uow.RollbackAsync();
        }
        (await products.CountAsync(null)).Should().Be(0, "the boundary joined the caller's transaction, it did not commit its own");
        (await rows.CountAsync(null)).Should().Be(0);

        await using (var uow = MongoDbUnitOfWork.FromStore(products))
        {
            await uow.BeginAsync();
            await bulk.CreateAsync(item);
            await uow.CommitAsync();
        }
        (await products.CountAsync(null)).Should().Be(1);
        (await ValuesAsync(rows)).Should().Equal("sk-a");
    }

    [Fact]
    public async Task A_session_given_to_the_entity_store_carries_both_writes()
    {
        if (!Ready()) return;
        var (products, rows, _, bulk) = await FixtureAsync();
        using var session = await products.Client!.Client.StartSessionAsync();
        session.StartTransaction();
        products.SetTransactionContext(session);

        await bulk.CreateAsync(new MongoProduct { Name = "sk-given", Code = "c" });
        await session.AbortTransactionAsync();
        products.SetTransactionContext(null);

        (await products.CountAsync(null)).Should().Be(0);
        (await rows.CountAsync(null)).Should().Be(0, "the translation was written in the caller's session too");
    }

    [Fact]
    public async Task A_writer_committing_between_our_read_and_create_is_a_write_conflict_retried_once()
    {
        if (!Ready()) return;
        var settings = NewSettings();
        var products = Store<MongoProduct>(settings);
        var rows = Store<EntityTranslationModel>(settings);
        await products.InitAsync();
        await rows.InitAsync();
        await LocalizationMongoIndexes.EnsureAsync(rows);
        var guid = Guid.NewGuid();
        // The other writer commits on its own (a random Guid, so only the key index refuses ours), after our read found
        // nothing and before our create. Measured: our transaction's snapshot predates that commit, so MongoDB answers a
        // write conflict, not a duplicate key.
        var racing = new RaceStore<EntityTranslationModel>(rows, beforeCreate: async () =>
        {
            Task other;
            using (ExecutionContext.SuppressFlow())
            {
                other = Task.Run(() => Store<EntityTranslationModel>(settings).CreateAsync(Row(guid, "other writer")));
            }
            await other;
        });
        var boundary = MongoLocalizedWriteBoundary.For(products, racing);
        boundary.IsAtomic.Should().BeTrue();
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<MongoProduct>, MongoProduct>(products, racing, Slovak(), boundary);

        await bulk.CreateAsync(new MongoProduct { Guid = guid, Name = "ours", Code = "c" });

        racing.FirstCreateFailure.Should().NotBeNull("the test must actually have raced");
        _output.WriteLine($"first refusal: {racing.FirstCreateFailure!.GetType().Name}: {racing.FirstCreateFailure.Message}");
        IsTransient(racing.FirstCreateFailure).Should().BeTrue();
        (await products.CountAsync(null)).Should().Be(1, "the rolled-back first pass left no second entity");
        (await rows.ReadAsync(t => t.EntityGuid == guid, null, null, null)).Select(t => t.Value).Should().Equal("ours");
    }

    [Fact]
    public async Task A_write_conflict_with_an_open_transaction_is_retried_once_in_a_new_transaction()
    {
        if (!Ready()) return;
        var settings = NewSettings();
        var products = Store<MongoProduct>(settings);
        var rows = Store<EntityTranslationModel>(settings);
        await products.InitAsync();
        await rows.InitAsync();
        await LocalizationMongoIndexes.EnsureAsync(rows);
        var guid = Guid.NewGuid();
        var otherStore = Store<EntityTranslationModel>(settings);
        IClientSessionHandle? other = null;
        // The other writer has inserted the key in a transaction it has not committed when our create runs, and commits
        // once ours was refused.
        var racing = new RaceStore<EntityTranslationModel>(rows,
            beforeCreate: async () =>
            {
                Task started;
                using (ExecutionContext.SuppressFlow())
                {
                    started = Task.Run(async () =>
                    {
                        other = await otherStore.Client!.Client.StartSessionAsync();
                        other.StartTransaction();
                        otherStore.SetTransactionContext(other);
                        await otherStore.CreateAsync(Row(guid, "other writer"));
                        otherStore.SetTransactionContext(null);
                    });
                }
                await started;
            },
            afterCreateFailure: async () => await other!.CommitTransactionAsync());
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<MongoProduct>, MongoProduct>(
            products, racing, Slovak(), MongoLocalizedWriteBoundary.For(products, racing));

        try
        {
            await bulk.CreateAsync(new MongoProduct { Guid = guid, Name = "ours", Code = "c" });
        }
        finally
        {
            other?.Dispose();
        }

        racing.FirstCreateFailure.Should().NotBeNull("the test must actually have raced");
        _output.WriteLine($"first refusal: {racing.FirstCreateFailure!.GetType().Name}: {racing.FirstCreateFailure.Message}");
        IsTransient(racing.FirstCreateFailure).Should().BeTrue();
        (await products.CountAsync(null)).Should().Be(1);
        (await rows.ReadAsync(t => t.EntityGuid == guid, null, null, null)).Select(t => t.Value).Should().Equal("ours");
    }

    [Fact]
    public void The_sync_wrapper_rolls_back_the_same_way()
    {
        if (!Ready()) return;
        var settings = NewSettings();
        var products = new MongoDBStore<SyncMongoProduct>();
        products.SetSettings(settings);
        var rows = new MongoDBStore<SyncMongoTranslation>();
        rows.SetSettings(settings);
        products.Init();
        rows.Init();
        var failing = new FailingSyncStore<SyncMongoTranslation>(rows);
        var boundary = MongoLocalizedWriteBoundary.For(products, failing);
        boundary.IsAtomic.Should().BeTrue();
        var bulk = new LocalizedBulkStoreWrapper<IBulkStore<SyncMongoProduct>, SyncMongoProduct, SyncMongoTranslation>(products, failing, Slovak(), boundary);
        var kept = new SyncMongoProduct { Name = "sk-kept", Code = "c" };
        bulk.Create(kept);
        failing.Fail = true;

        var create = () => bulk.Create(new SyncMongoProduct { Name = "sk-new", Code = "c" });
        var delete = () => bulk.Delete(kept);

        // Checked after each write: a leaked create and a leaked delete would otherwise leave one row and cancel out.
        create.Should().Throw<InvalidOperationException>();
        products.Read(null, null, null, null).Select(x => x.Name).Should().Equal("sk-kept");
        delete.Should().Throw<InvalidOperationException>();
        products.Read(null, null, null, null).Select(x => x.Name).Should().Equal("sk-kept");
        rows.Read(null, null, null, null).Select(x => x.Value).Should().Equal("sk-kept");
    }

    private static bool IsTransient(Exception error)
    {
        for (Exception? e = error; e != null; e = e.InnerException)
        {
            if (e is MongoException mongo && mongo.HasErrorLabel("TransientTransactionError")) return true;
        }
        return false;
    }

    [Fact]
    public async Task A_key_held_out_of_sight_is_not_retried()
    {
        if (!Ready()) return;
        var (products, rows, _, _) = await FixtureAsync();
        var guid = Guid.NewGuid();
        await rows.CreateAsync(Row(guid, "held"));
        // A translation store that cannot see the row holding the key, as a narrower scope would.
        var blind = new RaceStore<EntityTranslationModel>(rows, beforeCreate: () => Task.CompletedTask, hideReads: true);
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<MongoProduct>, MongoProduct>(
            products, blind, Slovak(), MongoLocalizedWriteBoundary.For(products, blind));

        var act = async () => await bulk.CreateAsync(new MongoProduct { Guid = guid, Name = "ours", Code = "c" });

        await act.Should().ThrowAsync<StoreUniqueViolationException>();
        blind.Creates.Should().Be(1, "a key the write cannot see is not a race, so the write is not re-run");
        (await products.CountAsync(null)).Should().Be(0);
    }

    private static EntityTranslationModel Row(Guid entity, string value) => new()
    {
        Guid = Guid.NewGuid(),
        EntityGuid = entity,
        EntityType = nameof(MongoProduct),
        FieldName = nameof(MongoProduct.Name),
        Culture = "sk",
        Value = value,
        ValueHash = LocalizedValueHash.Of(value),
    };

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(_host)) return;
        try
        {
            var client = new MongoClient($"mongodb://{_host}:27017");
            foreach (var db in _databases) client.DropDatabase(db);
        }
        catch
        {
            // Best effort: the names are unique per test.
        }
    }

    /// <summary>
    /// Delegates to a real store. Before the first collection create it runs <c>beforeCreate</c> (another writer takes the
    /// key); when that create fails it records the failure and runs <c>afterCreateFailure</c>.
    /// </summary>
    private sealed class RaceStore<T> : IAsyncBulkStore<T>, IStoreWrapper where T : AbstractModel
    {
        private readonly IAsyncBulkStore<T> _inner;
        private Func<Task>? _beforeCreate;
        private readonly Func<Task>? _afterCreateFailure;
        private readonly bool _hideReads;

        public RaceStore(IAsyncBulkStore<T> inner, Func<Task> beforeCreate, Func<Task>? afterCreateFailure = null, bool hideReads = false)
        {
            _inner = inner;
            _beforeCreate = beforeCreate;
            _afterCreateFailure = afterCreateFailure;
            _hideReads = hideReads;
        }

        public Exception? FirstCreateFailure { get; private set; }

        /// <summary>Collection creates made through this store.</summary>
        public int Creates { get; private set; }

        public async Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default)
        {
            Creates++;
            var before = _beforeCreate;
            if (before == null)
            {
                await _inner.CreateAsync(data, storeDelegate, ct);
                return;
            }
            _beforeCreate = null;
            await before();
            try
            {
                await _inner.CreateAsync(data, storeDelegate, ct);
            }
            catch (Exception failure)
            {
                FirstCreateFailure = failure;
                if (_afterCreateFailure != null) await _afterCreateFailure();
                throw;
            }
        }

        public object? GetInnerStore() => _inner;
        public async Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
            => _hideReads ? Array.Empty<T>() : await _inner.ReadAsync(filter, orderBy, limit, offset, ct);
        public async Task<T?> ReadAsync(Guid guid, CancellationToken ct = default) => _hideReads ? null : await _inner.ReadAsync(guid, ct);
        public async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _hideReads ? null : await ((IAsyncReadStore<T>)_inner).ReadAsync(filter, ct);
        public async Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default) => _hideReads ? Array.Empty<T>() : await _inner.ReadAsync(ct);
        public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _hideReads ? Task.FromResult(0L) : _inner.CountAsync(filter, ct);
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
    /// transaction stays usable on every provider. An <see cref="IStoreWrapper"/>, so the boundary must find the MongoDB
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
