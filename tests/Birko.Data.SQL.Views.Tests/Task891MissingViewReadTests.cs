using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using PortableMode = Birko.Data.Views.ViewQueryMode;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-891 — a view read whose relation is missing reports it, as the view count does; it is never answered with an
/// empty result (rule 26).
/// <para>
/// Before, measured live: the connector's reader answered a missing table with no rows, a swallow that exists for a store's
/// lazily created table, while a view count runs through the command path and threw "Invalid object name" (SQL Server),
/// 42P01 (PostgreSQL), 1146 (MySQL) or "no such table" (SQLite). So a persistent view that was never created (its
/// <c>EnsureAsync</c> failed), a view dropped after Auto mode had seen it, or an on-the-fly view over a source table that
/// does not exist returned 0 rows from every read, sync and async, while its count threw. A view is never created by a read,
/// so no healing hides behind that empty answer. Each case writes a <c>MEASURE</c> line.
/// </para>
/// </summary>
[Collection("T891V")]
public abstract class Task891MissingViewReadTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task891MissingViewReadTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract AbstractConnector Connector();

    [Table("T891VSource")]
    public class T891VSource : AbstractModel
    {
        public string? Login { get; set; }
        public int Amount { get; set; }
    }

    public class T891VView
    {
        public string? Login { get; set; }
        public int Amount { get; set; }
    }

    private const string ViewName = "T891VView";

    private static ViewDefinition Definition(PortableMode mode)
        => new ViewDefinitionBuilder<T891VView>()
            .HasName(ViewName).HasQueryMode(mode)
            .From<T891VSource>()
            .Select<T891VSource, string>(s => s.Login!, x => x.Login!)
            .Select<T891VSource, int>(s => s.Amount, x => x.Amount)
            .Build();

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
        var connector = Connector();
        _connector = connector;
        await CleanAsync(connector);
        connector.CreateTable(new[] { typeof(T891VSource) });
        connector.Insert(typeof(T891VSource), new T891VSource { Guid = Guid.NewGuid(), Login = "a", Amount = 1 });
        connector.Insert(typeof(T891VSource), new T891VSource { Guid = Guid.NewGuid(), Login = "b", Amount = 2 });
    }

    public async Task DisposeAsync()
    {
        if (_connector == null) return;
        await CleanAsync(_connector);
    }

    private static async Task CleanAsync(AbstractConnector connector)
    {
        try { await new SqlViewManager(connector).DropAsync(ViewName); } catch { }
        try { connector.DropTable(new[] { typeof(T891VSource) }); } catch { }
        connector.ClearViewExistsCache();
    }

    /// <summary>What one call did: <c>n rows</c>, or the exception's type and first line.</summary>
    private static async Task<(string Outcome, Exception? Error)> Outcome(Func<Task<long>> call)
    {
        try { return ($"{await call()} rows", null); }
        catch (Exception ex) { return ($"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}", ex); }
    }

    /// <summary>
    /// Every read and count of <paramref name="mode"/>'s view, sync and async, against a relation that is missing: each one
    /// reports it as a missing table, the way the count does.
    /// </summary>
    private async Task EveryReadReportsWhatTheCountReports(string scenario, PortableMode mode, Func<Task>? beforeEach = null)
    {
        var connector = _connector!;
        var store = new SqlViewStore<T891VView>(connector, Definition(mode));
        var view = SqlViewTranslator.Translate(Definition(mode));
        var calls = new (string Name, Func<Task<long>> Call)[]
        {
            ("CountAsync", () => store.CountAsync()),
            ("SelectCount (sync)", () => Task.FromResult(connector.SelectCount(view))),
            ("QueryAsync", async () => (await store.QueryAsync()).LongCount()),
            ("QueryAsync(filter)", async () => (await store.QueryAsync(v => v.Amount > 0)).LongCount()),
            ("QueryFirstAsync", async () => await store.QueryFirstAsync() == null ? 0 : 1),
            ("Select (sync)", () => Task.FromResult((long)connector.Select(view, SqlViewStore<T891VView>.RowReader(view), (IEnumerable<Birko.Data.SQL.Conditions.Condition>?)null).Count())),
            ("Select (sync, paged)", () => Task.FromResult((long)connector.Select(view, SqlViewStore<T891VView>.RowReader(view), (IEnumerable<Birko.Data.SQL.Conditions.Condition>?)null, null, 1, 0).Count())),
        };

        var failures = new List<string>();
        foreach (var (name, call) in calls)
        {
            if (beforeEach != null) await beforeEach();
            var (outcome, error) = await Outcome(call);
            Output.WriteLine($"MEASURE {Provider} | {scenario} | {name} | {outcome}");
            if (error == null || !connector.IsMissingTableExceptionChain(error))
                failures.Add($"{name}: {outcome}");
        }
        failures.Should().BeEmpty($"every read reports the missing relation, as the count does ({scenario})");
    }

    [Fact]
    public async Task A_persistent_view_that_was_never_created_is_reported_by_every_read()
    {
        if (_connector == null) return;
        await EveryReadReportsWhatTheCountReports("persistent view never created", PortableMode.Persistent);
    }

    /// <summary>
    /// TASK-901 inverted part of this (rule 56): the read that meets the dropped view still reports it, but it also makes
    /// Auto mode forget the view, so the read after it falls back on the fly (pinned in <c>ViewProbeContract</c>). Each call
    /// is therefore measured right after the view was seen and dropped, not one after another.
    /// </summary>
    [Fact]
    public async Task A_view_dropped_after_Auto_mode_saw_it_is_reported_by_the_read_that_meets_it()
    {
        if (_connector == null) return;
        var connector = _connector;
        async Task SeeThenDrop()
        {
            await new SqlViewManager(connector).EnsureAsync(Definition(PortableMode.Auto));
            var store = new SqlViewStore<T891VView>(connector, Definition(PortableMode.Auto));
            (await store.QueryAsync()).Should().HaveCount(2, "the view exists and Auto mode remembers it");

            // Dropped outside this connector, so its existence cache still says the view is there.
            connector.DoCommand(c => c.CommandText = "DROP VIEW " + connector.QuoteIdentifier(ViewName), c => c.ExecuteNonQuery());
        }

        await EveryReadReportsWhatTheCountReports("Auto view dropped after it was seen", PortableMode.Auto, SeeThenDrop);

        var next = new SqlViewStore<T891VView>(connector, Definition(PortableMode.Auto));
        (await next.CountAsync()).Should().Be(2, "the read that reported the view made Auto mode probe again, and it reads on the fly");
    }

    [Fact]
    public async Task An_on_the_fly_view_over_a_missing_source_table_is_reported_by_every_read()
    {
        if (_connector == null) return;
        _connector.DropTable(new[] { typeof(T891VSource) });
        await EveryReadReportsWhatTheCountReports("on-the-fly view, source table missing", PortableMode.OnTheFly);
    }

    [Fact]
    public async Task A_view_that_exists_still_answers_its_rows()
    {
        if (_connector == null) return;
        await new SqlViewManager(_connector).EnsureAsync(Definition(PortableMode.Persistent));
        foreach (var mode in new[] { PortableMode.Persistent, PortableMode.OnTheFly, PortableMode.Auto })
        {
            var store = new SqlViewStore<T891VView>(_connector, Definition(mode));
            (await store.QueryAsync()).Select(v => v.Login).OrderBy(x => x).Should().Equal("a", "b");
            (await store.CountAsync(v => v.Amount > 1)).Should().Be(1);
        }
    }
}

public sealed class SqliteTask891MissingViewReadTests : Task891MissingViewReadTestsBase
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t891v-{Guid.NewGuid():N}.db");
    public SqliteTask891MissingViewReadTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    protected override AbstractConnector Connector() => new SqLiteConnector(new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path)));
}

public sealed class PostgreSqlTask891MissingViewReadTests : Task891MissingViewReadTestsBase
{
    public PostgreSqlTask891MissingViewReadTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override AbstractConnector Connector() => new PostgreSQLConnector(new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        LiveDatabase.Name("PG"),
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 });
}

public sealed class MySqlTask891MissingViewReadTests : Task891MissingViewReadTestsBase
{
    public MySqlTask891MissingViewReadTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override AbstractConnector Connector() => new MySQLConnector(new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        LiveDatabase.Name("MYSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 });
}

public sealed class MSSqlTask891MissingViewReadTests : Task891MissingViewReadTestsBase
{
    public MSSqlTask891MissingViewReadTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override AbstractConnector Connector() => new MSSqlConnector(new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        LiveDatabase.Name("MSSQL"),
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true });
}
