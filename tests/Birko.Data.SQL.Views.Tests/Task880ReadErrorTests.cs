using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
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
using Birko.Data.Views;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using PortableMode = Birko.Data.Views.ViewQueryMode;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-880 — an error the server raises while it produces a read's rows reaches the caller; it is never answered with
/// an empty result (rule 26).
/// <para>
/// Before, the connector's reader loop returned nothing as soon as <c>HasRows</c> was false, and SQL Server reports an
/// error raised during execution only after the result's metadata, so the error was never read. Measured on SQL Server
/// 2022: a divide by zero or an overflow in a filter, or a SUM that overflows in a view, made every read, paged read,
/// view read and the SELECT before <c>Update(filter, Action)</c> return 0 rows, while the count threw. SQLite, PostgreSQL
/// and MySQL already threw wherever they raise the error (SQLite and MySQL answer a divide by zero with NULL).
/// </para>
/// <para>
/// The contract: a read answers what the count answers. When the count throws, so does every read; when it answers, the
/// read returns that many rows. Each case also writes a <c>MEASURE</c> line, which is the per-provider table.
/// </para>
/// </summary>
[Collection("T880")]
public abstract class Task880ReadErrorTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task880ReadErrorTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract IBulkStore<T880Row> SyncStore(PasswordSettings settings);
    protected abstract IAsyncBulkStore<T880Row> AsyncStore(PasswordSettings settings);

    [Table("T880Rows")]
    public class T880Row : AbstractModel
    {
        public string? Code { get; set; }
        public string? Kind { get; set; }
        public int Hits { get; set; }
        public long Big { get; set; }
        public int Mark { get; set; }
    }

    public class T880View
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
        public long Big { get; set; }
    }

    public class T880SumView
    {
        public string? Kind { get; set; }
        public long Total { get; set; }
    }

    private static ViewDefinition Plain(PortableMode mode) => new ViewDefinitionBuilder<T880View>()
        .HasName("T880View").HasQueryMode(mode)
        .From<T880Row>()
        .Select<T880Row, string?>(o => o.Code, v => v.Code)
        .Select<T880Row, int>(o => o.Hits, v => v.Hits)
        .Select<T880Row, long>(o => o.Big, v => v.Big)
        .Build();

    /// <summary>Kind "c" holds two <see cref="long.MaxValue"/> rows, so its SUM overflows a BIGINT.</summary>
    private static ViewDefinition Sum(PortableMode mode) => new ViewDefinitionBuilder<T880SumView>()
        .HasName("T880SumView").HasQueryMode(mode)
        .From<T880Row>()
        .Select<T880Row, string?>(o => o.Kind, v => v.Kind)
        .GroupBy<T880Row, string?>(o => o.Kind)
        .Sum<T880Row, long>(o => o.Big, v => v.Total)
        .Build();

    private static IEnumerable<T880Row> SeedRows()
    {
        yield return new T880Row { Code = "r1", Kind = "a", Hits = 1, Big = 1 };
        yield return new T880Row { Code = "r2", Kind = "b", Hits = 2, Big = long.MinValue };
        yield return new T880Row { Code = "r3", Kind = "c", Hits = 3, Big = long.MaxValue };
        yield return new T880Row { Code = "r4", Kind = "c", Hits = 4, Big = long.MaxValue };
    }

    /// <summary>
    /// The filters. A divide by zero raises on SQL Server and PostgreSQL; an overflow (<c>ABS(-2^63)</c>) on all four.
    /// "after a row" lets r1 through before r2 raises, where the scan order allows it. A conversion error cannot be
    /// reached through a filter, since TASK-860 translates only exact widening casts. "no error" is the control: no server
    /// raises and every path returns r3.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<T880Row, bool>>> Errors =
        new Dictionary<string, Expression<Func<T880Row, bool>>>
        {
            ["divide by zero"] = x => x.Hits / (x.Hits - x.Hits) == 0,
            ["overflow"] = x => x.Code == "r2" && Math.Abs(x.Big) > 0,
            ["no error"] = x => x.Code == "r3" && x.Hits + 1 > 0,
            ["overflow after a row"] = x => Math.Abs(x.Big) > 0,
        };

    public static readonly IReadOnlyDictionary<string, Expression<Func<T880View, bool>>> ViewErrors =
        new Dictionary<string, Expression<Func<T880View, bool>>>
        {
            ["divide by zero"] = v => v.Hits / (v.Hits - v.Hits) == 0,
            ["overflow"] = v => v.Code == "r2" && Math.Abs(v.Big) > 0,
            ["no error"] = v => v.Code == "r3" && v.Hits + 1 > 0,
            ["overflow after a row"] = v => Math.Abs(v.Big) > 0,
        };

    public static readonly string[] StorePaths =
    {
        "ReadAsync", "Read", "ReadAsync paged", "Read paged", "UpdateAsync(Action)", "Update(Action)",
    };

    public static IEnumerable<object[]> StoreCases
        => from kind in Errors.Keys from path in StorePaths select new object[] { kind, path };

    public static IEnumerable<object[]> ViewCases
        => from kind in ViewErrors.Keys
           from path in new[] { "QueryAsync", "connector.Select" }
           select new object[] { kind, path };

    public static IEnumerable<object[]> SumViewCases
        => from mode in new[] { PortableMode.OnTheFly, PortableMode.Persistent }
           from path in new[] { "QueryAsync", "connector.Select" }
           select new object[] { mode, path };

    private PasswordSettings? _settings;
    private IBulkStore<T880Row>? _sync;
    private IAsyncBulkStore<T880Row>? _async;
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
        _sync = SyncStore(_settings);
        _async = AsyncStore(_settings);
        _connector = ((ISqlConnectedStore)_sync).Connector!;
        await CleanAsync();
        foreach (var row in SeedRows()) await _async.CreateAsync(row);
        await new SqlViewManager(_connector).EnsureAsync(Sum(PortableMode.Persistent));
    }

    public async Task DisposeAsync()
    {
        if (_settings != null) await CleanAsync();
    }

    private async Task CleanAsync()
    {
        var connector = ((ISqlConnectedStore)_sync!).Connector!;
        try { await new SqlViewManager(connector).DropAsync("T880SumView"); } catch { }
        try { connector.DropTable(new[] { typeof(T880Row) }); } catch { }
    }

    /// <summary>What an operation answered: the exception it threw, or how many rows it returned.</summary>
    private sealed record Outcome(Exception? Thrown, int Rows)
    {
        public override string ToString() => Thrown == null
            ? $"{Rows} rows"
            : $"throws {Thrown.GetType().Name}: {Thrown.Message.Split('\n')[0]}";
    }

    private static async Task<Outcome> Measure(Func<Task<int>> action)
    {
        try { return new Outcome(null, await action()); }
        catch (Exception ex) { return new Outcome(ex, 0); }
    }

    private static Task<int> Sync(Func<int> action) => Task.FromResult(action());

    private void Record(string kind, string path, Outcome outcome, Outcome count)
        => Output.WriteLine($"MEASURE | {Provider} | {kind} | {path} | {outcome} | count: {count}");

    private static void ShouldAnswerAsTheCount(string what, Outcome outcome, Outcome count)
    {
        if (count.Thrown != null)
        {
            outcome.Thrown.Should().NotBeNull($"{what}: the count throws ({count.Thrown.Message.Split('\n')[0]}), so the read must not answer {outcome.Rows} rows");
            return;
        }
        outcome.Thrown.Should().BeNull($"{what}: the count answers {count.Rows}");
        outcome.Rows.Should().Be(count.Rows, $"{what}: the count answers {count.Rows}");
    }

    private IReadOnlyCollection<Conditions.Condition> ViewConditions<TView>(ViewDefinition definition, Expression<Func<TView, bool>> filter)
    {
        var view = SqlViewTranslator.Translate(definition);
        using var scope = DataBase.ScopeFieldResolver((type, property, withTableName) =>
        {
            if (type != typeof(TView)) return null;
            return view.GetTableFields().FirstOrDefault(f => f.Property?.Name == property)?.GetSelectName(withTableName);
        });
        return DataBase.ParseConditionExpression(filter).ToList();
    }

    [Theory]
    [MemberData(nameof(StoreCases))]
    public async Task A_store_read_that_the_server_fails_throws(string kind, string path)
    {
        if (_settings == null) return;
        var filter = Errors[kind];
        var count = await Measure(async () => (int)await _async!.CountAsync(filter));
        var syncCount = await Measure(() => Sync(() => (int)_sync!.Count(filter)));
        (syncCount.Thrown == null).Should().Be(count.Thrown == null, "the sync and async counts agree");

        var outcome = await Measure(path switch
        {
            "ReadAsync" => async () => (await _async!.ReadAsync(filter)).Count(),
            "Read" => () => Sync(() => _sync!.Read(filter).Count()),
            "ReadAsync paged" => async () => (await _async!.ReadAsync(filter, null, 10, 0)).Count(),
            "Read paged" => () => Sync(() => _sync!.Read(filter, null, 10, 0).Count()),
            "UpdateAsync(Action)" => async () => { await _async!.UpdateAsync(filter, r => r.Mark = 880); return Marked(); },
            "Update(Action)" => () => Sync(() => { _sync!.Update(filter, r => r.Mark = 880); return Marked(); }),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        });
        Record(kind, path, outcome, count);

        if (path.StartsWith("Update"))
        {
            var marked = Marked();
            ResetMarks();
            if (count.Thrown == null) return;
            outcome.Thrown.Should().NotBeNull($"{path} with '{kind}': the count throws, so the SELECT before the update must too");
            marked.Should().Be(0, "nothing is written when the SELECT fails");
            return;
        }
        ShouldAnswerAsTheCount($"{path} with '{kind}'", outcome, count);
    }

    private int Marked() => _sync!.Read(x => x.Mark == 880).Count();

    private void ResetMarks() => _sync!.Update(x => x.Mark == 880, new PropertyUpdate<T880Row>().Set(x => x.Mark, 0));

    [Theory]
    [MemberData(nameof(ViewCases))]
    public async Task An_on_the_fly_view_read_that_the_server_fails_throws(string kind, string path)
    {
        if (_settings == null) return;
        var filter = ViewErrors[kind];
        var definition = Plain(PortableMode.OnTheFly);
        var store = new SqlViewStore<T880View>(_connector!, definition);
        var count = await Measure(async () => (int)await store.CountAsync(filter));
        var outcome = await Measure(path switch
        {
            "QueryAsync" => async () => (await store.QueryAsync(filter)).Count(),
            "connector.Select" => () => Sync(() => _connector!.Select(SqlViewTranslator.Translate(definition), (_, _) => new object(),
                ViewConditions(definition, filter), null, null, null).Count()),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        });
        Record($"view {kind}", $"OnTheFly {path}", outcome, count);
        ShouldAnswerAsTheCount($"OnTheFly view {path} with '{kind}'", outcome, count);
    }

    [Theory]
    [MemberData(nameof(SumViewCases))]
    public async Task A_grouped_view_whose_sum_the_server_fails_throws(PortableMode mode, string path)
    {
        if (_settings == null) return;
        Expression<Func<T880SumView, bool>> filter = v => v.Kind == "c";
        var definition = Sum(mode);
        var store = new SqlViewStore<T880SumView>(_connector!, definition);
        var count = await Measure(async () => (int)await store.CountAsync(filter));
        var outcome = await Measure(path switch
        {
            "QueryAsync" => async () => (await store.QueryAsync(filter)).Count(),
            "connector.Select" => () => Sync(() => _connector!.Select(SqlViewTranslator.Translate(definition), (_, _) => new object(),
                ViewConditions(definition, filter), null, null, null).Count()),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        });
        Record("SUM overflow", $"{mode} view {path}", outcome, count);
        // The group exists, so 0 rows is the swallowed error. QueryAsync of this grouped view also throws an
        // InvalidCastException on every provider without any overflow (filed separately), so only "not empty" is asserted.
        if (outcome.Thrown == null)
        {
            outcome.Rows.Should().Be(1, $"{mode} view {path}: the group exists, so an answer without an error must hold it");
        }
    }
}

