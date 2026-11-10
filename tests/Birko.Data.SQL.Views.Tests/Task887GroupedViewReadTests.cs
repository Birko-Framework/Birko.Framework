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
/// TASK-887 — a grouped view reads every aggregate into its view property, on every SQL provider, whatever type the
/// server answers the aggregate in.
/// <para>
/// Each case is one aggregate over <see cref="T887Row"/> grouped by a key, the key declared first or last on the view
/// class, with or without a join, OnTheFly and Persistent. It reads the group "a" through
/// <see cref="SqlViewStore{TView}.QueryAsync"/> (the async reader), every group through the sync
/// <c>connector.Select</c> with the store's own row reader, and counts. The answer is what LINQ answers over the seeded
/// rows. Each path writes a <c>MEASURE</c> line, which is the per-provider table.
/// </para>
/// </summary>
[Collection("T887")]
public abstract class Task887GroupedViewReadTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task887GroupedViewReadTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract IAsyncBulkStore<T887Row> RowStore(PasswordSettings settings);
    protected abstract IAsyncBulkStore<T887Owner> OwnerStore(PasswordSettings settings);

    /// <summary>
    /// TASK-892: a server keeps its own scale for a decimal average (SQL Server 6 decimals), so a view refuses a decimal
    /// average, or one into a decimal property, by name before anything runs. SQLite computes it in C#'s decimal.
    /// </summary>
    protected virtual bool RefusesDecimalAverage => true;

    private bool Refuses(AggCase c) => RefusesDecimalAverage && c.Name is "Avg decimal" or "Avg int as decimal";

    [Table("T887Rows")]
    public class T887Row : AbstractModel
    {
        public string? Kind { get; set; }
        public string? OwnerCode { get; set; }
        public int Hits { get; set; }
        public int? NHits { get; set; }
        public long Big { get; set; }
        public short Small { get; set; }
        public decimal Price { get; set; }
        public double Ratio { get; set; }
        public string? Name { get; set; }
        public DateTime Stamp { get; set; }
    }

    [Table("T887Owners")]
    public class T887Owner : AbstractModel
    {
        public string? Code { get; set; }
        public string? Label { get; set; }
    }

    public interface IKindView
    {
        string? Kind { get; }
        object? Boxed { get; }
    }

    /// <summary>The group key declared before the aggregate.</summary>
    public class KeyFirst<TValue> : IKindView
    {
        public string? Kind { get; set; }
        public TValue Value { get; set; } = default!;
        object? IKindView.Boxed => Value;
    }

    /// <summary>The group key declared after the aggregate.</summary>
    public class KeyLast<TValue> : IKindView
    {
        public TValue Value { get; set; } = default!;
        public string? Kind { get; set; }
        object? IKindView.Boxed => Value;
    }

    private static readonly T887Owner[] Owners =
    {
        new() { Code = "o1", Label = "a" },
        new() { Code = "o2", Label = "b" },
    };

    /// <summary>
    /// Group "a" holds a <see cref="Big"/> past <see cref="int.MaxValue"/>, so a read through a 32-bit getter cannot pass
    /// for right; its int average is fractional (2.5).
    /// </summary>
    private static readonly T887Row[] Rows =
    {
        new() { Kind = "a", OwnerCode = "o1", Hits = 1, NHits = 1, Big = 10, Small = 1, Price = 1.25m, Ratio = 0.5, Name = "m", Stamp = new DateTime(2026, 1, 2, 3, 4, 5) },
        new() { Kind = "a", OwnerCode = "o1", Hits = 4, NHits = null, Big = 3_000_000_000, Small = 7, Price = 2.75m, Ratio = 1.5, Name = "k", Stamp = new DateTime(2026, 3, 4, 5, 6, 7) },
        new() { Kind = "b", OwnerCode = "o2", Hits = 5, NHits = 5, Big = 2, Small = 2, Price = 9.5m, Ratio = 4, Name = "z", Stamp = new DateTime(2025, 1, 1) },
        new() { Kind = null, OwnerCode = "o2", Hits = 2, NHits = null, Big = 1, Small = 3, Price = 0.5m, Ratio = 2, Name = "q", Stamp = new DateTime(2024, 6, 1) },
    };

    /// <summary>One aggregate case: what it builds, and what LINQ answers for a group's rows.</summary>
    public abstract class AggCase
    {
        public required string Name { get; init; }
        public abstract Type ValueType { get; }
        public abstract ViewDefinition Build<TView>(string viewName, PortableMode mode, bool join) where TView : class, IKindView;
        public abstract object? Expected(IReadOnlyList<T887Row> group);
        public override string ToString() => Name;
    }

    public sealed class AggCase<TValue> : AggCase
    {
        public required AggregateFunction Function { get; init; }
        public Expression<Func<T887Row, TValue>>? Source { get; init; }
        public required Func<IReadOnlyList<T887Row>, object?> Linq { get; init; }
        public override Type ValueType => typeof(TValue);
        public override object? Expected(IReadOnlyList<T887Row> group) => Linq(group);

        public override ViewDefinition Build<TView>(string viewName, PortableMode mode, bool join)
        {
            var builder = new ViewDefinitionBuilder<TView>().HasName(viewName).HasQueryMode(mode).From<T887Row>();
            builder = join
                ? builder.Join<T887Row, T887Owner, string?>(r => r.OwnerCode, o => o.Code)
                    .Select<T887Owner, string?>(o => o.Label, ViewMember<TView, string?>("Kind"))
                    .GroupBy<T887Owner, string?>(o => o.Label)
                : builder.Select<T887Row, string?>(r => r.Kind, ViewMember<TView, string?>("Kind"))
                    .GroupBy<T887Row, string?>(r => r.Kind);
            if (Function == AggregateFunction.Count)
            {
                return (typeof(TValue) == typeof(long)
                    ? builder.Count<T887Row>(ViewMember<TView, long>("Value"))
                    : builder.Count<T887Row>(ViewMember<TView, int>("Value"))).Build();
            }
            return builder.Aggregate(Function, Source!, ViewMember<TView, TValue>("Value")).Build();
        }
    }

    private static Expression<Func<TView, TProp>> ViewMember<TView, TProp>(string name)
    {
        var parameter = Expression.Parameter(typeof(TView), "v");
        return Expression.Lambda<Func<TView, TProp>>(Expression.Property(parameter, name), parameter);
    }

    public static readonly AggCase[] Cases =
    {
        new AggCase<int> { Name = "Count int", Function = AggregateFunction.Count, Linq = g => g.Count },
        new AggCase<long> { Name = "Count long", Function = AggregateFunction.Count, Linq = g => (long)g.Count },
        new AggCase<int> { Name = "Sum int", Function = AggregateFunction.Sum, Source = r => r.Hits, Linq = g => g.Sum(r => r.Hits) },
        new AggCase<int?> { Name = "Sum int?", Function = AggregateFunction.Sum, Source = r => r.NHits, Linq = g => g.Sum(r => r.NHits) }, // TASK-892: 0 over only NULLs, as LINQ
        new AggCase<long> { Name = "Sum long", Function = AggregateFunction.Sum, Source = r => r.Big, Linq = g => g.Sum(r => r.Big) },
        new AggCase<short> { Name = "Sum short", Function = AggregateFunction.Sum, Source = r => r.Small, Linq = g => (short)g.Sum(r => r.Small) },
        new AggCase<decimal> { Name = "Sum decimal", Function = AggregateFunction.Sum, Source = r => r.Price, Linq = g => g.Sum(r => r.Price) },
        new AggCase<double> { Name = "Sum double", Function = AggregateFunction.Sum, Source = r => r.Ratio, Linq = g => g.Sum(r => r.Ratio) },
        new AggCase<decimal> { Name = "Avg decimal", Function = AggregateFunction.Avg, Source = r => r.Price, Linq = g => g.Average(r => r.Price) },
        new AggCase<double> { Name = "Avg double", Function = AggregateFunction.Avg, Source = r => r.Ratio, Linq = g => g.Average(r => r.Ratio) },
        new AggCase<long> { Name = "Avg long", Function = AggregateFunction.Avg, Source = r => r.Big, Linq = g => AverageOrRefuse<long>(g.Select(r => (double)r.Big)) },
        new AggCase<int> { Name = "Avg int", Function = AggregateFunction.Avg, Source = r => r.Hits, Linq = g => AverageOrRefuse<int>(g.Select(r => (double)r.Hits)) },
        new AggCase<int> { Name = "Min int", Function = AggregateFunction.Min, Source = r => r.Hits, Linq = g => g.Min(r => r.Hits) },
        new AggCase<long> { Name = "Max long", Function = AggregateFunction.Max, Source = r => r.Big, Linq = g => g.Max(r => r.Big) },
        new AggCase<short> { Name = "Max short", Function = AggregateFunction.Max, Source = r => r.Small, Linq = g => g.Max(r => r.Small) },
        new AggCase<decimal> { Name = "Min decimal", Function = AggregateFunction.Min, Source = r => r.Price, Linq = g => g.Min(r => r.Price) },
        new AggCase<double> { Name = "Max double", Function = AggregateFunction.Max, Source = r => r.Ratio, Linq = g => g.Max(r => r.Ratio) },
        new AggCase<string?> { Name = "Min string", Function = AggregateFunction.Min, Source = r => r.Name, Linq = g => g.Min(r => r.Name) },
        new AggCase<DateTime> { Name = "Max DateTime", Function = AggregateFunction.Max, Source = r => r.Stamp, Linq = g => g.Max(r => r.Stamp) },
        new AggCase<int?> { Name = "Max int?", Function = AggregateFunction.Max, Source = r => r.NHits, Linq = g => g.Max(r => r.NHits) },
        // A source widened by the builder's Convert: the view property's type is not the column's.
        new AggCase<double> { Name = "Avg int as double", Function = AggregateFunction.Avg, Source = r => r.Hits, Linq = g => g.Average(r => r.Hits) },
        new AggCase<decimal> { Name = "Avg int as decimal", Function = AggregateFunction.Avg, Source = r => r.Hits, Linq = g => g.Average(r => (decimal)r.Hits) },
        new AggCase<long> { Name = "Sum int as long", Function = AggregateFunction.Sum, Source = r => r.Hits, Linq = g => g.Sum(r => (long)r.Hits) },
        new AggCase<long> { Name = "Max int as long", Function = AggregateFunction.Max, Source = r => r.Hits, Linq = g => (long)g.Max(r => r.Hits) },
        new AggCase<double> { Name = "Sum long as double", Function = AggregateFunction.Sum, Source = r => r.Big, Linq = g => g.Sum(r => (double)r.Big) },
    };

    /// <summary>An integral average that is not a whole number has no value in an integral property.</summary>
    private static object AverageOrRefuse<TValue>(IEnumerable<double> values)
    {
        var average = values.Average();
        return average == Math.Floor(average) ? Convert.ChangeType(average, typeof(TValue)) : Refused.Instance;
    }

    /// <summary>The expected answer is an exception: the value cannot be put in the view property without loss.</summary>
    private sealed class Refused
    {
        public static readonly Refused Instance = new();
        public override string ToString() => "refused";
    }

    public static IEnumerable<object[]> Matrix
        => from c in Cases
           from keyLast in new[] { false, true }
           from joined in new[] { false, true }
           from mode in new[] { PortableMode.OnTheFly, PortableMode.Persistent }
           select new object[] { c.Name, keyLast, joined, mode };

    private PasswordSettings? _settings;
    private IAsyncBulkStore<T887Row>? _rows;
    private IAsyncBulkStore<T887Owner>? _owners;
    private AbstractConnector? _connector;
    private readonly List<string> _views = new();

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
        _rows = RowStore(_settings);
        _owners = OwnerStore(_settings);
        _connector = ((ISqlConnectedStore)_rows).Connector!;
        Clean();
        foreach (var row in Rows) await _rows.CreateAsync(Copy(row));
        foreach (var owner in Owners) await _owners.CreateAsync(new T887Owner { Code = owner.Code, Label = owner.Label });
    }

    public async Task DisposeAsync()
    {
        if (_settings == null) return;
        foreach (var view in _views)
        {
            try { await new SqlViewManager(_connector!).DropAsync(view); } catch { }
        }
        Clean();
    }

    private void Clean()
    {
        try { _connector!.DropTable(new[] { typeof(T887Row), typeof(T887Owner) }); } catch { }
    }

    private static T887Row Copy(T887Row r) => new()
    {
        Kind = r.Kind, OwnerCode = r.OwnerCode, Hits = r.Hits, NHits = r.NHits, Big = r.Big, Small = r.Small, Price = r.Price,
        Ratio = r.Ratio, Name = r.Name, Stamp = r.Stamp,
    };

    private static string? GroupKey(T887Row row, bool join)
        => join ? Owners.First(o => o.Code == row.OwnerCode).Label : row.Kind;

    private static object? ExpectedFor(AggCase c, string? key, bool join)
        => c.Expected(Rows.Where(r => GroupKey(r, join) == key).ToList());

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task A_grouped_view_reads_each_aggregate_as_LINQ_answers(string caseName, bool keyLast, bool join, PortableMode mode)
    {
        if (_settings == null) return;
        var c = Cases.Single(x => x.Name == caseName);
        var runner = typeof(Task887GroupedViewReadTestsBase).GetMethod(nameof(Run), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var viewType = (keyLast ? typeof(KeyLast<>) : typeof(KeyFirst<>)).MakeGenericType(c.ValueType);
        await (Task)runner.MakeGenericMethod(viewType).Invoke(this, new object[] { c, keyLast, join, mode })!;
    }

    private async Task Run<TView>(AggCase c, bool keyLast, bool join, PortableMode mode) where TView : class, IKindView, new()
    {
        var shape = $"{c.Name} | key {(keyLast ? "last" : "first")} | {(join ? "join" : "one table")} | {mode}";
        var viewName = $"T887V{Array.IndexOf(Cases, c)}{(keyLast ? 1 : 0)}{(join ? 1 : 0)}{(int)mode}";
        var definition = c.Build<TView>(viewName, mode, join);
        if (mode == PortableMode.Persistent)
        {
            _views.Add(viewName);
            if (Refuses(c))
            {
                var ensure = () => new SqlViewManager(_connector!).EnsureAsync(definition);
                (await ensure.Should().ThrowAsync<NotSupportedException>()).WithMessage("*Value*TASK-892*");
                return;
            }
            await new SqlViewManager(_connector!).EnsureAsync(definition);
        }
        var store = new SqlViewStore<TView>(_connector!, definition);
        var parameter = Expression.Parameter(typeof(TView), "v");
        var filter = Expression.Lambda<Func<TView, bool>>(
            Expression.Equal(Expression.Property(parameter, "Kind"), Expression.Constant("a", typeof(string))), parameter);
        var expectedA = Refuses(c) ? Refused.Instance : ExpectedFor(c, "a", join);

        var failures = new List<string>();

        // The async reader, group "a".
        var query = await Measure(async () => (await store.QueryAsync(filter)).Cast<IKindView>().ToList());
        Record(shape, "QueryAsync(a)", query, expectedA);
        Check("QueryAsync(a)", query, new (string? Key, object? Value)[] { ("a", expectedA) }, failures);

        // The sync reader, every group, through the store's own row reader.
        var expectedAll = Rows.Select(r => GroupKey(r, join)).Distinct().Select(k => (k, Refuses(c) ? Refused.Instance : ExpectedFor(c, k, join))).ToArray();
        var sqlView = SqlViewTranslator.Translate(definition);
        var select = await Measure(() => Task.FromResult(_connector!
            .Select(sqlView, SqlViewStore<TView>.RowReader(sqlView), (IEnumerable<Conditions.Condition>?)null)
            .Cast<IKindView>().ToList()));
        Record(shape, "connector.Select(all)", select, expectedAll);
        Check("connector.Select(all)", select, expectedAll, failures);

        var count = await Measure(async () => await store.CountAsync(filter));
        Output.WriteLine($"MEASURE | {Provider} | {shape} | CountAsync(a) | {(count.Thrown == null ? count.Value.ToString() : Describe(count.Thrown))}");
        if (count.Thrown != null || count.Value != 1) failures.Add($"CountAsync(a): {(count.Thrown == null ? count.Value.ToString() : Describe(count.Thrown))}");

        failures.Should().BeEmpty($"{Provider} | {shape}");
    }

    private sealed record Outcome<T>(T Value, Exception? Thrown);

    private static async Task<Outcome<T>> Measure<T>(Func<Task<T>> action)
    {
        try { return new Outcome<T>(await action(), null); }
        catch (Exception ex) { return new Outcome<T>(default!, ex); }
    }

    private static string Describe(Exception ex)
    {
        while (ex is System.Reflection.TargetInvocationException { InnerException: not null }) ex = ex.InnerException;
        return $"throws {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
    }

    private static string Show(object? value) => value switch
    {
        null => "null",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss"),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string Show(IEnumerable<IKindView> rows)
        => string.Join(", ", rows.OrderBy(r => r.Kind ?? "").Select(r => $"{r.Kind ?? "null"}={Show(r.Boxed)}"));

    private void Record(string shape, string path, Outcome<List<IKindView>> outcome, object? expected)
        => Output.WriteLine($"MEASURE | {Provider} | {shape} | {path} | {(outcome.Thrown == null ? Show(outcome.Value) : Describe(outcome.Thrown))} | expected {Show(expected)}");

    private void Record(string shape, string path, Outcome<List<IKindView>> outcome, (string? Key, object? Value)[] expected)
        => Output.WriteLine($"MEASURE | {Provider} | {shape} | {path} | {(outcome.Thrown == null ? Show(outcome.Value) : Describe(outcome.Thrown))} | expected {string.Join(", ", expected.OrderBy(e => e.Key ?? "").Select(e => $"{e.Key ?? "null"}={Show(e.Value)}"))}");

    private static void Check(string path, Outcome<List<IKindView>> outcome, (string? Key, object? Value)[] expected, List<string> failures)
    {
        var refusedGroups = expected.Where(e => e.Value is Refused).ToList();
        if (refusedGroups.Count > 0)
        {
            if (outcome.Thrown == null) failures.Add($"{path}: answered {Show(outcome.Value)}, but {string.Join(", ", refusedGroups.Select(e => e.Key))} has no exact value in the view property");
            return;
        }
        if (outcome.Thrown != null)
        {
            failures.Add($"{path}: {Describe(outcome.Thrown)}");
            return;
        }
        var actual = outcome.Value.OrderBy(r => r.Kind ?? "").ToList();
        var wanted = expected.OrderBy(e => e.Key ?? "").ToList();
        var same = actual.Count == wanted.Count
                   && actual.Zip(wanted).All(p => p.First.Kind == p.Second.Key
                                                  && Equals(p.First.Boxed, p.Second.Value));
        if (!same) failures.Add($"{path}: answered {Show(outcome.Value)}");
    }
}

public sealed class SqliteTask887GroupedViewReadTests : Task887GroupedViewReadTestsBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-t887-" + Guid.NewGuid().ToString("N"));
    public SqliteTask887GroupedViewReadTests(ITestOutputHelper output) : base(output) => Directory.CreateDirectory(_root);
    protected override string Provider => "SQLite";
    protected override string? HostVariable => null;
    protected override bool RefusesDecimalAverage => false;
    private SqLiteSettings? _settings;
    protected override PasswordSettings Settings() => _settings ??= new SqLiteSettings(_root, "t887.db");
    protected override IAsyncBulkStore<T887Row> RowStore(PasswordSettings settings) { var s = new AsyncSQLiteStore<T887Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T887Owner> OwnerStore(PasswordSettings settings) { var s = new AsyncSQLiteStore<T887Owner>(); s.SetSettings(settings); return s; }

    public void Dispose()
    {
        if (_settings != null) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()));
        try { Directory.Delete(_root, true); } catch { }
    }
}

