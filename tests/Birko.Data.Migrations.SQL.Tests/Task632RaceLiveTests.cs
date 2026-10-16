using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.SQL;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.SQL.Tests;

[Table("T632Slugs")]
public class T632Slugged : AbstractModel, ISluggable
{
    public string? Name { get; set; }

    [MaxLengthField(100)]
    [IndexedField("UX_T632Slugs_Slug", IsUnique: true)]
    public string? Slug { get; set; }

    public string? GetSlugSource() => Name;
}

[Table("T632Defaults")]
public class T632Defaultable : AbstractModel, IDefaultMarker
{
    public string? Name { get; set; }
    public bool IsDefault { get; set; }

    [IndexedField("UX_T632Defaults_Marker", IsUnique: true, WhereNotNull = new[] { nameof(DefaultMarker) })]
    public bool? DefaultMarker { get; set; }
}

/// <summary>
/// A real store whose first two single-item creates wait for each other, so two callers have both probed (slug) or both
/// unset the other defaults (default) before either writes: the race, made deterministic.
/// </summary>
public sealed class GatedStore<DB, T> : AsyncDataBaseBulkStore<DB, T> where DB : AbstractConnector where T : AbstractModel
{
    private int _arrivals;
    private readonly CountdownEvent _gate = new(2);

    public override async Task<Guid> CreateAsync(T data, StoreDataDelegate<T>? processDelegate = null, CancellationToken ct = default)
    {
        if (Interlocked.Increment(ref _arrivals) <= 2)
        {
            _gate.Signal();
            _gate.Wait(TimeSpan.FromSeconds(10));
        }
        return await base.CreateAsync(data, processDelegate, ct);
    }
}

/// <summary>
/// TASK-632 (SH-M327, SH-M328), live: two concurrent creates of one slug source end with two distinct slugs, and two
/// concurrent promotions end with one default — the storage refuses the loser through the unique index the model
/// declares, and the decorator retries.
/// </summary>
public abstract class Task632RaceLiveTestsBase<DB> : IDisposable where DB : AbstractConnector
{
    protected readonly ITestOutputHelper Output;
    protected Task632RaceLiveTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract Birko.Configuration.Settings Settings();

    protected bool RequireServer()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private GatedStore<DB, T> Store<T>() where T : AbstractModel
    {
        var store = new GatedStore<DB, T>();
        store.SetSettings(Settings());
        return store;
    }

    private static void Drop(AbstractConnector? connector, Type type)
    {
        try { connector?.DropTable(new[] { type }); } catch { }
    }

    public void Dispose()
    {
        if (HostVariable != null && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return;
        var probe = new AsyncDataBaseBulkStore<DB, T632Slugged>();
        probe.SetSettings(Settings());
        Drop(probe.Connector, typeof(T632Slugged));
        Drop(probe.Connector, typeof(T632Defaultable));
    }

    [Fact]
    public async Task Two_concurrent_creates_of_one_slug_get_two_slugs()
    {
        if (!RequireServer()) return;
        var store = Store<T632Slugged>();
        Drop(store.Connector, typeof(T632Slugged));
        await store.InitAsync();
        var slugs = new AsyncSluggableStoreWrapper<GatedStore<DB, T632Slugged>, T632Slugged>(store);

        var a = new T632Slugged { Name = "Widget" };
        var b = new T632Slugged { Name = "Widget" };
        await Task.WhenAll(Task.Run(() => slugs.CreateAsync(a)), Task.Run(() => slugs.CreateAsync(b)));

        var stored = (await store.ReadAsync(x => x.Name == "Widget", null, null, null)).Select(x => x.Slug).ToList();
        Output.WriteLine(string.Join(", ", stored));
        stored.Should().HaveCount(2).And.OnlyHaveUniqueItems().And.Contain("widget");
    }

    [Fact]
    public async Task Two_concurrent_promotions_leave_one_default()
    {
        if (!RequireServer()) return;
        var store = Store<T632Defaultable>();
        Drop(store.Connector, typeof(T632Defaultable));
        await store.InitAsync();
        var defaults = new AsyncDefaultStoreWrapper<GatedStore<DB, T632Defaultable>, T632Defaultable>(store);

        await Task.WhenAll(
            Task.Run(() => defaults.CreateAsync(new T632Defaultable { Name = "a", IsDefault = true })),
            Task.Run(() => defaults.CreateAsync(new T632Defaultable { Name = "b", IsDefault = true })));

        var rows = (await store.ReadAsync(null, null, null, null)).ToList();
        rows.Should().HaveCount(2);
        rows.Count(r => r.IsDefault).Should().Be(1, "at most one default, whoever wins");
        rows.Single(r => r.IsDefault).DefaultMarker.Should().BeTrue();
        rows.Single(r => !r.IsDefault).DefaultMarker.Should().BeNull("the marker is true or null, never false");
    }
}

public sealed class SqliteTask632RaceLiveTests : Task632RaceLiveTestsBase<SqLiteConnector>
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t632-{Guid.NewGuid():N}.db");
    public SqliteTask632RaceLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override Birko.Configuration.Settings Settings() => new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
}

public sealed class PostgreSqlTask632RaceLiveTests : Task632RaceLiveTestsBase<PostgreSQLConnector>
{
    public PostgreSqlTask632RaceLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override Birko.Configuration.Settings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
}

public sealed class MySqlTask632RaceLiveTests : Task632RaceLiveTestsBase<MySQLConnector>
{
    public MySqlTask632RaceLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override Birko.Configuration.Settings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
}

public sealed class MSSqlTask632RaceLiveTests : Task632RaceLiveTestsBase<MSSqlConnector>
{
    public MSSqlTask632RaceLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override Birko.Configuration.Settings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
}
