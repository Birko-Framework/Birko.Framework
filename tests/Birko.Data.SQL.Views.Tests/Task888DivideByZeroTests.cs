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
/// TASK-888 — a division by zero in a filter or an update's SET value fails the statement on every SQL provider, as
/// C# throws <see cref="DivideByZeroException"/> for an integer or <c>decimal</c> division (the user's decision,
/// 2026-11-09: every provider throws). Measured before: SQLite and MySQL computed NULL, so a read returned no row for
/// it and a count left it out, silently, while PostgreSQL and SQL Server failed the statement.
/// <para>
/// A <c>double</c> division by zero is Infinity or NaN in C#; no provider can compute those (SQL Server stores neither,
/// MySQL and SQLite have no NaN, PostgreSQL orders NaN above every number), and PostgreSQL and SQL Server fail the
/// statement, so it fails on every provider too: a refusal, never another answer. A NULL divisor stays NULL, as C#'s
/// lifted operators give null.
/// </para>
/// <para>Each case writes a <c>MEASURE</c> line, which is the per-provider table.</para>
/// </summary>
[Collection("T888")]
public abstract class Task888DivideByZeroTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task888DivideByZeroTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract IBulkStore<T888Row> SyncStore(PasswordSettings settings);
    protected abstract IAsyncBulkStore<T888Row> AsyncStore(PasswordSettings settings);

    /// <summary>
    /// <c>IntDiv</c>, <c>BigDiv</c>, <c>DecDiv</c> and <c>DblDiv</c> are zero on r2; <c>Safe</c> is never zero; <c>NDiv</c> is
    /// NULL on r2 and never zero; <c>NZero</c> is NULL on r1 and zero on r2.
    /// </summary>
    [Table("T888Rows")]
    public class T888Row : AbstractModel
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
        public int IntDiv { get; set; }
        public int Safe { get; set; }
        public int? NDiv { get; set; }
        public int? NZero { get; set; }
        public int? NHits { get; set; }
        public long Big { get; set; }
        public long BigDiv { get; set; }
        [PrecisionField(18)]
        [ScaleField(4)]
        public decimal Amount { get; set; }
        [PrecisionField(18)]
        [ScaleField(4)]
        public decimal DecDiv { get; set; }
        [PrecisionField(18)]
        [ScaleField(4)]
        public decimal DecSafe { get; set; }
        public double Dbl { get; set; }
        public double DblDiv { get; set; }
        public int Mark { get; set; }
    }

    public class T888View
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
        public int IntDiv { get; set; }
        public int Safe { get; set; }
        public double Dbl { get; set; }
        public double DblDiv { get; set; }
    }

    private static ViewDefinition View(PortableMode mode) => new ViewDefinitionBuilder<T888View>()
        .HasName("T888View").HasQueryMode(mode)
        .From<T888Row>()
        .Select<T888Row, string?>(o => o.Code, v => v.Code)
        .Select<T888Row, int>(o => o.Hits, v => v.Hits)
        .Select<T888Row, int>(o => o.IntDiv, v => v.IntDiv)
        .Select<T888Row, int>(o => o.Safe, v => v.Safe)
        .Select<T888Row, double>(o => o.Dbl, v => v.Dbl)
        .Select<T888Row, double>(o => o.DblDiv, v => v.DblDiv)
        .Build();

    private static IEnumerable<T888Row> SeedRows()
    {
        yield return new T888Row
        {
            Code = "r1", Hits = 6, IntDiv = 2, Safe = 2, NDiv = 2, NZero = null, NHits = 1, Big = 6, BigDiv = 2,
            Amount = 6m, DecDiv = 4m, DecSafe = 4m, Dbl = 6, DblDiv = 2,
        };
        yield return new T888Row
        {
            Code = "r2", Hits = 4, IntDiv = 0, Safe = 3, NDiv = null, NZero = 0, NHits = 1, Big = 4, BigDiv = 0,
            Amount = 4m, DecDiv = 0m, DecSafe = 3m, Dbl = 4, DblDiv = 0,
        };
        yield return new T888Row
        {
            Code = "r3", Hits = -7, IntDiv = 2, Safe = 2, NDiv = 3, NZero = 5, NHits = 1, Big = -7, BigDiv = 2,
            Amount = -7m, DecDiv = 2m, DecSafe = 2m, Dbl = -7, DblDiv = 2,
        };
    }

    /// <summary>
    /// A filter case. <see cref="FloatingByZero"/> marks a <c>double</c> division by zero: C# answers Infinity or NaN and
    /// does not throw, but the decided answer is a failed statement (see the class remarks).
    /// </summary>
    public sealed record FilterCase(Expression<Func<T888Row, bool>> Filter, bool FloatingByZero = false);

    private static readonly int zero = 0;

    public static readonly IReadOnlyDictionary<string, FilterCase> Filters = new Dictionary<string, FilterCase>
    {
        ["int / zero column"] = new(x => x.Hits / x.IntDiv == 2),
        ["int % zero column"] = new(x => x.Hits % x.IntDiv == 0),
        ["int / (Hits - Hits)"] = new(x => x.Hits / (x.Hits - x.Hits) == 0),
        ["int / constant zero"] = new(x => x.Hits / zero == 0),
        ["int % constant zero"] = new(x => x.Hits % zero == 0),
        ["long / zero column"] = new(x => x.Big / x.BigDiv == 3),
        ["long % zero column"] = new(x => x.Big % x.BigDiv == 0),
        ["int? / zero"] = new(x => x.Hits / x.NZero > -100),
        ["int? % zero"] = new(x => x.Hits % x.NZero == 0),
        ["decimal / zero column"] = new(x => x.Amount / x.DecDiv > 0m),
        ["decimal % zero column"] = new(x => x.Amount % x.DecDiv == 0m),
        ["double / zero column"] = new(x => x.Dbl / x.DblDiv > 1, FloatingByZero: true),
        ["double % zero column"] = new(x => x.Dbl % x.DblDiv == 0, FloatingByZero: true),
        // Controls: no divisor is zero, so each matches what C# matches.
        ["int / safe column"] = new(x => x.Hits / x.Safe == 3),
        ["int % safe column"] = new(x => x.Hits % x.Safe == -1),
        ["int / negative result"] = new(x => x.Hits / x.Safe == -3),
        ["int? / NULL divisor"] = new(x => x.Hits / x.NDiv == 3),
        ["int? % NULL divisor"] = new(x => x.Hits % x.NDiv == 0),
        ["decimal % safe column"] = new(x => x.Amount % x.DecSafe == 1m),
        ["double / safe column"] = new(x => x.Dbl / x.Safe > 1.5),
    };

    public static readonly string[] FilterPaths =
    {
        "ReadAsync", "Read", "CountAsync", "Count", "DeleteAsync", "Delete", "UpdateAsync(PropertyUpdate)",
        "Update(PropertyUpdate)", "UpdateAsync(Action)",
    };

    public static IEnumerable<object[]> FilterCases => from name in Filters.Keys from path in FilterPaths select new object[] { name, path };

    /// <summary>An update's SET value; <see cref="Apply"/> runs it through the connector, <see cref="CSharp"/> computes it.</summary>
    public sealed record SetCase(Action<AbstractConnector> Apply, Action<T888Row> CSharp, bool FloatingByZero = false);

    private static Action<AbstractConnector> Set<P>(Expression<Func<T888Row, P>> column, Expression<Func<T888Row, P>> value)
        => connector => connector.Update(typeof(T888Row),
            new Dictionary<Expression<Func<T888Row, P>>, Expression<Func<T888Row, P>>> { [column] = value },
            (Expression<Func<T888Row, bool>>)(x => x.Code != null));

    public static readonly IReadOnlyDictionary<string, SetCase> Sets = new Dictionary<string, SetCase>
    {
        ["Hits = Hits / IntDiv"] = new(Set<int>(x => x.Hits, x => x.Hits / x.IntDiv), r => r.Hits = r.Hits / r.IntDiv),
        ["Hits = Hits % IntDiv"] = new(Set<int>(x => x.Hits, x => x.Hits % x.IntDiv), r => r.Hits = r.Hits % r.IntDiv),
        ["Hits = Hits / 0"] = new(Set<int>(x => x.Hits, x => x.Hits / zero), r => r.Hits = r.Hits / zero),
        ["Big = Big / BigDiv"] = new(Set<long>(x => x.Big, x => x.Big / x.BigDiv), r => r.Big = r.Big / r.BigDiv),
        ["NHits = Hits / NZero"] = new(Set<int?>(x => x.NHits, x => x.Hits / x.NZero), r => r.NHits = r.Hits / r.NZero),
        ["Amount = Amount % DecDiv"] = new(Set<decimal>(x => x.Amount, x => x.Amount % x.DecDiv), r => r.Amount = r.Amount % r.DecDiv),
        ["Amount = Amount / DecDiv"] = new(Set<decimal>(x => x.Amount, x => x.Amount / x.DecDiv), r => r.Amount = r.Amount / r.DecDiv),
        ["Dbl = Dbl / DblDiv"] = new(Set<double>(x => x.Dbl, x => x.Dbl / x.DblDiv), r => r.Dbl = r.Dbl / r.DblDiv, FloatingByZero: true),
        // Controls.
        ["Hits = Hits / Safe"] = new(Set<int>(x => x.Hits, x => x.Hits / x.Safe), r => r.Hits = r.Hits / r.Safe),
        ["Hits = Hits % Safe"] = new(Set<int>(x => x.Hits, x => x.Hits % x.Safe), r => r.Hits = r.Hits % r.Safe),
        ["NHits = Hits / NDiv"] = new(Set<int?>(x => x.NHits, x => x.Hits / x.NDiv), r => r.NHits = r.Hits / r.NDiv),
        ["Amount = Amount % DecSafe"] = new(Set<decimal>(x => x.Amount, x => x.Amount % x.DecSafe), r => r.Amount = r.Amount % r.DecSafe),
    };

    public static IEnumerable<object[]> SetCases => Sets.Keys.Select(k => new object[] { k });

    public static readonly IReadOnlyDictionary<string, Expression<Func<T888View, bool>>> ViewFilters =
        new Dictionary<string, Expression<Func<T888View, bool>>>
        {
            ["int / zero column"] = v => v.Hits / v.IntDiv == 2,
            ["int % zero column"] = v => v.Hits % v.IntDiv == 0,
            ["double / zero column"] = v => v.Dbl / v.DblDiv > 1,
            ["int / safe column"] = v => v.Hits / v.Safe == 3,
        };

    /// <summary>On-the-fly only: a persistent view refuses a computed filter by name before anything runs (TASK-858).</summary>
    public static IEnumerable<object[]> ViewCases
        => from name in ViewFilters.Keys
           from mode in new[] { PortableMode.OnTheFly }
           from path in new[] { "QueryAsync", "CountAsync" }
           select new object[] { name, mode, path };

    private PasswordSettings? _settings;
    private IBulkStore<T888Row>? _sync;
    private IAsyncBulkStore<T888Row>? _async;
    private AbstractConnector? _connector;
    private List<T888Row> _seed = new();

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
        _seed = SeedRows().ToList();
        foreach (var row in _seed) await _async.CreateAsync(row);
    }

    public async Task DisposeAsync()
    {
        if (_settings != null) await CleanAsync();
    }

    private async Task CleanAsync()
    {
        try { await new SqlViewManager(_connector!).DropAsync("T888View"); } catch { }
        try { _connector!.DropTable(new[] { typeof(T888Row) }); } catch { }
    }

    private sealed record Outcome(Exception? Thrown, IReadOnlyList<string> Codes)
    {
        public override string ToString() => Thrown == null
            ? $"[{string.Join(", ", Codes)}]"
            : $"throws {Thrown.GetType().Name}: {Thrown.Message.Split('\n')[0]}";
    }

    private static Outcome CSharp<TRow>(IEnumerable<TRow> rows, Func<TRow, bool> filter, Func<TRow, string?> code)
    {
        try { return new Outcome(null, rows.Where(filter).Select(r => code(r) ?? "").OrderBy(c => c, StringComparer.Ordinal).ToList()); }
        catch (Exception ex) { return new Outcome(ex, Array.Empty<string>()); }
    }

    private static async Task<Outcome> Measure(Func<Task<IEnumerable<string>>> action)
    {
        try { return new Outcome(null, (await action()).OrderBy(c => c, StringComparer.Ordinal).ToList()); }
        catch (Exception ex) { return new Outcome(ex, Array.Empty<string>()); }
    }

    private static IEnumerable<string> Codes(IEnumerable<T888Row> rows) => rows.Select(r => r.Code ?? "");

    /// <summary>Every column of every row, so a statement that failed can be shown to have written nothing.</summary>
    private string Snapshot() => string.Join("; ", _sync!.Read(x => x.Code != null, null, null, null)
        .OrderBy(r => r.Code, StringComparer.Ordinal)
        .Select(r => $"{r.Code}:{r.Hits}/{r.NHits}/{r.Big}/{r.Amount.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture)}/{r.DecSafe.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}/"
            + $"{r.Dbl.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{r.Mark}"));

    private static string Expected(IEnumerable<T888Row> rows)
        => string.Join("; ", rows.OrderBy(r => r.Code, StringComparer.Ordinal)
            .Select(r => $"{r.Code}:{r.Hits}/{r.NHits}/{r.Big}/{r.Amount.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture)}/{r.DecSafe.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}/"
                + $"{r.Dbl.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{r.Mark}"));

    private void Record(string kind, string path, Outcome outcome, Outcome csharp)
        => Output.WriteLine($"MEASURE | {Provider} | {kind} | {path} | {outcome} | C#: {csharp}");

    [Theory]
    [MemberData(nameof(FilterCases))]
    public async Task A_filter_that_divides_by_zero_fails_as_CSharp_throws(string name, string path)
    {
        if (_settings == null) return;
        var c = Filters[name];
        var compiled = c.Filter.Compile();
        var csharp = CSharp(_seed, compiled, r => r.Code);
        if (c.FloatingByZero) csharp.Thrown.Should().BeNull("C# divides a double by zero into Infinity or NaN");
        var mustThrow = csharp.Thrown != null || c.FloatingByZero;
        var before = Snapshot();
        var filter = c.Filter;

        var outcome = await Measure(path switch
        {
            "ReadAsync" => async () => Codes(await _async!.ReadAsync(filter, null, null, null)),
            "Read" => () => Task.FromResult(Codes(_sync!.Read(filter, null, null, null))),
            "CountAsync" => async () => Enumerable.Repeat("row", (int)await _async!.CountAsync(filter)),
            "Count" => () => Task.FromResult(Enumerable.Repeat("row", (int)_sync!.Count(filter))),
            "DeleteAsync" => async () => { await _async!.DeleteAsync(filter); return Gone(); },
            "Delete" => () => { _sync!.Delete(filter); return Task.FromResult(Gone()); },
            "UpdateAsync(PropertyUpdate)" => async () => { await _async!.UpdateAsync(filter, new PropertyUpdate<T888Row>().Set(x => x.Mark, 888)); return Marked(); },
            "Update(PropertyUpdate)" => () => { _sync!.Update(filter, new PropertyUpdate<T888Row>().Set(x => x.Mark, 888)); return Task.FromResult(Marked()); },
            "UpdateAsync(Action)" => async () => { await _async!.UpdateAsync(filter, r => r.Mark = 888); return Marked(); },
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        });
        var expected = path.StartsWith("Count")
            ? csharp with { Codes = Enumerable.Repeat("row", csharp.Codes.Count).ToList() }
            : csharp;
        Record(name, path, outcome, expected);

        if (mustThrow)
        {
            outcome.Thrown.Should().NotBeNull($"{path} with '{name}': C# {(c.FloatingByZero ? "computes Infinity or NaN, which no provider can" : "throws")}, so the statement must fail");
            Snapshot().Should().Be(before, "a statement that failed writes nothing");
            return;
        }
        outcome.Thrown.Should().BeNull($"{path} with '{name}': no divisor is zero");
        outcome.Codes.Should().Equal(expected.Codes, $"{path} with '{name}' matches what C# matches");
    }

    private IEnumerable<string> Gone()
    {
        var left = Codes(_sync!.Read(x => x.Code != null, null, null, null)).ToHashSet();
        return _seed.Select(r => r.Code ?? "").Where(c => !left.Contains(c)).ToList();
    }

    private IEnumerable<string> Marked() => Codes(_sync!.Read(x => x.Mark == 888, null, null, null)).ToList();

    [Theory]
    [MemberData(nameof(SetCases))]
    public async Task A_SET_value_that_divides_by_zero_fails_and_writes_nothing(string name)
    {
        if (_settings == null) return;
        var c = Sets[name];
        var expectedRows = SeedRows().ToList();
        Exception? csharpThrown = null;
        try { foreach (var row in expectedRows) c.CSharp(row); }
        catch (Exception ex) { csharpThrown = ex; }
        var mustThrow = csharpThrown != null || c.FloatingByZero;
        var before = Snapshot();

        Exception? thrown = null;
        try { c.Apply(_connector!); }
        catch (Exception ex) { thrown = ex; }
        await Task.CompletedTask;
        var after = Snapshot();
        Output.WriteLine($"MEASURE | {Provider} | SET {name} | {(thrown == null ? "stored " + after : $"throws {thrown.GetType().Name}: {thrown.Message.Split('\n')[0]}")} | C#: {(csharpThrown == null ? Expected(expectedRows) : "throws " + csharpThrown.GetType().Name)}");

        if (mustThrow)
        {
            thrown.Should().NotBeNull($"SET {name}: C# {(c.FloatingByZero ? "computes Infinity or NaN, which no provider can" : "throws")}, so the statement must fail");
            after.Should().Be(before, "a statement that failed writes nothing");
            return;
        }
        thrown.Should().BeNull($"SET {name}: no divisor is zero");
        after.Should().Be(Expected(expectedRows), $"SET {name} stores what C# computes");
    }

    [Fact]
    public async Task A_computed_decimal_SET_that_divides_by_zero_fails_and_writes_nothing()
    {
        if (_settings == null) return;
        var before = Snapshot();
        Exception? thrown = null;
        try
        {
            await _async!.UpdateAsync(x => x.Code != null, new PropertyUpdate<T888Row>().Compute(x => x.DecSafe, x => x.Amount / x.DecDiv));
        }
        catch (Exception ex) { thrown = ex; }
        Output.WriteLine($"MEASURE | {Provider} | Compute DecSafe = Amount / DecDiv | {(thrown == null ? "stored " + Snapshot() : $"throws {thrown.GetType().Name}: {thrown.Message.Split('\n')[0]}")} | C#: throws DivideByZeroException");
        thrown.Should().NotBeNull("C# throws for a decimal division by zero");
        Snapshot().Should().Be(before, "a statement that failed writes nothing");
    }

    [Theory]
    [MemberData(nameof(ViewCases))]
    public async Task A_view_filter_that_divides_by_zero_fails(string name, PortableMode mode, string path)
    {
        if (_settings == null) return;
        var definition = View(mode);
        if (mode == PortableMode.Persistent) await new SqlViewManager(_connector!).EnsureAsync(definition);
        var store = new SqlViewStore<T888View>(_connector!, definition);
        var filter = ViewFilters[name];
        var rows = _seed.Select(r => new T888View { Code = r.Code, Hits = r.Hits, IntDiv = r.IntDiv, Safe = r.Safe, Dbl = r.Dbl, DblDiv = r.DblDiv }).ToList();
        var csharp = CSharp(rows, filter.Compile(), v => v.Code);
        var floating = name.StartsWith("double") && name.Contains("zero");
        var mustThrow = csharp.Thrown != null || floating;

        var outcome = await Measure(path switch
        {
            "QueryAsync" => async () => (await store.QueryAsync(filter)).Select(v => v.Code ?? ""),
            "CountAsync" => async () => Enumerable.Repeat("row", (int)await store.CountAsync(filter)),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        });
        var expected = path == "CountAsync" ? csharp with { Codes = Enumerable.Repeat("row", csharp.Codes.Count).ToList() } : csharp;
        Record($"view {name}", $"{mode} {path}", outcome, expected);

        if (mustThrow)
        {
            outcome.Thrown.Should().NotBeNull($"{mode} view {path} with '{name}' must fail");
            return;
        }
        outcome.Thrown.Should().BeNull();
        outcome.Codes.Should().Equal(expected.Codes);
    }
}

