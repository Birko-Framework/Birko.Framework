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
/// TASK-890 — integer arithmetic in a filter or an update's SET value that overflows its C# type fails the statement on every
/// SQL provider, and arithmetic that does not overflow answers what C# answers.
/// <para>
/// The oracle is C# <c>checked</c> arithmetic: the expression is compiled with every integer <c>+</c>, <c>-</c>, <c>*</c>, unary
/// minus and narrowing conversion checked. Where it throws <see cref="OverflowException"/> on any row, the statement must fail
/// and write nothing; elsewhere it must match or store what C# computes. C#'s default (unchecked) arithmetic wraps, which no
/// provider computes: PostgreSQL and SQL Server fail the statement, and TASK-870 made SQLite fail it too. MySQL computed every
/// integer expression in BIGINT and answered (measured: <c>x.Hits * 2 / 2 == x.Hits</c> matched the <see cref="int.MaxValue"/>
/// row, where C# wraps to -1 and does not).
/// </para>
/// <para>Each case writes a <c>MEASURE</c> line, which is the per-provider table.</para>
/// </summary>
[Collection("T890")]
public abstract class Task890ArithmeticOverflowTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task890ArithmeticOverflowTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract IBulkStore<T890Row> SyncStore(PasswordSettings settings);
    protected abstract IAsyncBulkStore<T890Row> AsyncStore(PasswordSettings settings);

    /// <summary>r1 holds each type's maximum, r3 its minimum, r2 small values; <c>Two</c> is 2 on every row.</summary>
    [Table("T890Rows")]
    public class T890Row : AbstractModel
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
        public long Big { get; set; }
        public short Sh { get; set; }
        public int Two { get; set; }
        public int Mark { get; set; }
    }

    public class T890View
    {
        public string? Code { get; set; }
        public int Hits { get; set; }
        public int Two { get; set; }
    }

    private static ViewDefinition View(PortableMode mode) => new ViewDefinitionBuilder<T890View>()
        .HasName("T890View").HasQueryMode(mode)
        .From<T890Row>()
        .Select<T890Row, string?>(o => o.Code, v => v.Code)
        .Select<T890Row, int>(o => o.Hits, v => v.Hits)
        .Select<T890Row, int>(o => o.Two, v => v.Two)
        .Build();

    private static IEnumerable<T890Row> SeedRows()
    {
        yield return new T890Row { Code = "r1", Hits = int.MaxValue, Big = long.MaxValue, Sh = short.MaxValue, Two = 2 };
        yield return new T890Row { Code = "r2", Hits = 5, Big = 5, Sh = 5, Two = 2 };
        yield return new T890Row { Code = "r3", Hits = int.MinValue, Big = long.MinValue, Sh = short.MinValue, Two = 2 };
    }

    /// <summary>Rewrites an expression into C#'s <c>checked</c> arithmetic: the oracle.</summary>
    private sealed class Checked : ExpressionVisitor
    {
        public static Expression<Func<TRow, TResult>> Of<TRow, TResult>(Expression<Func<TRow, TResult>> e)
            => (Expression<Func<TRow, TResult>>)new Checked().Visit(e)!;

        protected override Expression VisitBinary(BinaryExpression node)
        {
            var left = Visit(node.Left)!;
            var right = Visit(node.Right)!;
            return node.NodeType switch
            {
                ExpressionType.Add => Expression.AddChecked(left, right),
                ExpressionType.Subtract => Expression.SubtractChecked(left, right),
                ExpressionType.Multiply => Expression.MultiplyChecked(left, right),
                _ => node.Update(left, node.Conversion, right),
            };
        }

        protected override Expression VisitUnary(UnaryExpression node)
        {
            var operand = Visit(node.Operand)!;
            return node.NodeType switch
            {
                ExpressionType.Negate => Expression.NegateChecked(operand),
                ExpressionType.Convert => Expression.ConvertChecked(operand, node.Type),
                _ => node.Update(operand),
            };
        }
    }

    private static readonly Expression<Func<T890Row, bool>> EveryRow = x => x.Code != null;
    private static readonly Expression<Func<T890Row, bool>> SmallRow = x => x.Code == "r2";

    public static readonly IReadOnlyDictionary<string, Expression<Func<T890Row, bool>>> Filters =
        new Dictionary<string, Expression<Func<T890Row, bool>>>
        {
            // Overflow on r1 or r3: C# checked throws, so the statement must fail.
            ["Hits * 2 / 2 == Hits"] = x => x.Hits * 2 / 2 == x.Hits,
            ["Hits + 1 > 0"] = x => x.Hits + 1 > 0,
            ["Hits - 1 < 0"] = x => x.Hits - 1 < 0,
            ["-Hits > 0"] = x => -x.Hits > 0,
            ["Hits * Two > 0"] = x => x.Hits * x.Two > 0,
            ["Hits / -1 > 0"] = x => x.Hits / -1 > 0,
            ["Hits + Two - Two == Hits"] = x => x.Hits + x.Two - x.Two == x.Hits,
            ["Big * 2 / 2 == Big"] = x => x.Big * 2 / 2 == x.Big,
            ["Big + Hits > 0"] = x => x.Big + x.Hits > 0,
            ["Math.Abs(Hits) > 0"] = x => Math.Abs(x.Hits) > 0,
            ["Math.Abs(Sh) > 0"] = x => Math.Abs(x.Sh) > 0,
            // No overflow in C#: each must match what C# matches.
            ["Two * 3 - 1 == 5"] = x => x.Two * 3 - 1 == 5,
            ["Hits / Two > 1"] = x => x.Hits / x.Two > 1,
            ["(long)Hits * 2 > 0"] = x => (long)x.Hits * 2 > 0,
            ["Sh * Sh > 1000"] = x => x.Sh * x.Sh > 1000,
            ["Sh + Sh > 0"] = x => x.Sh + x.Sh > 0,
            ["-Sh > 0"] = x => -x.Sh > 0,
        };

    public static readonly string[] FilterPaths =
    {
        "ReadAsync", "Read", "CountAsync", "Count", "DeleteAsync", "UpdateAsync(PropertyUpdate)", "UpdateAsync(Action)",
    };

    public static IEnumerable<object[]> FilterCases => from name in Filters.Keys from path in FilterPaths select new object[] { name, path };

    /// <summary>An update's SET value over the rows <see cref="Filter"/> selects; <see cref="CSharp"/> computes it, checked.</summary>
    public sealed record SetCase(Action<AbstractConnector> Apply, Func<T890Row, Action> CSharp, Expression<Func<T890Row, bool>> Filter);

    private static SetCase Set<P>(Expression<Func<T890Row, P>> column, Expression<Func<T890Row, P>> value, Expression<Func<T890Row, bool>> filter)
    {
        var compute = Checked.Of(value).Compile();
        var target = (System.Reflection.PropertyInfo)((MemberExpression)column.Body).Member;
        return new SetCase(
            connector => connector.Update(typeof(T890Row),
                new Dictionary<Expression<Func<T890Row, P>>, Expression<Func<T890Row, P>>> { [column] = value }, filter),
            row => () => target.SetValue(row, compute(row)),
            filter);
    }

    public static readonly IReadOnlyDictionary<string, SetCase> Sets = new Dictionary<string, SetCase>
    {
        ["Hits = Hits * 2 / 2, every row"] = Set<int>(x => x.Hits, x => x.Hits * 2 / 2, EveryRow),
        ["Hits = Hits * 2 / 2, r2"] = Set<int>(x => x.Hits, x => x.Hits * 2 / 2, SmallRow),
        ["Hits = Hits + Two - Two, every row"] = Set<int>(x => x.Hits, x => x.Hits + x.Two - x.Two, EveryRow),
        ["Big = Big * 2 / 2, every row"] = Set<long>(x => x.Big, x => x.Big * 2 / 2, EveryRow),
        ["Sh = (short)(Sh + 1), every row"] = Set<short>(x => x.Sh, x => (short)(x.Sh + 1), EveryRow),
        ["Sh = (short)(Sh - 1), every row"] = Set<short>(x => x.Sh, x => (short)(x.Sh - 1), EveryRow),
        ["Sh = (short)(Sh + 1), r2"] = Set<short>(x => x.Sh, x => (short)(x.Sh + 1), SmallRow),
        ["Sh = (short)(Sh * Two), r2"] = Set<short>(x => x.Sh, x => (short)(x.Sh * x.Two), SmallRow),
        ["Sh = (short)-Sh, r2"] = Set<short>(x => x.Sh, x => (short)-x.Sh, SmallRow),
        ["Sh = (short)(Sh * Sh / Sh), every row"] = Set<short>(x => x.Sh, x => (short)(x.Sh * x.Sh / x.Sh), EveryRow),
        ["Hits = (int)((long)Hits * 2 / 2), every row"] = Set<int>(x => x.Hits, x => (int)((long)x.Hits * 2 / 2), EveryRow),
        ["Hits = (int)(Big / 2), r2"] = Set<int>(x => x.Hits, x => (int)(x.Big / 2), SmallRow),
        ["Hits = (int)Big, every row"] = Set<int>(x => x.Hits, x => (int)x.Big, EveryRow),
    };

    public static IEnumerable<object[]> SetCases => Sets.Keys.Select(k => new object[] { k });

    public static readonly IReadOnlyDictionary<string, Expression<Func<T890View, bool>>> ViewFilters =
        new Dictionary<string, Expression<Func<T890View, bool>>>
        {
            ["Hits * 2 / 2 == Hits"] = v => v.Hits * 2 / 2 == v.Hits,
            ["Hits + Two > 0"] = v => v.Hits + v.Two > 0,
            ["Two * 3 - 1 == 5"] = v => v.Two * 3 - 1 == 5,
        };

    public static IEnumerable<object[]> ViewCases
        => from name in ViewFilters.Keys from path in new[] { "QueryAsync", "CountAsync" } select new object[] { name, path };

    private PasswordSettings? _settings;
    private IBulkStore<T890Row>? _sync;
    private IAsyncBulkStore<T890Row>? _async;
    private AbstractConnector? _connector;
    private List<T890Row> _seed = new();

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
        try { await new SqlViewManager(_connector!).DropAsync("T890View"); } catch { }
        try { _connector!.DropTable(new[] { typeof(T890Row) }); } catch { }
    }

    private sealed record Outcome(Exception? Thrown, IReadOnlyList<string> Codes)
    {
        public override string ToString() => Thrown == null
            ? $"[{string.Join(", ", Codes)}]"
            : $"throws {Thrown.GetType().Name}: {Thrown.Message.Split('\n')[0]}";
    }

    private static Outcome CSharp<TRow>(IEnumerable<TRow> rows, Func<TRow, bool> filter, Func<TRow, string?> code)
    {
        try { return new Outcome(null, rows.Where(filter).ToList().Select(r => code(r) ?? "").OrderBy(c => c, StringComparer.Ordinal).ToList()); }
        catch (OverflowException ex) { return new Outcome(ex, Array.Empty<string>()); }
    }

    private static async Task<Outcome> Measure(Func<Task<IEnumerable<string>>> action)
    {
        try { return new Outcome(null, (await action()).OrderBy(c => c, StringComparer.Ordinal).ToList()); }
        catch (Exception ex) { return new Outcome(ex, Array.Empty<string>()); }
    }

    private static IEnumerable<string> Codes(IEnumerable<T890Row> rows) => rows.Select(r => r.Code ?? "");

    private static string Show(IEnumerable<T890Row> rows) => string.Join("; ", rows.OrderBy(r => r.Code, StringComparer.Ordinal)
        .Select(r => $"{r.Code}:{r.Hits}/{r.Big}/{r.Sh}/{r.Two}/{r.Mark}"));

    /// <summary>Every column of every row, so a statement that failed can be shown to have written nothing.</summary>
    private string Snapshot() => Show(_sync!.Read(x => x.Code != null, null, null, null));

    private void Record(string kind, string path, Outcome outcome, Outcome csharp)
        => Output.WriteLine($"MEASURE | {Provider} | {kind} | {path} | {outcome} | C# checked: {csharp}");

    [Theory]
    [MemberData(nameof(FilterCases))]
    public async Task A_filter_whose_integer_arithmetic_overflows_fails_as_checked_CSharp_throws(string name, string path)
    {
        if (_settings == null) return;
        var filter = Filters[name];
        var csharp = CSharp(_seed, Checked.Of(filter).Compile(), r => r.Code);
        var before = Snapshot();

        var outcome = await Measure(path switch
        {
            "ReadAsync" => async () => Codes(await _async!.ReadAsync(filter, null, null, null)),
            "Read" => () => Task.FromResult(Codes(_sync!.Read(filter, null, null, null))),
            "CountAsync" => async () => Enumerable.Repeat("row", (int)await _async!.CountAsync(filter)),
            "Count" => () => Task.FromResult(Enumerable.Repeat("row", (int)_sync!.Count(filter))),
            "DeleteAsync" => async () => { await _async!.DeleteAsync(filter); return Gone(); },
            "UpdateAsync(PropertyUpdate)" => async () => { await _async!.UpdateAsync(filter, new PropertyUpdate<T890Row>().Set(x => x.Mark, 890)); return Marked(); },
            "UpdateAsync(Action)" => async () => { await _async!.UpdateAsync(filter, r => r.Mark = 890); return Marked(); },
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        });
        var expected = path.StartsWith("Count") ? csharp with { Codes = Enumerable.Repeat("row", csharp.Codes.Count).ToList() } : csharp;
        Record(name, path, outcome, expected);

        if (csharp.Thrown != null)
        {
            outcome.Thrown.Should().NotBeNull($"{path} with '{name}': the arithmetic overflows its C# type on a row, so the statement must fail");
            Snapshot().Should().Be(before, "a statement that failed writes nothing");
            return;
        }
        outcome.Thrown.Should().BeNull($"{path} with '{name}': nothing overflows in C#");
        outcome.Codes.Should().Equal(expected.Codes, $"{path} with '{name}' matches what C# matches");
    }

    private IEnumerable<string> Gone()
    {
        var left = Codes(_sync!.Read(x => x.Code != null, null, null, null)).ToHashSet();
        return _seed.Select(r => r.Code ?? "").Where(c => !left.Contains(c)).ToList();
    }

    private IEnumerable<string> Marked() => Codes(_sync!.Read(x => x.Mark == 890, null, null, null)).ToList();

    [Theory]
    [MemberData(nameof(SetCases))]
    public async Task A_SET_whose_integer_arithmetic_overflows_fails_and_writes_nothing(string name)
    {
        if (_settings == null) return;
        var c = Sets[name];
        var expectedRows = SeedRows().ToList();
        var selects = c.Filter.Compile();
        Exception? csharpThrown = null;
        try { foreach (var row in expectedRows.Where(selects)) c.CSharp(row)(); }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is OverflowException) { csharpThrown = ex.InnerException; }
        catch (OverflowException ex) { csharpThrown = ex; }
        var before = Snapshot();

        Exception? thrown = null;
        try { c.Apply(_connector!); }
        catch (Exception ex) { thrown = ex; }
        await Task.CompletedTask;
        var after = Snapshot();
        Output.WriteLine($"MEASURE | {Provider} | SET {name} | {(thrown == null ? "stored " + after : $"throws {thrown.GetType().Name}: {thrown.Message.Split('\n')[0]}")} | C# checked: {(csharpThrown == null ? Show(expectedRows) : "throws " + csharpThrown.GetType().Name)}");

        if (csharpThrown != null)
        {
            thrown.Should().NotBeNull($"SET {name}: the arithmetic overflows its C# type on a row, so the statement must fail");
            after.Should().Be(before, "a statement that failed writes nothing");
            return;
        }
        thrown.Should().BeNull($"SET {name}: nothing overflows in C#");
        after.Should().Be(Show(expectedRows), $"SET {name} stores what C# computes");
    }

    [Theory]
    [MemberData(nameof(ViewCases))]
    public async Task An_OnTheFly_view_filter_whose_integer_arithmetic_overflows_fails(string name, string path)
    {
        if (_settings == null) return;
        var store = new SqlViewStore<T890View>(_connector!, View(PortableMode.OnTheFly));
        var filter = ViewFilters[name];
        var rows = _seed.Select(r => new T890View { Code = r.Code, Hits = r.Hits, Two = r.Two }).ToList();
        var csharp = CSharp(rows, Checked.Of(filter).Compile(), v => v.Code);

        var outcome = await Measure(path switch
        {
            "QueryAsync" => async () => (await store.QueryAsync(filter)).Select(v => v.Code ?? ""),
            "CountAsync" => async () => Enumerable.Repeat("row", (int)await store.CountAsync(filter)),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
        });
        var expected = path == "CountAsync" ? csharp with { Codes = Enumerable.Repeat("row", csharp.Codes.Count).ToList() } : csharp;
        Record($"view {name}", $"OnTheFly {path}", outcome, expected);

        if (csharp.Thrown != null)
        {
            outcome.Thrown.Should().NotBeNull($"OnTheFly view {path} with '{name}' must fail");
            return;
        }
        outcome.Thrown.Should().BeNull();
        outcome.Codes.Should().Equal(expected.Codes);
    }
}

