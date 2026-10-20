using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.SQL.Tests;

// TASK-685's own tables: TASK-632's suite runs in parallel and drops T632Slugs / T632Defaults (rule 61).
[Table("T685Slugs")]
public class T685Slugged : AbstractModel, ISluggable
{
    public string? Name { get; set; }

    [MaxLengthField(100)]
    [IndexedField("UX_T685Slugs_Slug", IsUnique: true)]
    public string? Slug { get; set; }

    public string? GetSlugSource() => Name;
}

[Table("T685Defaults")]
public class T685Defaultable : AbstractModel, IDefaultMarker
{
    public string? Name { get; set; }
    public bool IsDefault { get; set; }

    [IndexedField("UX_T685Defaults_Marker", IsUnique: true, WhereNotNull = new[] { nameof(DefaultMarker) })]
    public bool? DefaultMarker { get; set; }
}

/// <summary>
/// TASK-685, live: a unique refusal inside a caller's transaction. A racing writer on another connection stores the
/// conflicting row right after the decorator's first probe, so the decorator's write is refused and it probes again.
/// What happens next is per provider:
/// <list type="bullet">
/// <item><description>PostgreSQL aborts the transaction, so the re-probe fails. The caller must get the typed
/// <see cref="StoreUniqueViolationException"/>, never the raw <c>25P02</c> error.</description></item>
/// <item><description>MySQL keeps the transaction, but the re-probe reads its REPEATABLE READ snapshot, from before the
/// other writer committed. It finds nothing, and the typed refusal stands.</description></item>
/// <item><description>SQL Server keeps it, and its READ COMMITTED re-probe sees the other writer's row, so the decorator
/// retries in place.</description></item>
/// </list>
/// SQLite is skipped: with one writer, the racer would wait on the caller's lock.
/// </summary>
public abstract class Task685InTransactionLiveTestsBase<DB> : IDisposable where DB : AbstractConnector
{
    protected readonly ITestOutputHelper Output;
    protected Task685InTransactionLiveTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string HostVariable { get; }
    protected abstract Birko.Configuration.Settings Settings();

    /// <summary>True: the decorator retries in place inside the transaction; false: the typed refusal reaches the caller.</summary>
    protected abstract bool RetriesInsideATransaction { get; }

    /// <summary>True: the refusal aborts the transaction, so the decorator's re-probe fails and the typed refusal says so.</summary>
    protected abstract bool ProbeFailsAfterRefusal { get; }

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private AsyncDataBaseBulkStore<DB, T> Store<T>() where T : AbstractModel
    {
        var store = new AsyncDataBaseBulkStore<DB, T>();
        store.SetSettings(Settings());
        return store;
    }

    private DataBaseBulkStore<DB, T> SyncStore<T>() where T : AbstractModel
    {
        var store = new DataBaseBulkStore<DB, T>();
        store.SetSettings(Settings());
        return store;
    }

    private static void Drop(AbstractConnector? connector, Type type)
    {
        try { connector?.DropTable(new[] { type }); } catch { }
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return;
        var probe = Store<T685Slugged>();
        Drop(probe.Connector, typeof(T685Slugged));
        Drop(probe.Connector, typeof(T685Defaultable));
    }

    /// <summary>Another writer, on its own connection and outside the caller's transaction.</summary>
    private Func<Task> OtherWriter<T>(Func<T> make) where T : AbstractModel => () =>
    {
        Task other;
        using (ExecutionContext.SuppressFlow())
        {
            other = Task.Run(async () => await Store<T>().CreateAsync(make()));
        }
        return other;
    };

    private async Task<(AsyncDataBaseBulkStore<DB, T> Store, AsyncRacer<T> Racer)> FreshAsync<T>(Func<T> other, bool afterListRead)
        where T : AbstractModel
    {
        var store = Store<T>();
        Drop(store.Connector, typeof(T));
        await store.InitAsync();
        return (store, new AsyncRacer<T>(store, OtherWriter(other), afterListRead));
    }