public sealed class SqliteTask880ReadErrorTests : Task880ReadErrorTestsBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-t880-" + Guid.NewGuid().ToString("N"));
    public SqliteTask880ReadErrorTests(ITestOutputHelper output) : base(output) => Directory.CreateDirectory(_root);
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    private SqLiteSettings? _settings;
    protected override PasswordSettings Settings() => _settings ??= new SqLiteSettings(_root, "t880.db");
    protected override IBulkStore<T880Row> SyncStore(PasswordSettings settings) { var s = new SQLiteStore<T880Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T880Row> AsyncStore(PasswordSettings settings) { var s = new AsyncSQLiteStore<T880Row>(); s.SetSettings(settings); return s; }

    public void Dispose()
    {
        if (_settings != null) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()));
        try { Directory.Delete(_root, true); } catch { }
    }
}

public sealed class PostgreSqlTask880ReadErrorTests : Task880ReadErrorTestsBase
{
    public PostgreSqlTask880ReadErrorTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IBulkStore<T880Row> SyncStore(PasswordSettings settings) { var s = new PostgreSQLStore<T880Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T880Row> AsyncStore(PasswordSettings settings) { var s = new AsyncPostgreSQLStore<T880Row>(); s.SetSettings(settings); return s; }
}

public sealed class MySqlTask880ReadErrorTests : Task880ReadErrorTestsBase
{
    public MySqlTask880ReadErrorTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IBulkStore<T880Row> SyncStore(PasswordSettings settings) { var s = new MySQLStore<T880Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T880Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMySQLStore<T880Row>(); s.SetSettings(settings); return s; }
}

public sealed class MSSqlTask880ReadErrorTests : Task880ReadErrorTestsBase
{
    public MSSqlTask880ReadErrorTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IBulkStore<T880Row> SyncStore(PasswordSettings settings) { var s = new MSSqlStore<T880Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T880Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMSSqlStore<T880Row>(); s.SetSettings(settings); return s; }
}
