using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Models;
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

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-894 — every <c>OnException</c> subscriber runs, and none of them changes what the caller receives (rule 25).
/// <para>
/// Measured before, on SQLite, PostgreSQL 16, MySQL 8.4 and SQL Server 2022 (the <c>MEASURE</c> lines): a consumer's
/// <c>OnException</c> subscriber was never called, for any failure. Each provider subscribed its own handler first, in
/// its constructor, and that handler always throws (<c>EnsureSchemaAndReport</c>), so the multicast stopped before the
/// consumer's delegate.
/// </para>
/// <para>
/// The provider's handler is now the <c>ReportCommandException</c> override, run first; the subscribers then receive the
/// exception it reports, each in its own <c>try</c>, and that same exception object is rethrown. So what the caller
/// selects on (rule 9) is unchanged: the missing-table count filter, the retry filter and a type a host catches.
/// </para>
/// </summary>
[Collection("T894")]
public abstract class Task894OnExceptionSubscriberTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task894OnExceptionSubscriberTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract IBulkStore<T> SyncStore<T>(PasswordSettings settings) where T : AbstractModel;
    protected abstract IAsyncBulkStore<T> AsyncStore<T>(PasswordSettings settings) where T : AbstractModel;

    [Table("T894Narrow")]
    public class T894Narrow : AbstractModel
    {
        public string? Code { get; set; }
    }

    /// <summary>Maps <c>T894Narrow</c> with a column the table does not have, so every statement on it fails.</summary>
    [Table("T894Narrow")]
    public class T894Wide : AbstractModel
    {
        public string? Code { get; set; }
        public int Extra { get; set; }
    }

    [Table("T894Gone")]
    public class T894Gone : AbstractModel
    {
        public string? Code { get; set; }
    }

    [Table("T894Never")]
    public class T894Never : AbstractModel
    {
        public string? Code { get; set; }
    }

    public sealed class SubscriberBoom : Exception
    {
        public SubscriberBoom() : base("T894: the OnException subscriber threw") { }
    }

    public static readonly string[] Failures =
    {
        "Read(filter)", "ReadAsync(filter)", "Create", "CreateAsync", "Update(filter, set)", "UpdateAsync(filter, set)",
        "Create(many)", "CreateAsync(many)", "Create on a dropped table", "CreateAsync on a dropped table",
    };

    public static IEnumerable<object[]> FailureCases => Failures.Select(f => new object[] { f });

    private PasswordSettings? _settings;
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

    public Task InitializeAsync()
    {
        if (!Live()) return Task.CompletedTask;
        _settings = Settings();
        _connector = ((ISqlConnectedStore)AsyncStore<T894Narrow>(_settings)).Connector!;
        Clean();
        _connector.CreateTable(new[] { typeof(T894Narrow) });
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (_settings != null) Clean();
        return Task.CompletedTask;
    }

    private void Clean()
    {
        try { _connector!.DropTable(new[] { typeof(T894Narrow), typeof(T894Gone), typeof(T894Never) }); } catch { }
    }

    private static Func<Task> Sync(Action action) => () => { action(); return Task.CompletedTask; };

    private Func<Task> Arrange(string name)
    {
        var sync = SyncStore<T894Wide>(_settings!);
        var async = AsyncStore<T894Wide>(_settings!);
        List<T894Wide> Many() => Enumerable.Range(0, 3).Select(i => new T894Wide { Code = "m" + i }).ToList();
        switch (name)
        {
            case "Read(filter)": return Sync(() => sync.Read(x => x.Code == "x").ToList());
            case "ReadAsync(filter)": return async () => (await async.ReadAsync(x => x.Code == "x")).ToList();
            case "Create": return Sync(() => sync.Create(new T894Wide { Code = "c" }));
            case "CreateAsync": return () => async.CreateAsync(new T894Wide { Code = "c" });
            case "Update(filter, set)": return Sync(() => sync.Update(x => x.Code == "x", new PropertyUpdate<T894Wide>().Set(x => x.Extra, 1)));
            case "UpdateAsync(filter, set)": return () => async.UpdateAsync(x => x.Code == "x", new PropertyUpdate<T894Wide>().Set(x => x.Extra, 1));
            case "Create(many)": return Sync(() => sync.Create(Many()));
            case "CreateAsync(many)": return () => async.CreateAsync(Many());
            case "Create on a dropped table":
            case "CreateAsync on a dropped table":
            {
                // An initialised store whose table is dropped beneath it: the provider's handler ensures the schema
                // and reports the failure annotated (TASK-277/286), a rewrapped Exception.
                var gone = AsyncStore<T894Gone>(_settings!);
                gone.CreateAsync(new T894Gone { Code = "seed" }).GetAwaiter().GetResult();
                _connector!.DropTable(new[] { typeof(T894Gone) });
                if (name == "CreateAsync on a dropped table") return () => gone.CreateAsync(new T894Gone { Code = "c" });
                var goneSync = SyncStore<T894Gone>(_settings!);
                goneSync.Count();
                _connector.DropTable(new[] { typeof(T894Gone) });
                return Sync(() => goneSync.Create(new T894Gone { Code = "c" }));
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }

    /// <summary>
    /// Rule 25 and rule 9: with a throwing subscriber registered FIRST, a later subscriber still receives the failure, it
    /// receives the very object the caller catches, and the caller does not get the subscriber's exception.
    /// </summary>
    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task Every_subscriber_receives_what_the_caller_receives(string failure)
    {
        if (_settings == null) return;
        var act = Arrange(failure);
        var received = new List<(Exception Error, string? Text)>();
        OnException boom = (_, _) => throw new SubscriberBoom();
        OnException record = (ex, text) => { lock (received) received.Add((ex, text)); };
        _connector!.OnException += boom;
        _connector.OnException += record;
        Exception? thrown = null;
        try { await act(); }
        catch (Exception ex) { thrown = ex; }
        finally
        {
            _connector.OnException -= boom;
            _connector.OnException -= record;
        }
        Output.WriteLine($"MEASURE {Provider} | {failure} | thrown: {thrown?.GetType().Name ?? "none"} | subscriber received {received.Count} | same object: {received.Any(r => ReferenceEquals(r.Error, thrown))}");

        thrown.Should().NotBeNull($"{failure} fails");
        thrown.Should().NotBeOfType<SubscriberBoom>("a subscriber's exception never reaches the caller");
        // The connector is cached process-wide, so a parallel class's failure can reach this subscriber too.
        var mine = received.Where(r => r.Text != null && r.Text.Contains("T894", StringComparison.OrdinalIgnoreCase)).ToList();
        mine.Should().ContainSingle($"{failure}: the subscriber after a throwing one still runs, once");
        mine[0].Error.Should().BeSameAs(thrown, "a subscriber sees the exception the caller receives");
        _connector.SubscriberFailures.Should().Contain(f => f.Channel == nameof(AbstractConnector.OnException) && f.Error is SubscriberBoom);
    }

    /// <summary>
    /// TASK-285's filter (<c>IsMissingTableExceptionChain</c>) still matches: the count of a table that does not exist is
    /// 0, with a throwing subscriber registered, and the subscribers are told about the failure the count answered.
    /// </summary>
    [Fact]
    public async Task The_count_of_a_missing_table_is_still_0()
    {
        if (_settings == null) return;
        var received = new List<Exception>();
        OnException boom = (_, _) => throw new SubscriberBoom();
        OnException record = (ex, text) =>
        {
            if (text == null || !text.Contains("T894Never", StringComparison.OrdinalIgnoreCase)) return; // a parallel class's failure
            lock (received) received.Add(ex);
        };
        _connector!.OnException += boom;
        _connector.OnException += record;
        long sync, async;
        try
        {
            sync = _connector.SelectCount(typeof(T894Never));
            async = await ((AbstractAsyncConnector)_connector).SelectCountAsync(typeof(T894Never));
        }
        finally
        {
            _connector.OnException -= boom;
            _connector.OnException -= record;
        }
        Output.WriteLine($"MEASURE {Provider} | count of a missing table | sync {sync} async {async} | subscriber received {received.Count}");
        sync.Should().Be(0);
        async.Should().Be(0);
        received.Should().HaveCount(2).And.OnlyContain(ex => _connector.IsMissingTableExceptionChain(ex));
    }
}

public sealed class SqliteTask894OnExceptionSubscriberTests : Task894OnExceptionSubscriberTestsBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-t894-" + Guid.NewGuid().ToString("N"));
    public SqliteTask894OnExceptionSubscriberTests(ITestOutputHelper output) : base(output) => Directory.CreateDirectory(_root);
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    private SqLiteSettings? _settings;
    protected override PasswordSettings Settings() => _settings ??= new SqLiteSettings(_root, "t894.db");
    protected override IBulkStore<T> SyncStore<T>(PasswordSettings settings) { var s = new SQLiteStore<T>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T> AsyncStore<T>(PasswordSettings settings) { var s = new AsyncSQLiteStore<T>(); s.SetSettings(settings); return s; }

    /// <summary>
    /// A connector that classes "no such column" as a transient error the server did not apply, so a statement on a
    /// missing column stands for a deadlock victim or SQLITE_BUSY (TASK-305) — which on SQLite surfaces at <c>BEGIN</c>,
    /// outside <c>InitException</c>, and so cannot be provoked through it (measured).
    /// </summary>
    private sealed class FlakyConnector : SqLiteConnector
    {
        public FlakyConnector(SqLiteSettings settings) : base(settings) { }
        private static bool Flaky(Exception ex) => ex is Microsoft.Data.Sqlite.SqliteException e && e.Message.Contains("no such column", StringComparison.OrdinalIgnoreCase);
        public override bool IsTransientException(Exception ex) => Flaky(ex) || base.IsTransientException(ex);
        public override bool IsNotAppliedException(Exception ex) => Flaky(ex) || base.IsNotAppliedException(ex);
    }

    /// <summary>
    /// The retry filter (<c>IsTransientException</c> / <c>IsNotAppliedException</c>) still matches the object the
    /// subscribers saw, and they run before the retry. The subscriber adds the missing column and then throws; the retry
    /// succeeds. Before, it never ran, so every attempt failed the same way.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_transient_failure_is_retried_after_the_subscribers_ran(bool isAsync)
    {
        var settings = new SqLiteSettings(_root, "t894-retry-" + isAsync + ".db");
        var connector = new FlakyConnector(settings);
        connector.CreateTable(new[] { typeof(T894Narrow) });
        var seen = 0;
        OnException repair = (ex, _) =>
        {
            seen++;
            using var raw = new Microsoft.Data.Sqlite.SqliteConnection(settings.GetConnectionString());
            raw.Open();
            using var alter = raw.CreateCommand();
            alter.CommandText = "ALTER TABLE \"T894Narrow\" ADD COLUMN Extra INTEGER";
            alter.ExecuteNonQuery();
            throw new SubscriberBoom();
        };
        connector.OnException += repair;
        System.Linq.Expressions.Expression<Func<T894Wide, bool>> filter = x => x.Extra == 1;
        Exception? thrown = null;
        long count = -1;
        try
        {
            count = isAsync ? await connector.SelectCountAsync(typeof(T894Wide), filter) : connector.SelectCount(typeof(T894Wide), filter);
        }
        catch (Exception ex) { thrown = ex; }
        finally
        {
            connector.OnException -= repair;
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(settings.GetConnectionString()));
        }
        Output.WriteLine($"MEASURE SQLite | transient failure ({(isAsync ? "async" : "sync")}) | thrown: {thrown?.GetType().Name ?? "none"} | subscriber ran {seen} | count {count}");

        thrown.Should().BeNull("the subscriber repaired the cause and the retry filter still matched the reported exception");
        seen.Should().Be(1);
        count.Should().Be(0);
        connector.SubscriberFailures.Should().Contain(f => f.Channel == nameof(AbstractConnector.OnException) && f.Error is SubscriberBoom);
    }

    public void Dispose()
    {
        if (_settings != null) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()));
        try { Directory.Delete(_root, true); } catch { }
    }
}