    /// <summary>Runs <paramref name="write"/> in the caller's own transaction and asserts the per-provider outcome.</summary>
    private async Task InTransactionAsync<T>(AsyncDataBaseBulkStore<DB, T> store, Func<Task> write, Func<Task> assertRetried, Func<Task> assertRefused, Func<bool> raced)
        where T : AbstractModel
    {
        Exception? thrown = null;
        await using (var uow = SqlUnitOfWork.FromStore(store))
        {
            await uow.BeginAsync();
            try
            {
                await write();
                await uow.CommitAsync();
            }
            catch (Exception error)
            {
                thrown = error;
                if (uow.IsActive) await uow.RollbackAsync();
            }
        }

        raced().Should().BeTrue("the test must actually have raced");
        Output.WriteLine(thrown == null ? "no exception" : thrown.ToString());
        if (RetriesInsideATransaction)
        {
            thrown.Should().BeNull();
            await assertRetried();
        }
        else
        {
            // Rule 9: the type is the contract; a raw driver exception (PostgreSQL's 25P02) is the defect.
            thrown.Should().BeAssignableTo<StoreUniqueViolationException>();
            if (ProbeFailsAfterRefusal)
            {
                thrown!.Message.Should().Contain("could not query the store after the refusal").And.Contain("25P02");
                thrown.InnerException.Should().BeAssignableTo<StoreUniqueViolationException>("the original refusal is kept");
            }
            else
            {
                thrown!.Message.Should().NotContain("could not query", "the probe ran and found nothing, so the refusal stands as thrown");
            }
            await assertRefused();
        }
    }

    [Fact]
    public async Task A_slug_create_racing_inside_the_callers_transaction()
    {
        if (!RequireServer()) return;
        var (store, racer) = await FreshAsync(() => new T685Slugged { Name = "Widget", Slug = "widget" }, afterListRead: false);
        var slugs = new AsyncSluggableStoreWrapper<AsyncRacer<T685Slugged>, T685Slugged>(racer);

        await InTransactionAsync(store,
            () => slugs.CreateAsync(new T685Slugged { Name = "Widget" }),
            async () => (await store.ReadAsync(null, null, null, null)).Select(x => x.Slug).Should().BeEquivalentTo("widget", "widget-2"),
            async () => (await store.ReadAsync(null, null, null, null)).Select(x => x.Slug).Should().Equal("widget"),
            () => racer.Raced);
    }

    [Fact]
    public async Task A_slug_update_racing_inside_the_callers_transaction()
    {
        if (!RequireServer()) return;
        var (store, racer) = await FreshAsync(() => new T685Slugged { Name = "Widget", Slug = "widget" }, afterListRead: false);
        var mine = new T685Slugged { Name = "Gadget", Slug = "gadget" };
        await store.CreateAsync(mine);
        var slugs = new AsyncSluggableStoreWrapper<AsyncRacer<T685Slugged>, T685Slugged>(racer);

        await InTransactionAsync(store,
            () => slugs.UpdateAsync(new T685Slugged { Guid = mine.Guid, Name = "Widget", Slug = "widget" }),
            async () => (await store.ReadAsync(null, null, null, null)).Select(x => x.Slug).Should().BeEquivalentTo("widget", "widget-2"),
            async () => (await store.ReadAsync(null, null, null, null)).Select(x => x.Slug).Should().BeEquivalentTo("widget", "gadget"),
            () => racer.Raced);
    }

    [Fact]
    public async Task A_default_promotion_racing_inside_the_callers_transaction()
    {
        if (!RequireServer()) return;
        var (store, racer) = await FreshAsync(() => new T685Defaultable { Name = "theirs", IsDefault = true, DefaultMarker = true }, afterListRead: true);
        var defaults = new AsyncDefaultStoreWrapper<AsyncRacer<T685Defaultable>, T685Defaultable>(racer);

        await InTransactionAsync(store,
            () => defaults.CreateAsync(new T685Defaultable { Name = "mine", IsDefault = true }),
            async () => (await store.ReadAsync(null, null, null, null)).Count(r => r.IsDefault).Should().Be(1),
            async () => (await store.ReadAsync(null, null, null, null)).Select(r => r.Name).Should().Equal("theirs"),
            () => racer.Raced);
    }