public sealed class SqliteTask890ArithmeticOverflowTests : Task890ArithmeticOverflowTestsBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-t890-" + Guid.NewGuid().ToString("N"));
    public SqliteTask890ArithmeticOverflowTests(ITestOutputHelper output) : base(output) => Directory.CreateDirectory(_root);
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    private SqLiteSettings? _settings;
    protected override PasswordSettings Settings() => _settings ??= new SqLiteSettings(_root, "t890.db");
    protected override IBulkStore<T890Row> SyncStore(PasswordSettings settings) { var s = new SQLiteStore<T890Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T890Row> AsyncStore(PasswordSettings settings) { var s = new AsyncSQLiteStore<T890Row>(); s.SetSettings(settings); return s; }

    public void Dispose()
    {
        if (_settings != null) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()));
        try { Directory.Delete(_root, true); } catch { }
    }
}

public sealed class PostgreSqlTask890ArithmeticOverflowTests : Task890ArithmeticOverflowTestsBase
{
    public PostgreSqlTask890ArithmeticOverflowTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IBulkStore<T890Row> SyncStore(PasswordSettings settings) { var s = new PostgreSQLStore<T890Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T890Row> AsyncStore(PasswordSettings settings) { var s = new AsyncPostgreSQLStore<T890Row>(); s.SetSettings(settings); return s; }
}

public sealed class MySqlTask890ArithmeticOverflowTests : Task890ArithmeticOverflowTestsBase
{
    public MySqlTask890ArithmeticOverflowTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IBulkStore<T890Row> SyncStore(PasswordSettings settings) { var s = new MySQLStore<T890Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T890Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMySQLStore<T890Row>(); s.SetSettings(settings); return s; }
}

public sealed class MSSqlTask890ArithmeticOverflowTests : Task890ArithmeticOverflowTestsBase
{
    public MSSqlTask890ArithmeticOverflowTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IBulkStore<T890Row> SyncStore(PasswordSettings settings) { var s = new MSSqlStore<T890Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T890Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMSSqlStore<T890Row>(); s.SetSettings(settings); return s; }
}
