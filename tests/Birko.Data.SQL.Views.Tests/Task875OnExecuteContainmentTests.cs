using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Models;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.IndexManagement;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.IndexManagement;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.IndexManagement;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SQLite.IndexManagement;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-875 — <c>OnExecute</c> is a diagnostic channel: a subscriber that throws does not change what the operation does
/// (rule 25), every statement the connector sends reaches it, and an error <c>ExecuteReader</c> raises takes the same
/// path as one <c>Read()</c> raises.
/// <para>
/// Measured before, on SQLite, PostgreSQL 16, MySQL 8.4 and SQL Server 2022, sync and async (the <c>MEASURE</c> lines):
/// a throwing subscriber failed every operation in the table below with the subscriber's own exception, and no write was
/// applied, because <c>AnnounceCommand</c> ran inside the runner's <c>try</c>, before the statement, and the provider's
/// <c>OnException</c> handler rethrew it. The bulk writes (store collection create / update / delete) and the
/// <c>IIndexManager</c> statements never reached <c>OnExecute</c> at all, and the base multi-row insert announced its
/// first row only. A read whose <c>ExecuteReader</c> failed (a column the table does not have) reached the caller without
/// <see cref="AbstractConnector.CommandTextDataKey"/>, which every other failed statement carries.
/// </para>
/// </summary>
[Collection("T875")]
public abstract class Task875OnExecuteContainmentTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task875OnExecuteContainmentTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract IBulkStore<T> SyncStore<T>(PasswordSettings settings) where T : AbstractModel;
    protected abstract IAsyncBulkStore<T> AsyncStore<T>(PasswordSettings settings) where T : AbstractModel;
    protected abstract IIndexManager IndexManager(AbstractConnector connector);

    /// <summary>
    /// Whether a bulk insert's text carries the row values. PostgreSQL's binary <c>COPY</c> and SQL Server's
    /// <c>SqlBulkCopy</c> send the rows as data after one statement, so their text names the table and columns only.
    /// </summary>
    protected virtual bool BulkInsertTextHasValues => true;

    [Table("T875Rows")]
    public class T875Row : AbstractModel
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
    }

    [Table("T875Fresh")]
    public class T875Fresh : AbstractModel
    {
        public string? Code { get; set; }
    }

    [Table("T875Narrow")]
    public class T875Narrow : AbstractModel
    {
        public string? Code { get; set; }
    }

    /// <summary>Reads <c>T875Narrow</c> with a column the table does not have, so <c>ExecuteReader</c> fails.</summary>
    [Table("T875Narrow")]
    public class T875Wide : AbstractModel
    {
        public string? Code { get; set; }
        public int Extra { get; set; }
    }

    public sealed class SubscriberBoom : Exception
    {
        public SubscriberBoom() : base("T875: the OnExecute subscriber threw") { }
    }

    public static readonly string[] Operations =
    {
        "Create", "CreateAsync", "Read", "ReadAsync", "Read(filter)", "ReadAsync(filter)", "Count", "CountAsync",
        "Update", "UpdateAsync", "Update(filter, set)", "UpdateAsync(filter, set)", "Update(filter, action)",
        "UpdateAsync(filter, action)", "Delete", "DeleteAsync", "Delete(filter)", "DeleteAsync(filter)",
        "Create(many)", "CreateAsync(many)", "Update(many)", "UpdateAsync(many)", "Delete(many)", "DeleteAsync(many)",
        "connector.Insert(many)", "connector.InsertAsync(many)", "schema ensure", "IndexManager",
    };

    public static IEnumerable<object[]> OperationCases => Operations.Select(o => new object[] { o });

    private PasswordSettings? _settings;
    private IBulkStore<T875Row>? _sync;
    private IAsyncBulkStore<T875Row>? _async;
    private AbstractConnector? _connector;

    private bool Live()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    public async Task InitializeAsync()
    {
        if (!Live()) return;
        _settings = Settings();
        _sync = SyncStore<T875Row>(_settings);
        _async = AsyncStore<T875Row>(_settings);
        _connector = ((ISqlConnectedStore)_async).Connector!;
        ((ISqlConnectedStore)_sync).Connector.Should().BeSameAs(_connector, "the sync and async stores share the cached connector");
        Clean();
        await _async.CreateAsync(new T875Row { Code = "seed" });
        _sync.Create(new T875Row { Code = "seed-sync" });
    }

    public Task DisposeAsync()
    {
        if (_settings != null) Clean();
        return Task.CompletedTask;
    }

    private void Clean()
    {
        try { _connector!.DropTable(new[] { typeof(T875Row), typeof(T875Fresh), typeof(T875Narrow) }); } catch { }
    }

    private static string Marker() => "m875-" + Guid.NewGuid().ToString("N")[..12];

    private async Task<int> CountCode(string code) => (int)await _async!.CountAsync(x => x.Code == code);

    /// <summary>An operation: what it does, whether it did it, and the values its statements carry.</summary>
    private sealed record Op(Func<Task> Act, Func<Task<bool>> Applied, string Table, IReadOnlyList<string> Markers);

    private static Func<Task> Sync(Action action) => () => { action(); return Task.CompletedTask; };

    /// <summary>Arranges <paramref name="name"/> with no subscriber attached, and returns it ready to run.</summary>
    private async Task<Op> Arrange(string name)
    {
        var sync = _sync!;
        var async = _async!;
        var m = Marker();
        T875Row Seeded() { var r = new T875Row { Code = m, Hits = 1 }; async.CreateAsync(r).GetAwaiter().GetResult(); return r; }
        List<T875Row> Many(bool seed)
        {
            var rows = Enumerable.Range(0, 3).Select(i => new T875Row { Code = m + "-" + i, Hits = 1 }).ToList();
            if (seed) async.CreateAsync(rows).GetAwaiter().GetResult();
            return rows;
        }
        async Task<bool> AllHits(IEnumerable<T875Row> rows, int hits)
        {
            foreach (var row in rows)
            {
                var stored = await async.ReadAsync(row.Guid!.Value);
                if (stored == null || stored.Hits != hits) return false;
            }
            return true;
        }
        async Task<bool> NoneLeft(IEnumerable<T875Row> rows)
        {
            foreach (var row in rows)
                if (await async.ReadAsync(row.Guid!.Value) != null) return false;
            return true;
        }
        string[] Codes(IEnumerable<T875Row> rows) => rows.Select(r => r.Code!).ToArray();
        string[] Ids(IEnumerable<T875Row> rows) => rows.Select(r => r.Guid!.Value.ToString("D")).ToArray();
        await Task.CompletedTask;

        switch (name)
        {
            case "Create":
                return new(Sync(() => sync.Create(new T875Row { Code = m })), async () => await CountCode(m) == 1, "T875Rows", new[] { m });
            case "CreateAsync":
                return new(() => async.CreateAsync(new T875Row { Code = m }), async () => await CountCode(m) == 1, "T875Rows", new[] { m });
            case "Read":
            case "ReadAsync":
            {
                var row = Seeded();
                T875Row? read = null;
                Func<Task> act = name == "Read"
                    ? Sync(() => read = sync.Read(row.Guid!.Value))
                    : async () => read = await async.ReadAsync(row.Guid!.Value);
                return new(act, () => Task.FromResult(read?.Code == m), "T875Rows", new[] { row.Guid!.Value.ToString("D") });
            }
            case "Read(filter)":
            case "ReadAsync(filter)":
            {
                Seeded();
                List<T875Row>? read = null;
                Func<Task> act = name == "Read(filter)"
                    ? Sync(() => read = sync.Read(x => x.Code == m).ToList())
                    : async () => read = (await async.ReadAsync(x => x.Code == m)).ToList();
                return new(act, () => Task.FromResult(read?.Count == 1), "T875Rows", new[] { m });
            }
            case "Count":
            case "CountAsync":
            {
                Seeded();
                long count = -1;
                Func<Task> act = name == "Count"
                    ? Sync(() => count = sync.Count(x => x.Code == m))
                    : async () => count = await async.CountAsync(x => x.Code == m);
                return new(act, () => Task.FromResult(count == 1), "T875Rows", new[] { m });
            }
            case "Update":
            case "UpdateAsync":
            {
                var row = Seeded();
                row.Hits = 5;
                Func<Task> act = name == "Update" ? Sync(() => sync.Update(row)) : () => async.UpdateAsync(row);
                return new(act, () => AllHits(new[] { row }, 5), "T875Rows", new[] { m });
            }
            case "Update(filter, set)":
            case "UpdateAsync(filter, set)":
            {
                var row = Seeded();
                Func<Task> act = name == "Update(filter, set)"
                    ? Sync(() => sync.Update(x => x.Code == m, new PropertyUpdate<T875Row>().Set(x => x.Hits, 6)))
                    : () => async.UpdateAsync(x => x.Code == m, new PropertyUpdate<T875Row>().Set(x => x.Hits, 6));
                return new(act, () => AllHits(new[] { row }, 6), "T875Rows", new[] { m });
            }
            case "Update(filter, action)":
            case "UpdateAsync(filter, action)":
            {
                var row = Seeded();
                Func<Task> act = name == "Update(filter, action)"
                    ? Sync(() => sync.Update(x => x.Code == m, r => r.Hits = 7))
                    : () => async.UpdateAsync(x => x.Code == m, r => r.Hits = 7);
                return new(act, () => AllHits(new[] { row }, 7), "T875Rows", new[] { m });
            }
            case "Delete":
            case "DeleteAsync":
            {
                var row = Seeded();
                Func<Task> act = name == "Delete" ? Sync(() => sync.Delete(row)) : () => async.DeleteAsync(row);
                return new(act, () => NoneLeft(new[] { row }), "T875Rows", new[] { row.Guid!.Value.ToString("D") });
            }
            case "Delete(filter)":
            case "DeleteAsync(filter)":
            {
                var row = Seeded();
                Func<Task> act = name == "Delete(filter)" ? Sync(() => sync.Delete(x => x.Code == m)) : () => async.DeleteAsync(x => x.Code == m);
                return new(act, () => NoneLeft(new[] { row }), "T875Rows", new[] { m });
            }
            case "Create(many)":
            case "CreateAsync(many)":
            {
                var rows = Many(seed: false);
                Func<Task> act = name == "Create(many)" ? Sync(() => sync.Create(rows)) : () => async.CreateAsync(rows);
                return new(act, async () => (await async.CountAsync(x => x.Code!.StartsWith(m))) == 3, "T875Rows",
                    BulkInsertTextHasValues ? Codes(rows) : Array.Empty<string>());
            }
            case "Update(many)":
            case "UpdateAsync(many)":
            {
                var rows = Many(seed: true);
                foreach (var row in rows) row.Hits = 9;
                Func<Task> act = name == "Update(many)" ? Sync(() => sync.Update(rows)) : () => async.UpdateAsync(rows);
                return new(act, () => AllHits(rows, 9), "T875Rows", Ids(rows));
            }
            case "Delete(many)":
            case "DeleteAsync(many)":
            {
                var rows = Many(seed: true);
                Func<Task> act = name == "Delete(many)" ? Sync(() => sync.Delete(rows)) : () => async.DeleteAsync(rows);
                return new(act, () => NoneLeft(rows), "T875Rows", Ids(rows));
            }
            case "connector.Insert(many)":
            case "connector.InsertAsync(many)":
            {
                var rows = Many(seed: false);
                foreach (var row in rows) row.Guid = Guid.NewGuid();
                var table = DataBase.LoadTable(typeof(T875Row));
                var connector = (AbstractAsyncConnector)_connector!;
                Func<Task> act = name == "connector.Insert(many)"
                    ? Sync(() => connector.Insert(table, rows.Cast<object>()))
                    : () => connector.InsertAsync(table, rows.Cast<object>());
                return new(act, async () => (await async.CountAsync(x => x.Code!.StartsWith(m))) == 3, "T875Rows", Codes(rows));
            }
            case "schema ensure":
            {
                try { _connector!.DropTable(new[] { typeof(T875Fresh) }); } catch { }
                var fresh = AsyncStore<T875Fresh>(_settings!);
                return new(() => fresh.CreateAsync(new T875Fresh { Code = m }),
                    async () => (await AsyncStore<T875Fresh>(_settings!).CountAsync(x => x.Code == m)) == 1, "T875Fresh", new[] { m });
            }
            case "IndexManager":
            {
                var index = "ix875_" + Guid.NewGuid().ToString("N")[..8];
                var manager = IndexManager(_connector!);
                var definition = new IndexDefinition { Name = index, Fields = new[] { IndexField.Ascending("Hits") } };
                return new(() => manager.CreateAsync(definition, "T875Rows"),
                    () => manager.ExistsAsync(index, "T875Rows"), "T875Rows", new[] { index });
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }

    private List<string> TextsNaming(IEnumerable<string> texts, string table)
        => texts.Where(t => t.Contains(table, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Rule 25: with a subscriber that throws registered FIRST, the operation completes and applies, a later subscriber
    /// still receives the statement, and the failure is recorded on <see cref="AbstractConnector.SubscriberFailures"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(OperationCases))]
    public async Task A_throwing_subscriber_does_not_change_the_outcome(string operation)
    {
        if (_settings == null) return;
        var op = await Arrange(operation);
        var received = new List<string>();
        OnExecute boom = _ => throw new SubscriberBoom();
        OnExecute record = text => { lock (received) received.Add(text); };
        _connector!.OnExecute += boom;
        _connector.OnExecute += record;
        Exception? thrown = null;
        try { await op.Act(); }
        catch (Exception ex) { thrown = ex; }
        finally
        {
            _connector.OnExecute -= boom;
            _connector.OnExecute -= record;
        }
        var applied = await op.Applied();
        Output.WriteLine($"MEASURE {Provider} | {operation} | throwing subscriber | thrown: {thrown?.GetType().Name ?? "none"} | applied: {applied} | later subscriber got {TextsNaming(received, op.Table).Count}");

        thrown.Should().BeNull($"{operation}: a diagnostic subscriber's exception must not reach the caller");
        applied.Should().BeTrue($"{operation} must still do what it was asked");
        TextsNaming(received, op.Table).Should().NotBeEmpty($"{operation}: a later subscriber still receives the statement");
        _connector.SubscriberFailures.Should().Contain(f => f.Channel == nameof(AbstractConnector.OnExecute) && f.Error is SubscriberBoom);
    }

    /// <summary>Every statement an operation sends reaches <c>OnExecute</c>, with the values it carries.</summary>
    [Theory]
    [MemberData(nameof(OperationCases))]
    public async Task Every_statement_reaches_OnExecute(string operation)
    {
        if (_settings == null) return;
        var op = await Arrange(operation);
        var received = new List<string>();
        OnExecute record = text => { lock (received) received.Add(text); };
        _connector!.OnExecute += record;
        try { await op.Act(); }
        finally { _connector.OnExecute -= record; }
        var texts = TextsNaming(received, op.Table);
        var missing = op.Markers.Where(m => !texts.Any(t => t.Contains(m, StringComparison.Ordinal))).ToList();
        Output.WriteLine($"MEASURE {Provider} | {operation} | announced {texts.Count} | values missing {missing.Count}/{op.Markers.Count}");
        foreach (var text in texts) Output.WriteLine("  " + text);

        (await op.Applied()).Should().BeTrue();
        texts.Should().NotBeEmpty($"{operation} sends a statement");
        missing.Should().BeEmpty($"{operation}: every statement it sends reaches OnExecute with its values");
    }

    /// <summary>
    /// An error raised by <c>ExecuteReader</c> takes the path an error raised by <c>Read()</c> takes: the provider's
    /// <c>OnException</c> handler, which reports it as itself with the statement on <see cref="Exception.Data"/>.
    /// </summary>
    [Fact]
    public async Task An_ExecuteReader_error_carries_the_statement()
    {
        if (_settings == null) return;
        _connector!.CreateTable(new[] { typeof(T875Narrow) });
        var sync = SyncStore<T875Wide>(_settings);
        var async = AsyncStore<T875Wide>(_settings);

        var cases = new (string Path, Func<Task> Act)[]
        {
            ("Read(filter)", Sync(() => sync.Read(x => x.Code == "x").ToList())),
            ("ReadAsync(filter)", async () => (await async.ReadAsync(x => x.Code == "x")).ToList()),
            ("Read(id)", Sync(() => sync.Read(Guid.NewGuid()))),
            ("ReadAsync(id)", () => async.ReadAsync(Guid.NewGuid())),
            ("Count (control)", Sync(() => sync.Count(x => x.Extra == 1))),
            ("CountAsync (control)", () => async.CountAsync(x => x.Extra == 1)),
        };
        var failures = new List<string>();
        foreach (var (path, act) in cases)
        {
            Exception? thrown = null;
            try { await act(); }
            catch (Exception ex) { thrown = ex; }
            var text = thrown?.Data[AbstractConnector.CommandTextDataKey] as string;
            Output.WriteLine($"MEASURE {Provider} | ExecuteReader error | {path} | thrown: {thrown?.GetType().FullName ?? "none"} | statement on Data: {text != null}");
            if (thrown == null) failures.Add($"{path}: no exception");
            else if (thrown.GetType() == typeof(Exception)) failures.Add($"{path}: rewrapped as a bare Exception");
            else if (text == null || !text.Contains("T875Narrow", StringComparison.OrdinalIgnoreCase)) failures.Add($"{path}: no statement on Data");
        }
        failures.Should().BeEmpty();
    }
}

public sealed class SqliteTask875OnExecuteContainmentTests : Task875OnExecuteContainmentTestsBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-t875-" + Guid.NewGuid().ToString("N"));
    public SqliteTask875OnExecuteContainmentTests(ITestOutputHelper output) : base(output) => Directory.CreateDirectory(_root);
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    private SqLiteSettings? _settings;
    protected override PasswordSettings Settings() => _settings ??= new SqLiteSettings(_root, "t875.db");
    protected override IBulkStore<T> SyncStore<T>(PasswordSettings settings) { var s = new SQLiteStore<T>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T> AsyncStore<T>(PasswordSettings settings) { var s = new AsyncSQLiteStore<T>(); s.SetSettings(settings); return s; }
    protected override IIndexManager IndexManager(AbstractConnector connector) => new SqLiteIndexManager(connector);

    /// <summary>
    /// Deliberately NOT announced: the journal-mode PRAGMA is connection setup, run once per connector on a connection of
    /// its own, and announcing it attributed it to whichever operation first opened a connection (it broke 8
    /// "refused before any statement" cases in Task837ViewInListChunkingTests). A contract pin of the recorded gap.
    /// </summary>
    [Fact]
    public async Task The_journal_mode_pragma_is_not_announced()
    {
        var settings = new SqLiteSettings(_root, "t875-pragma.db");
        var store = new AsyncSQLiteStore<T875Fresh>();
        store.SetSettings(settings);
        var connector = ((ISqlConnectedStore)store).Connector!;
        var received = new List<string>();
        OnExecute record = text => { lock (received) received.Add(text); };
        connector.OnExecute += record;
        try { await store.CreateAsync(new T875Fresh { Code = "p" }); }
        finally
        {
            connector.OnExecute -= record;
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(settings.GetConnectionString()));
        }
        received.Should().NotContain(t => t.StartsWith("PRAGMA journal_mode", StringComparison.OrdinalIgnoreCase));
        received.Should().Contain(t => t.Contains("T875Fresh", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_settings != null) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()));
        try { Directory.Delete(_root, true); } catch { }
    }
}

public sealed class PostgreSqlTask875OnExecuteContainmentTests : Task875OnExecuteContainmentTestsBase
{
    public PostgreSqlTask875OnExecuteContainmentTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override bool BulkInsertTextHasValues => false;
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IBulkStore<T> SyncStore<T>(PasswordSettings settings) { var s = new PostgreSQLStore<T>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T> AsyncStore<T>(PasswordSettings settings) { var s = new AsyncPostgreSQLStore<T>(); s.SetSettings(settings); return s; }
    protected override IIndexManager IndexManager(AbstractConnector connector) => new PostgreSqlIndexManager(connector);
}

public sealed class MySqlTask875OnExecuteContainmentTests : Task875OnExecuteContainmentTestsBase
{
    public MySqlTask875OnExecuteContainmentTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IBulkStore<T> SyncStore<T>(PasswordSettings settings) { var s = new MySQLStore<T>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T> AsyncStore<T>(PasswordSettings settings) { var s = new AsyncMySQLStore<T>(); s.SetSettings(settings); return s; }
    protected override IIndexManager IndexManager(AbstractConnector connector) => new MySqlIndexManager(connector);
}

public sealed class MSSqlTask875OnExecuteContainmentTests : Task875OnExecuteContainmentTestsBase
{
    public MSSqlTask875OnExecuteContainmentTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override bool BulkInsertTextHasValues => false;
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IBulkStore<T> SyncStore<T>(PasswordSettings settings) { var s = new MSSqlStore<T>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T> AsyncStore<T>(PasswordSettings settings) { var s = new AsyncMSSqlStore<T>(); s.SetSettings(settings); return s; }
    protected override IIndexManager IndexManager(AbstractConnector connector) => new MSSqlIndexManager(connector);
}