    [Fact]
    public async Task The_sync_slug_create_racing_inside_the_callers_transaction()
    {
        if (!RequireServer()) return;
        var store = Store<T685Slugged>();
        Drop(store.Connector, typeof(T685Slugged));
        await store.InitAsync();
        var sync = SyncStore<T685Slugged>();
        // Schema first: a lazy init inside the caller's transaction would lock the table and block the racer.
        sync.Init();
        var racer = new SyncRacer<T685Slugged>(sync, OtherWriter(() => new T685Slugged { Name = "Widget", Slug = "widget" }), afterListRead: false);
        var slugs = new SluggableStoreWrapper<SyncRacer<T685Slugged>, T685Slugged>(racer);

        await InTransactionAsync(store,
            () => { slugs.Create(new T685Slugged { Name = "Widget" }); return Task.CompletedTask; },
            async () => (await store.ReadAsync(null, null, null, null)).Select(x => x.Slug).Should().BeEquivalentTo("widget", "widget-2"),
            async () => (await store.ReadAsync(null, null, null, null)).Select(x => x.Slug).Should().Equal("widget"),
            () => racer.Raced);
    }

    [Fact]
    public async Task The_sync_default_promotion_racing_inside_the_callers_transaction()
    {
        if (!RequireServer()) return;
        var store = Store<T685Defaultable>();
        Drop(store.Connector, typeof(T685Defaultable));
        await store.InitAsync();
        var sync = SyncStore<T685Defaultable>();
        sync.Init();
        var racer = new SyncRacer<T685Defaultable>(sync, OtherWriter(() => new T685Defaultable { Name = "theirs", IsDefault = true, DefaultMarker = true }), afterListRead: true);
        var defaults = new DefaultStoreWrapper<SyncRacer<T685Defaultable>, T685Defaultable>(racer);

        await InTransactionAsync(store,
            () => { defaults.Create(new T685Defaultable { Name = "mine", IsDefault = true }); return Task.CompletedTask; },
            async () => (await store.ReadAsync(null, null, null, null)).Count(r => r.IsDefault).Should().Be(1),
            async () => (await store.ReadAsync(null, null, null, null)).Select(r => r.Name).Should().Equal("theirs"),
            () => racer.Raced);
    }

    [Fact]
    public async Task Outside_a_transaction_a_racing_slug_create_is_still_retried()
    {
        if (!RequireServer()) return;
        var (store, racer) = await FreshAsync(() => new T685Slugged { Name = "Widget", Slug = "widget" }, afterListRead: false);
        var slugs = new AsyncSluggableStoreWrapper<AsyncRacer<T685Slugged>, T685Slugged>(racer);

        await slugs.CreateAsync(new T685Slugged { Name = "Widget" });

        racer.Raced.Should().BeTrue();
        (await store.ReadAsync(null, null, null, null)).Select(x => x.Slug).Should().BeEquivalentTo("widget", "widget-2");
    }
}

/// <summary>
/// Delegates to a real store; after its first single-row read (the slug probe) or first collection read (the defaults
/// read), it waits for <c>race</c> to commit a conflicting row from another connection. An <see cref="IStoreWrapper"/>,
/// so a decorator that looks beneath wrappers still reaches the real store.
/// </summary>
public sealed class AsyncRacer<T> : IAsyncBulkStore<T>, IStoreWrapper where T : AbstractModel
{
    private readonly IAsyncBulkStore<T> _inner;
    private readonly bool _afterListRead;
    private Func<Task>? _race;

    public AsyncRacer(IAsyncBulkStore<T> inner, Func<Task> race, bool afterListRead)
    {
        _inner = inner;
        _race = race;
        _afterListRead = afterListRead;
    }

    public bool Raced { get; private set; }

    private async Task RaceOnce()
    {
        var race = _race;
        if (race == null) return;
        _race = null;
        await race();
        Raced = true;
    }

    public object? GetInnerStore() => _inner;
    public async Task<IEnumerable<T>> ReadAsync(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
    {
        var result = (await _inner.ReadAsync(filter, orderBy, limit, offset, ct)).ToList();
        if (_afterListRead) await RaceOnce();
        return result;
    }
    public Task<T?> ReadAsync(Guid guid, CancellationToken ct = default) => _inner.ReadAsync(guid, ct);
    public async Task<T?> ReadAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default)
    {
        var result = await ((IAsyncReadStore<T>)_inner).ReadAsync(filter, ct);
        if (!_afterListRead) await RaceOnce();
        return result;
    }
    public Task<IEnumerable<T>> ReadAsync(CancellationToken ct = default) => _inner.ReadAsync(ct);
    public Task<long> CountAsync(Expression<Func<T, bool>>? filter = null, CancellationToken ct = default) => _inner.CountAsync(filter, ct);
    public Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default) => _inner.CreateAsync(data, processDelegate, ct);
    public Task CreateAsync(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null, CancellationToken ct = default) => _inner.CreateAsync(data, storeDelegate, ct);
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

