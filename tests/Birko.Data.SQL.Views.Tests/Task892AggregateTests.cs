using System;
using System.Collections.Generic;
using System.Globalization;
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
/// TASK-892 — SQL aggregates answer what LINQ answers, store-level and in a view, on every SQL provider.
/// <para>
/// The store-level <c>Aggregate</c> / <c>AggregateAsync</c> is compared with <see cref="AggregateHelper.LinqAggregate"/>,
/// the in-memory aggregation every other store matches (TASK-876, TASK-878): the same groups under the same names, each
/// value of the same CLR type and equal. A view aggregate is compared with LINQ's operator over the group's rows. Each
/// case writes a <c>MEASURE</c> line, which is the per-provider table.
/// </para>
/// <para>
/// Recorded, not asserted exactly: a SUM or AVG over a <c>double</c> column is compared to 15 significant digits. The
/// order in which a server adds the rows is not LINQ's (SQLite and SQL Server summed 0.1 + 0.2 + 0.3 to 0.6, LINQ to
/// 0.6000000000000001), and no SQL fixes the order of a group's rows.
/// </para>
/// </summary>
[Collection("T892")]
public abstract class Task892AggregateTestsBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected Task892AggregateTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string Provider { get; }
    protected abstract string? HostVariable { get; }
    protected abstract PasswordSettings Settings();
    protected abstract IAsyncBulkStore<T892Row> AsyncStore(PasswordSettings settings);
    protected abstract IBulkStore<T892Row> SyncStore(PasswordSettings settings);

    /// <summary>
    /// Whether a view can average a decimal (or average into one) as C# does. SQLite computes it in C#'s decimal; a server
    /// keeps its own scale, so the view refuses it there.
    /// </summary>
    protected virtual bool ViewAveragesDecimalsExactly => false;

    [Table("T892Rows")]
    public class T892Row : AbstractModel
    {
        public string? Kind { get; set; }
        public int Hits { get; set; }
        public int? NHits { get; set; }
        public long Big { get; set; }
        public long? NBig { get; set; }
        public short Small { get; set; }
        public decimal Price { get; set; }
        public decimal? NPrice { get; set; }
        public double Ratio { get; set; }
        public double? NRatio { get; set; }
        public string? Name { get; set; }
        public DateTime Stamp { get; set; }
    }

    /// <summary>
    /// Group "a" averages 4/3 over <see cref="T892Row.Hits"/> (SQL Server's integer AVG said 1) and sums
    /// <see cref="T892Row.Price"/> past what a double holds; group "n" holds only NULLs in every nullable column. The groups'
    /// decimal sums (0.5, 2, 9.5, 1234567890124.45679) order differently as text and as numbers.
    /// </summary>
    internal static readonly T892Row[] Rows =
    {
        new() { Kind = "a", Hits = 1, NHits = 1, Big = 10, NBig = 10, Small = 1, Price = 1234567890123.456789m, NPrice = 1m, Ratio = 0.1, NRatio = 0.1, Name = "m", Stamp = new DateTime(2026, 1, 2, 3, 4, 5) },
        new() { Kind = "a", Hits = 1, NHits = null, Big = 3_000_000_000, NBig = null, Small = 7, Price = 0.000001m, NPrice = null, Ratio = 0.2, NRatio = null, Name = "k", Stamp = new DateTime(2026, 3, 4, 5, 6, 7) },
        new() { Kind = "a", Hits = 2, NHits = 2, Big = 1, NBig = 1, Small = 2, Price = 1m, NPrice = 2m, Ratio = 0.3, NRatio = 0.3, Name = "z", Stamp = new DateTime(2025, 1, 1) },
        new() { Kind = "b", Hits = 5, NHits = 5, Big = 2, NBig = 2, Small = 2, Price = 9.5m, NPrice = 9.5m, Ratio = 4, NRatio = 4, Name = "q", Stamp = new DateTime(2024, 6, 1) },
        new() { Kind = "n", Hits = 3, NHits = null, Big = 4, NBig = null, Small = 5, Price = 2m, NPrice = null, Ratio = 1, NRatio = null, Name = "p", Stamp = new DateTime(2023, 2, 1) },
        new() { Kind = null, Hits = 2, NHits = null, Big = 1, NBig = null, Small = 3, Price = 0.5m, NPrice = null, Ratio = 2, NRatio = null, Name = "r", Stamp = new DateTime(2024, 6, 1) },
    };

    private static readonly string[] NumericSources = { "Hits", "NHits", "Big", "NBig", "Small", "Price", "NPrice", "Ratio", "NRatio" };

    public static readonly Dictionary<string, AggregateField> StoreAggregates = BuildStoreAggregates();

    private static Dictionary<string, AggregateField> BuildStoreAggregates()
    {
        var result = new Dictionary<string, AggregateField> { ["Count"] = new(AggregateFunction.Count, "Hits", "v") };
        foreach (var source in NumericSources)
        {
            foreach (var function in new[] { AggregateFunction.Sum, AggregateFunction.Avg, AggregateFunction.Min, AggregateFunction.Max })
            {
                result[$"{function} {source}"] = new(function, source, "v");
            }
        }
        foreach (var source in new[] { "Name", "Stamp" })
        {
            result[$"Min {source}"] = new(AggregateFunction.Min, source, "v");
            result[$"Max {source}"] = new(AggregateFunction.Max, source, "v");
        }
        return result;
    }

    public static IEnumerable<object[]> StoreMatrix
        => from name in StoreAggregates.Keys
           from grouped in new[] { false, true }
           from async in new[] { false, true }
           select new object[] { name, grouped, async };

    private PasswordSettings? _settings;
    private IAsyncBulkStore<T892Row>? _async;
    private IBulkStore<T892Row>? _sync;
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
        _async = AsyncStore(_settings);
        _sync = SyncStore(_settings);
        _connector = ((ISqlConnectedStore)_async).Connector!;
        Clean();
        foreach (var row in Rows) await _async.CreateAsync(Copy(row));
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
        try { _connector!.DropTable(new[] { typeof(T892Row) }); } catch { }
    }

    private static T892Row Copy(T892Row r) => new()
    {
        Kind = r.Kind, Hits = r.Hits, NHits = r.NHits, Big = r.Big, NBig = r.NBig, Small = r.Small, Price = r.Price,
        NPrice = r.NPrice, Ratio = r.Ratio, NRatio = r.NRatio, Name = r.Name, Stamp = r.Stamp,
    };

    // ---- store-level Aggregate ----

    private async Task<IReadOnlyList<AggregateResult>> Aggregate(AggregateQuery<T892Row> query, bool async)
        => async ? await ((IAsyncAggregatableStore<T892Row>)_async!).AggregateAsync(query) : ((IAggregatableStore<T892Row>)_sync!).Aggregate(query);

    private static bool FloatingSum(AggregateField aggregate)
        => aggregate.Function is AggregateFunction.Sum or AggregateFunction.Avg && aggregate.SourcePropertyName is "Ratio" or "NRatio";

    [Theory]
    [MemberData(nameof(StoreMatrix))]
    public async Task A_store_aggregate_answers_what_the_in_memory_aggregation_answers(string name, bool grouped, bool async)
    {
        if (_settings == null) return;
        var aggregate = StoreAggregates[name];
        var query = new AggregateQuery<T892Row>
        {
            GroupByFields = grouped ? new[] { "Kind" } : Array.Empty<string>(),
            Aggregates = new[] { aggregate },
        };
        var expected = Normalise(AggregateHelper.LinqAggregate(Rows, query), FloatingSum(aggregate), sort: true);
        string actual;
        try
        {
            actual = Normalise(await Aggregate(query, async), FloatingSum(aggregate), sort: true);
        }
        catch (Exception ex)
        {
            actual = Describe(ex);
        }

        Output.WriteLine($"MEASURE | {Provider} | store | {name} | {(grouped ? "grouped" : "ungrouped")} | {(async ? "async" : "sync")} | {actual} | expected {expected}");
        actual.Should().Be(expected);
    }

    /// <summary>
    /// Groups ordered by a decimal aggregate come in the order of its value. On SQLite a decimal aggregate is text, which
    /// orders byte-wise ('0.5', '1234567890124.45679', '2.0', '9.5') unless the decimal collation names it.
    /// </summary>
    [Theory]
    [InlineData(AggregateFunction.Sum, false)]
    [InlineData(AggregateFunction.Sum, true)]
    [InlineData(AggregateFunction.Avg, false)]
    [InlineData(AggregateFunction.Avg, true)]
    [InlineData(AggregateFunction.Min, false)]
    [InlineData(AggregateFunction.Max, true)]
    public async Task A_store_aggregate_ordered_by_a_decimal_aggregate_orders_by_its_value(AggregateFunction function, bool async)
    {
        if (_settings == null) return;
        foreach (var descending in new[] { false, true })
        {
            var query = new AggregateQuery<T892Row>
            {
                GroupByFields = new[] { "Kind" },
                Aggregates = new[] { new AggregateField(function, "Price", "v") },
                OrderBy = new Dictionary<string, bool> { ["v"] = descending },
            };
            var expected = Normalise(AggregateHelper.LinqAggregate(Rows, query), floating: false, sort: false);
            string actual;
            try { actual = Normalise(await Aggregate(query, async), floating: false, sort: false); }
            catch (Exception ex) { actual = Describe(ex); }

            Output.WriteLine($"MEASURE | {Provider} | store order | {function} Price {(descending ? "desc" : "asc")} | {(async ? "async" : "sync")} | {actual} | expected {expected}");
            actual.Should().Be(expected);
        }
    }

    /// <summary>A SUM or AVG of a column that is not a number is refused by name, as <see cref="AggregateHelper"/> refuses it.</summary>
    [Theory]
    [InlineData(AggregateFunction.Sum, "Name", false)]
    [InlineData(AggregateFunction.Avg, "Name", true)]
    [InlineData(AggregateFunction.Sum, "Stamp", true)]
    public async Task A_sum_or_average_of_a_non_numeric_column_is_refused_as_LINQ_refuses_it(AggregateFunction function, string source, bool async)
    {
        if (_settings == null) return;
        var query = new AggregateQuery<T892Row> { Aggregates = new[] { new AggregateField(function, source, "v") } };
        ((Action)(() => AggregateHelper.LinqAggregate(Rows, query))).Should().Throw<NotSupportedException>();

        var act = () => Aggregate(query, async);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    /// <summary>A result as text: each value with its CLR type, so a type that differs is a difference; rows sorted unless the order is the point.</summary>
    internal static string Normalise(IEnumerable<AggregateResult> results, bool floating, bool sort)
    {
        var rows = results.Select(r => string.Join(", ", r.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={Show(kv.Value, floating)}")));
        return string.Join(" ; ", sort ? rows.OrderBy(s => s, StringComparer.Ordinal) : rows);
    }

    /// <summary>
    /// A value with its type. A decimal by its value, not its scale (a server answers 9.500000 for LINQ's 9.5); a double
    /// exactly, or to 15 significant digits for a <paramref name="floating"/> sum (see the class remarks).
    /// </summary>
    internal static string Show(object? value, bool floating = false) => value switch
    {
        null => "null",
        DateTime d => $"DateTime:{d:yyyy-MM-dd HH:mm:ss}",
        double d => $"Double:{d.ToString(floating ? "G15" : "R", CultureInfo.InvariantCulture)}",
        decimal m => $"Decimal:{m.ToString("0.############################", CultureInfo.InvariantCulture)}",
        IFormattable f => $"{value.GetType().Name}:{f.ToString(null, CultureInfo.InvariantCulture)}",
        _ => $"{value.GetType().Name}:{value}",
    };

    private static string Describe(Exception ex)
    {
        while (ex is System.Reflection.TargetInvocationException { InnerException: not null }) ex = ex.InnerException;
        return $"throws {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
    }

    // ---- view aggregates ----

    public interface IKindView
    {
        string? Kind { get; }
        object? Boxed { get; }
    }

    public class KindView<TValue> : IKindView
    {
        public string? Kind { get; set; }
        public TValue Value { get; set; } = default!;
        object? IKindView.Boxed => Value;
    }

    public abstract class ViewCase
    {
        public required string Name { get; init; }
        public abstract Type ValueType { get; }
        /// <summary>A decimal average, or one into a decimal: exact on SQLite, refused by a server (see <see cref="ViewAveragesDecimalsExactly"/>).</summary>
        public bool DecimalAverage { get; init; }
        /// <summary>A SUM or AVG of doubles, compared to 15 significant digits (see the class remarks).</summary>
        public bool Floating { get; init; }
        public abstract ViewDefinition Build<TView>(string viewName, PortableMode mode) where TView : class, IKindView;
        public abstract object? Expected(IReadOnlyList<T892Row> group);
        public override string ToString() => Name;
    }

    public sealed class ViewCase<TValue> : ViewCase
    {
        public required AggregateFunction Function { get; init; }
        public required Expression<Func<T892Row, TValue>> Source { get; init; }
        public required Func<IReadOnlyList<T892Row>, object?> Linq { get; init; }
        public override Type ValueType => typeof(TValue);
        public override object? Expected(IReadOnlyList<T892Row> group) => Linq(group);

        public override ViewDefinition Build<TView>(string viewName, PortableMode mode)
            => new ViewDefinitionBuilder<TView>().HasName(viewName).HasQueryMode(mode).From<T892Row>()
                .Select<T892Row, string?>(r => r.Kind, ViewMember<TView, string?>("Kind"))
                .GroupBy<T892Row, string?>(r => r.Kind)
                .Aggregate(Function, Source, ViewMember<TView, TValue>("Value"))
                .Build();
    }

    private static Expression<Func<TView, TProp>> ViewMember<TView, TProp>(string name)
    {
        var parameter = Expression.Parameter(typeof(TView), "v");
        return Expression.Lambda<Func<TView, TProp>>(Expression.Property(parameter, name), parameter);
    }

    public static readonly ViewCase[] ViewCases =
    {
        // A SUM over only NULLs: LINQ's Sum answers 0 for int? as for int; SQL answered NULL on all four providers.
        new ViewCase<int?> { Name = "Sum int?", Function = AggregateFunction.Sum, Source = r => r.NHits, Linq = g => g.Sum(r => r.NHits) },
        new ViewCase<long?> { Name = "Sum long?", Function = AggregateFunction.Sum, Source = r => r.NBig, Linq = g => g.Sum(r => r.NBig) },
        new ViewCase<decimal?> { Name = "Sum decimal?", Function = AggregateFunction.Sum, Source = r => r.NPrice, Linq = g => g.Sum(r => r.NPrice) },
        new ViewCase<double?> { Name = "Sum double?", Function = AggregateFunction.Sum, Source = r => r.NRatio, Linq = g => g.Sum(r => r.NRatio), Floating = true },
        // Min, Max and Average over only NULLs are null in LINQ too.
        new ViewCase<int?> { Name = "Min int?", Function = AggregateFunction.Min, Source = r => r.NHits, Linq = g => g.Min(r => r.NHits) },
        new ViewCase<int?> { Name = "Max int?", Function = AggregateFunction.Max, Source = r => r.NHits, Linq = g => g.Max(r => r.NHits) },
        new ViewCase<double?> { Name = "Avg int? as double?", Function = AggregateFunction.Avg, Source = r => r.NHits, Linq = g => g.Average(r => r.NHits) },
        new ViewCase<decimal> { Name = "Sum decimal", Function = AggregateFunction.Sum, Source = r => r.Price, Linq = g => g.Sum(r => r.Price) },
        new ViewCase<decimal> { Name = "Avg decimal", Function = AggregateFunction.Avg, Source = r => r.Price, Linq = g => g.Average(r => r.Price), DecimalAverage = true },
        new ViewCase<decimal?> { Name = "Avg decimal?", Function = AggregateFunction.Avg, Source = r => r.NPrice, Linq = g => g.Average(r => r.NPrice), DecimalAverage = true },
        new ViewCase<decimal> { Name = "Avg int as decimal", Function = AggregateFunction.Avg, Source = r => r.Hits, Linq = g => g.Average(r => (decimal)r.Hits), DecimalAverage = true },
        new ViewCase<decimal> { Name = "Avg long as decimal", Function = AggregateFunction.Avg, Source = r => r.Big, Linq = g => g.Average(r => (decimal)r.Big), DecimalAverage = true },
        new ViewCase<double> { Name = "Avg int as double", Function = AggregateFunction.Avg, Source = r => r.Hits, Linq = g => g.Average(r => r.Hits) },
        new ViewCase<double> { Name = "Avg long as double", Function = AggregateFunction.Avg, Source = r => r.Big, Linq = g => g.Average(r => r.Big) },
        new ViewCase<double> { Name = "Avg double", Function = AggregateFunction.Avg, Source = r => r.Ratio, Linq = g => g.Average(r => r.Ratio), Floating = true },
        new ViewCase<decimal> { Name = "Min decimal", Function = AggregateFunction.Min, Source = r => r.Price, Linq = g => g.Min(r => r.Price) },
        new ViewCase<decimal> { Name = "Max decimal", Function = AggregateFunction.Max, Source = r => r.Price, Linq = g => g.Max(r => r.Price) },
    };

    public static IEnumerable<object[]> ViewMatrix
        => from c in ViewCases
           from mode in new[] { PortableMode.OnTheFly, PortableMode.Persistent }
           select new object[] { c.Name, mode };

    private const string RefusedByName = "refused (NotSupportedException naming the view property)";

    [Theory]
    [MemberData(nameof(ViewMatrix))]
    public Task A_view_aggregate_answers_what_LINQ_answers(string caseName, PortableMode mode)
        => RunViewCase(caseName, mode, ordered: false);

    public static IEnumerable<object[]> OrderedViewMatrix
        => from name in new[] { "Sum decimal", "Avg decimal", "Min decimal", "Max decimal" }
           from mode in new[] { PortableMode.OnTheFly, PortableMode.Persistent }
           select new object[] { name, mode };

    /// <summary>A view ordered by a decimal aggregate returns its groups in the order of the value (see the store twin).</summary>
    [Theory]
    [MemberData(nameof(OrderedViewMatrix))]
    public Task A_view_ordered_by_a_decimal_aggregate_orders_by_its_value(string caseName, PortableMode mode)
        => RunViewCase(caseName, mode, ordered: true);

    private async Task RunViewCase(string caseName, PortableMode mode, bool ordered)
    {
        if (_settings == null) return;
        var c = ViewCases.Single(x => x.Name == caseName);
        var runner = typeof(Task892AggregateTestsBase).GetMethod(nameof(RunView), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)runner.MakeGenericMethod(typeof(KindView<>).MakeGenericType(c.ValueType)).Invoke(this, new object[] { c, mode, ordered })!;
    }

    private async Task RunView<TView>(ViewCase c, PortableMode mode, bool ordered) where TView : class, IKindView, new()
    {
        var viewName = $"T892V{Array.IndexOf(ViewCases, c)}{(int)mode}{(ordered ? 1 : 0)}";
        var groups = Rows.Select(r => r.Kind).Distinct()
            .Select(k => (Key: k, Value: c.Expected(Rows.Where(r => r.Kind == k).ToList())))
            .ToList();
        // LINQ orders by the value, descending; the key breaks no tie here, the sums are distinct.
        groups = ordered
            ? groups.OrderByDescending(g => g.Value as IComparable).ToList()
            : groups.OrderBy(g => g.Key ?? "", StringComparer.Ordinal).ToList();
        var expected = c.DecimalAverage && !ViewAveragesDecimalsExactly
            ? RefusedByName
            : string.Join(", ", groups.Select(g => $"{g.Key ?? "null"}={Show(g.Value, c.Floating)}"));
        string actual;
        try
        {
            var definition = c.Build<TView>(viewName, mode);
            if (mode == PortableMode.Persistent)
            {
                _views.Add(viewName);
                await new SqlViewManager(_connector!).EnsureAsync(definition);
            }
            var store = new SqlViewStore<TView>(_connector!, definition);
            var rows = (await store.QueryAsync(orderBy: ordered ? OrderBy<TView>.ByName("Value", descending: true) : null)).Cast<IKindView>().ToList();
            var shown = ordered ? rows : rows.OrderBy(r => r.Kind ?? "", StringComparer.Ordinal).ToList();
            actual = string.Join(", ", shown.Select(r => $"{r.Kind ?? "null"}={Show(r.Boxed, c.Floating)}"));
        }
        catch (Exception ex) when (Unwrap(ex) is NotSupportedException refusal && refusal.Message.Contains("KindView", StringComparison.Ordinal))
        {
            actual = RefusedByName;
            Output.WriteLine("refusal: " + refusal.Message);
        }
        catch (Exception ex)
        {
            actual = Describe(ex);
        }

        Output.WriteLine($"MEASURE | {Provider} | view{(ordered ? " order" : "")} | {c.Name} | {mode} | {actual} | expected {expected}");
        actual.Should().Be(expected);
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is System.Reflection.TargetInvocationException { InnerException: not null }) ex = ex.InnerException;
        return ex;
    }
}


public sealed class SqliteTask892AggregateTests : Task892AggregateTestsBase, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-t892-" + Guid.NewGuid().ToString("N"));
    public SqliteTask892AggregateTests(ITestOutputHelper output) : base(output) => Directory.CreateDirectory(_root);
    protected override string Provider => "SQLite";
    protected override bool ViewAveragesDecimalsExactly => true;
    protected override string? HostVariable => null;
    private SqLiteSettings? _settings;
    protected override PasswordSettings Settings() => _settings ??= new SqLiteSettings(_root, "t892.db");
    protected override IAsyncBulkStore<T892Row> AsyncStore(PasswordSettings settings) { var s = new AsyncSQLiteStore<T892Row>(); s.SetSettings(settings); return s; }
    protected override IBulkStore<T892Row> SyncStore(PasswordSettings settings) { var s = new SQLiteStore<T892Row>(); s.SetSettings(settings); return s; }

    public void Dispose()
    {
        if (_settings != null) Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()));
        try { Directory.Delete(_root, true); } catch { }
    }
}