public sealed class PostgreSqlTask894OnExceptionSubscriberTests : Task894OnExceptionSubscriberTestsBase
{
    public PostgreSqlTask894OnExceptionSubscriberTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        LiveDatabase.Name("PG"),
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IBulkStore<T> SyncStore<T>(PasswordSettings settings) { var s = new PostgreSQLStore<T>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T> AsyncStore<T>(PasswordSettings settings) { var s = new AsyncPostgreSQLStore<T>(); s.SetSettings(settings); return s; }
}

public sealed class MySqlTask894OnExceptionSubscriberTests : Task894OnExceptionSubscriberTestsBase
{
    public MySqlTask894OnExceptionSubscriberTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        LiveDatabase.Name("MYSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IBulkStore<T> SyncStore<T>(PasswordSettings settings) { var s = new MySQLStore<T>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T> AsyncStore<T>(PasswordSettings settings) { var s = new AsyncMySQLStore<T>(); s.SetSettings(settings); return s; }
}

public sealed class MSSqlTask894OnExceptionSubscriberTests : Task894OnExceptionSubscriberTestsBase
{
    public MSSqlTask894OnExceptionSubscriberTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        LiveDatabase.Name("MSSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IBulkStore<T> SyncStore<T>(PasswordSettings settings) { var s = new MSSqlStore<T>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T> AsyncStore<T>(PasswordSettings settings) { var s = new AsyncMSSqlStore<T>(); s.SetSettings(settings); return s; }
}