/// <summary>The sync twin of <see cref="AsyncRacer{T}"/>.</summary>
public sealed class SyncRacer<T> : IBulkStore<T>, IStoreWrapper where T : AbstractModel
{
    private readonly IBulkStore<T> _inner;
    private readonly bool _afterListRead;
    private Func<Task>? _race;

    public SyncRacer(IBulkStore<T> inner, Func<Task> race, bool afterListRead)
    {
        _inner = inner;
        _race = race;
        _afterListRead = afterListRead;
    }

    public bool Raced { get; private set; }

    private void RaceOnce()
    {
        var race = _race;
        if (race == null) return;
        _race = null;
        race().GetAwaiter().GetResult();
        Raced = true;
    }

    public object? GetInnerStore() => _inner;
    public IEnumerable<T> Read(Expression<Func<T, bool>>? filter = null, OrderBy<T>? orderBy = null, int? limit = null, int? offset = null)
    {
        var result = _inner.Read(filter, orderBy, limit, offset).ToList();
        if (_afterListRead) RaceOnce();
        return result;
    }
    public T? Read(Guid guid) => _inner.Read(guid);
    public T? Read(Expression<Func<T, bool>>? filter = null)
    {
        var result = ((IReadStore<T>)_inner).Read(filter);
        if (!_afterListRead) RaceOnce();
        return result;
    }
    public IEnumerable<T> Read() => _inner.Read();
    public long Count(Expression<Func<T, bool>>? filter = null) => _inner.Count(filter);
    public Guid Create(T data, StoreDataDelegate<T>? storeDelegate = null) => _inner.Create(data, storeDelegate);
    public void Create(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null) => _inner.Create(data, storeDelegate);
    public void Update(T data, StoreDataDelegate<T>? storeDelegate = null) => _inner.Update(data, storeDelegate);
    public void Update(IEnumerable<T> data, StoreDataDelegate<T>? storeDelegate = null) => _inner.Update(data, storeDelegate);
    public void Delete(T data) => _inner.Delete(data);
    public void Update(Expression<Func<T, bool>> filter, Action<T> updateAction) => _inner.Update(filter, updateAction);
    public void Update(Expression<Func<T, bool>> filter, PropertyUpdate<T> updates) => _inner.Update(filter, updates);
    public void Delete(IEnumerable<T> data) => _inner.Delete(data);
    public void Delete(Expression<Func<T, bool>> filter) => _inner.Delete(filter);
    public Guid Save(T data, StoreDataDelegate<T>? storeDelegate = null) => _inner.Save(data, storeDelegate);
    public void Init() => _inner.Init();
    public void Destroy() => _inner.Destroy();
    public T CreateInstance() => _inner.CreateInstance();
}

public sealed class PostgreSqlTask685InTransactionLiveTests : Task685InTransactionLiveTestsBase<PostgreSQLConnector>
{
    public PostgreSqlTask685InTransactionLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string HostVariable => "BIRKO_PG_HOST";
    protected override bool RetriesInsideATransaction => false;
    protected override bool ProbeFailsAfterRefusal => true;
    protected override Birko.Configuration.Settings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
}

public sealed class MySqlTask685InTransactionLiveTests : Task685InTransactionLiveTestsBase<MySQLConnector>
{
    public MySqlTask685InTransactionLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string HostVariable => "BIRKO_MYSQL_HOST";
    protected override bool RetriesInsideATransaction => false;
    protected override bool ProbeFailsAfterRefusal => false;
    protected override Birko.Configuration.Settings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
}

public sealed class MSSqlTask685InTransactionLiveTests : Task685InTransactionLiveTestsBase<MSSqlConnector>
{
    public MSSqlTask685InTransactionLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string HostVariable => "BIRKO_MSSQL_HOST";
    protected override bool RetriesInsideATransaction => true;
    protected override bool ProbeFailsAfterRefusal => false;
    protected override Birko.Configuration.Settings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
}