public sealed class PostgreSqlTask892AggregateTests : Task892AggregateTestsBase
{
    public PostgreSqlTask892AggregateTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "PostgreSQL";
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override PasswordSettings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
    protected override IAsyncBulkStore<T892Row> AsyncStore(PasswordSettings settings) { var s = new AsyncPostgreSQLStore<T892Row>(); s.SetSettings(settings); return s; }
    protected override IBulkStore<T892Row> SyncStore(PasswordSettings settings) { var s = new PostgreSQLStore<T892Row>(); s.SetSettings(settings); return s; }
}

public sealed class MySqlTask892AggregateTests : Task892AggregateTestsBase
{
    public MySqlTask892AggregateTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "MySQL";
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override PasswordSettings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
    protected override IAsyncBulkStore<T892Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMySQLStore<T892Row>(); s.SetSettings(settings); return s; }
    protected override IBulkStore<T892Row> SyncStore(PasswordSettings settings) { var s = new MySQLStore<T892Row>(); s.SetSettings(settings); return s; }
}

public sealed class MSSqlTask892AggregateTests : Task892AggregateTestsBase
{
    public MSSqlTask892AggregateTests(ITestOutputHelper output) : base(output) { }
    protected override string Provider => "SQL Server";
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override PasswordSettings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
    protected override IAsyncBulkStore<T892Row> AsyncStore(PasswordSettings settings) { var s = new AsyncMSSqlStore<T892Row>(); s.SetSettings(settings); return s; }
    protected override IBulkStore<T892Row> SyncStore(PasswordSettings settings) { var s = new MSSqlStore<T892Row>(); s.SetSettings(settings); return s; }
}