public sealed class PostgreSqlTask887GroupedViewReadTests : Task887GroupedViewReadTestsBase
{
    public PostgreSqlTask887GroupedViewReadTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IAsyncBulkStore<T887Row> RowStore(PasswordSettings settings) { var s = new AsyncPostgreSQLStore<T887Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T887Owner> OwnerStore(PasswordSettings settings) { var s = new AsyncPostgreSQLStore<T887Owner>(); s.SetSettings(settings); return s; }
}

public sealed class MySqlTask887GroupedViewReadTests : Task887GroupedViewReadTestsBase
{
    public MySqlTask887GroupedViewReadTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IAsyncBulkStore<T887Row> RowStore(PasswordSettings settings) { var s = new AsyncMySQLStore<T887Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T887Owner> OwnerStore(PasswordSettings settings) { var s = new AsyncMySQLStore<T887Owner>(); s.SetSettings(settings); return s; }
}

public sealed class MSSqlTask887GroupedViewReadTests : Task887GroupedViewReadTestsBase
{
    public MSSqlTask887GroupedViewReadTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IAsyncBulkStore<T887Row> RowStore(PasswordSettings settings) { var s = new AsyncMSSqlStore<T887Row>(); s.SetSettings(settings); return s; }
    protected override IAsyncBulkStore<T887Owner> OwnerStore(PasswordSettings settings) { var s = new AsyncMSSqlStore<T887Owner>(); s.SetSettings(settings); return s; }
}