public sealed class SqliteTask888DivideByZeroTests : Task888DivideByZeroTestsBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-t888-" + Guid.NewGuid().ToString("N"));
    public SqliteTask888DivideByZeroTests(ITestOutputHelper output) : base(output) => Directory.CreateDirectory(_root);
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    private SqLiteSettings? _settings;
    protected override PasswordSettings Settings() => _settings ??= new SqLiteSettings(_root, "t888.db");
    protected override IBulkStore<T888Row> SyncStore(PasswordSettings settings) { var s = new SQLiteStore<T888Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T888Row> AsyncStore(PasswordSettings settings) { var s = new AsyncSQLiteStore<T888Row>(); s.SetSettings(settings); return s; }

    public void Dispose()
    {
        if (_settings != null) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()));
        try { Directory.Delete(_root, true); } catch { }
    }
}

public sealed class PostgreSqlTask888DivideByZeroTests : Task888DivideByZeroTestsBase
{
    public PostgreSqlTask888DivideByZeroTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IBulkStore<T888Row> SyncStore(PasswordSettings settings) { var s = new PostgreSQLStore<T888Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T888Row> AsyncStore(PasswordSettings settings) { var s = new AsyncPostgreSQLStore<T888Row>(); s.SetSettings(settings); return s; }
}

public sealed class MySqlTask888DivideByZeroTests : Task888DivideByZeroTestsBase
{
    public MySqlTask888DivideByZeroTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IBulkStore<T888Row> SyncStore(PasswordSettings settings) { var s = new MySQLStore<T888Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T888Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMySQLStore<T888Row>(); s.SetSettings(settings); return s; }
}

public sealed class MSSqlTask888DivideByZeroTests : Task888DivideByZeroTestsBase
{
    public MSSqlTask888DivideByZeroTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IBulkStore<T888Row> SyncStore(PasswordSettings settings) { var s = new MSSqlStore<T888Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T888Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMSSqlStore<T888Row>(); s.SetSettings(settings); return s; }
}
