using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Serialization;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-902, live against the Cosmos emulator: a view filter or sort over a <see cref="DateTime"/> view property answers what
/// C# answers over the view rows (C# compares the ticks, whatever the kind), on a projection read, its count and a sorted,
/// paged read. The documents are written as Birko's serializer writes them: as many fraction digits as needed, then
/// <c>Z</c>, the writer's offset, or nothing.
/// </summary>
/// <remarks>Gated on <c>BIRKO_COSMOS_CONNECTION</c>; with <c>BIRKO_REQUIRE_LIVE</c> set, an absent server is a failure. The
/// rendering tests need no server.</remarks>
public sealed class Task902CosmosViewDateTimeTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<RowView> _oracle = new();

    public Task902CosmosViewDateTimeTests(ITestOutputHelper output) => _output = output;

    public class Row
    {
        public string? Code { get; set; }
        public DateTime T { get; set; }
        public DateTime? TN { get; set; }
        public DateTime T2 { get; set; }
        public DateTimeOffset O { get; set; }
    }

    public class RowView
    {
        public string? Code { get; set; }
        public DateTime When { get; set; }
        public DateTime? Maybe { get; set; }
        public DateTime T2 { get; set; }
        public DateTimeOffset O { get; set; }
    }

    private static DateTime U(int s, long ticks = 0) => new DateTime(2026, 1, 1, 10, 0, s, DateTimeKind.Utc).AddTicks(ticks);
    private static DateTime L(int s, long ticks = 0) => new DateTime(2026, 1, 1, 10, 0, s, DateTimeKind.Local).AddTicks(ticks);
    private static DateTime N(int s, long ticks = 0) => new DateTime(2026, 1, 1, 10, 0, s, DateTimeKind.Unspecified).AddTicks(ticks);

    private static readonly (Row Row, string[] Missing)[] Seed =
    {
        (new() { Code = "c1", T = U(0), TN = null, T2 = L(0, 5_000_000) }, Array.Empty<string>()),
        (new() { Code = "c2", T = U(0, 5_000_000), TN = L(0), T2 = U(0) }, Array.Empty<string>()),
        (new() { Code = "c3", T = L(0), TN = N(0, 5_000_000), T2 = N(0) }, Array.Empty<string>()),
        (new() { Code = "c4", T = N(0), T2 = U(0, 1) }, new[] { "TN" }),
        (new() { Code = "c5", T = N(0, 1_234_567), TN = U(0, 1), T2 = L(0, 1_234_567) }, Array.Empty<string>()),
        (new() { Code = "c6", T = U(0, 1), TN = U(0), T2 = U(1) }, Array.Empty<string>()),
        (new() { Code = "c7", T = L(0).AddTicks(-1), TN = L(0).AddTicks(-1), T2 = N(0, 5_000_000) }, Array.Empty<string>()),
        (new() { Code = "c8", T = U(1), TN = new DateTime(2026, 1, 2), T2 = U(0) }, Array.Empty<string>()),
        (new() { Code = "c9", TN = U(0, 5_000_000) }, new[] { "T", "T2" }),
    };

    private static readonly (string Name, ExpressionType Op)[] Ops =
    {
        ("<", ExpressionType.LessThan), ("<=", ExpressionType.LessThanOrEqual), (">", ExpressionType.GreaterThan),
        (">=", ExpressionType.GreaterThanOrEqual), ("==", ExpressionType.Equal), ("!=", ExpressionType.NotEqual),
    };

    internal static (string Label, Expression<Func<RowView, bool>> Filter)[] Shapes()
    {
        var shapes = new List<(string, Expression<Func<RowView, bool>>)>();
        var values = new (string, DateTime)[] { ("u", U(0)), ("uh", U(0, 5_000_000)), ("l", L(0)), ("n", N(0)), ("t1", U(0, 1)) };
        foreach (var (name, op) in Ops)
        {
            foreach (var (label, value) in values)
            {
                var v = Expression.Parameter(typeof(RowView), "v");
                shapes.Add(($"When {name} {label}", Expression.Lambda<Func<RowView, bool>>(
                    Expression.MakeBinary(op, Expression.Property(v, "When"), Expression.Constant(value)), v)));
                shapes.Add(($"Maybe {name} {label}", Expression.Lambda<Func<RowView, bool>>(
                    Expression.MakeBinary(op, Expression.Property(v, "Maybe"), Expression.Constant(value, typeof(DateTime?))), v)));
            }
        }

        var u = U(0);
        var uh = U(0, 5_000_000);
        var n = N(0);
        var day = new DateTime(2026, 1, 1);
        var list = new[] { u, uh };
        var nullable = new DateTime?[] { null, uh };
        shapes.AddRange(new (string, Expression<Func<RowView, bool>>)[]
        {
            ("!(When > u)", v => !(v.When > u)),
            ("!(Maybe < uh)", v => !(v.Maybe < uh)),
            ("When.Date == day", v => v.When.Date == day),
            ("When.Date > day", v => v.When.Date > day),
            ("[u, uh].Contains(When)", v => list.Contains(v.When)),
            ("![u, uh].Contains(When)", v => !list.Contains(v.When)),
            ("[null, uh].Contains(Maybe)", v => nullable.Contains(v.Maybe)),
            ("(Maybe ?? n) > u", v => (v.Maybe ?? n) > u),
            ("When > T2", v => v.When > v.T2),
            ("When == T2", v => v.When == v.T2),
            ("When.Equals(u)", v => v.When.Equals(u)),
            ("DateTime.Compare(When, uh) >= 0", v => DateTime.Compare(v.When, uh) >= 0),
        });
        return shapes.ToArray();
    }

    public async Task InitializeAsync()
    {
        var conn = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
        {
            _output.WriteLine("SKIPPED: no live Cosmos. Set BIRKO_COSMOS_CONNECTION.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_COSMOS_CONNECTION is not.");
            return;
        }
        _client = new CosmosClient(conn, new CosmosClientOptions
        {
            ConnectionMode = ConnectionMode.Gateway,
            Serializer = new CosmosGuidIdSerializer(),
        });
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task902v_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        foreach (var (row, missing) in Seed)
        {
            var doc = (JsonObject)JsonSerializer.SerializeToNode(row)!;
            foreach (var name in missing) doc.Remove(name);
            doc["id"] = Guid.NewGuid().ToString();
            await _rows.CreateItemAsync(doc, new PartitionKey(doc["id"]!.GetValue<string>()));
        }
        // The oracle is the view as read back, so a value stored with an offset is the reader's local time, as C# reads it.
        _oracle.AddRange(await new CosmosViewStore<RowView>(_rows, View()).QueryAsync());
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private static ViewDefinition View() => new ViewDefinitionBuilder<RowView>()
        .From<Row>()
        .Select<Row, string?>(r => r.Code, v => v.Code)
        .Select<Row, DateTime>(r => r.T, v => v.When)
        .Select<Row, DateTime?>(r => r.TN, v => v.Maybe)
        .Select<Row, DateTime>(r => r.T2, v => v.T2)
        .Select<Row, DateTimeOffset>(r => r.O, v => v.O)
        .Build();

    private static string Codes(IEnumerable<RowView> rows) => string.Join(",", rows.Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal));

    [Fact]
    public async Task A_view_filter_on_a_DateTime_answers_as_CSharp()
    {
        if (_rows == null) return;
        _oracle.Should().HaveCount(Seed.Length);
        var store = new CosmosViewStore<RowView>(_rows, View());
        var failures = new List<string>();
        foreach (var (label, filter) in Shapes())
        {
            var expected = _oracle.Where(filter.Compile()).ToList();
            try
            {
                var read = Codes(await store.QueryAsync(filter));
                if (read != Codes(expected)) failures.Add($"{label} [read]: C# {{{Codes(expected)}}} got {{{read}}}");
                var count = await store.CountAsync(filter);
                if (count != expected.Count) failures.Add($"{label} [count]: C# {expected.Count} got {count}");
            }
            catch (Exception e)
            {
                failures.Add($"{label}: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
            }
        }
        foreach (var f in failures) _output.WriteLine(f);
        _output.WriteLine($"{failures.Count} failures over {Shapes().Length} shapes");
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task A_view_sort_by_a_DateTime_orders_as_CSharp_with_and_without_paging()
    {
        if (_rows == null) return;
        var store = new CosmosViewStore<RowView>(_rows, View());
        var sorts = new (string, OrderBy<RowView>, Func<IEnumerable<RowView>, IEnumerable<RowView>>, Func<RowView, DateTime?>)[]
        {
            ("When asc", OrderBy<RowView>.By(v => v.When), r => r.OrderBy(v => v.When), v => v.When),
            ("When desc", OrderBy<RowView>.ByDescending(v => v.When), r => r.OrderByDescending(v => v.When), v => v.When),
            ("Maybe asc", OrderBy<RowView>.By(v => v.Maybe), r => r.OrderBy(v => v.Maybe), v => v.Maybe),
            ("Maybe desc", OrderBy<RowView>.ByDescending(v => v.Maybe), r => r.OrderByDescending(v => v.Maybe), v => v.Maybe),
        };
        var pages = new (int?, int?)[] { (null, null), (3, null), (3, 1), (2, 2), (4, 3), (1, 5), (5, 6), (null, 2) };
        var failures = new List<string>();
        foreach (var (label, order, oracle, key) in sorts)
        foreach (var (limit, offset) in pages)
        {
            IEnumerable<RowView> expected = oracle(_oracle);
            if (offset.HasValue) expected = expected.Skip(offset.Value);
            if (limit.HasValue) expected = expected.Take(limit.Value);
            static string Ticks(IEnumerable<RowView> rows, Func<RowView, DateTime?> key)
                => string.Join(",", rows.Select(r => key(r)?.Ticks.ToString() ?? "null"));
            var name = $"{label} limit {limit?.ToString() ?? "-"} offset {offset?.ToString() ?? "-"}";
            try
            {
                var got = Ticks(await store.QueryAsync(null, order, limit, offset), key);
                if (got != Ticks(expected, key)) failures.Add($"{name}: C# {Ticks(expected, key)} got {got}");
            }
            catch (Exception e)
            {
                failures.Add($"{name}: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
            }
        }
        foreach (var f in failures) _output.WriteLine(f);
        _output.WriteLine($"{failures.Count} failures");
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task A_view_sort_or_filter_on_a_DateTimeOffset_is_refused_by_name()
    {
        var o = DateTimeOffset.UnixEpoch;
        var refusal = () => CosmosFilterTranslator.Translate<RowView>(v => v.O > o, new List<KeyValuePair<string, object?>>());
        refusal.Should().Throw<NotSupportedException>().WithMessage("*DateTimeOffset*(TASK-902)*");
        var member = () => CosmosFilterTranslator.Translate<RowView>(v => v.When.Year == 2026, new List<KeyValuePair<string, object?>>());
        member.Should().Throw<NotSupportedException>().WithMessage("*DateTime.Year*(TASK-902)*");
        if (_rows == null) return;
        var store = new CosmosViewStore<RowView>(_rows, View());
        var sort = async () => await store.QueryAsync(null, OrderBy<RowView>.By(v => v.O), 2, null);
        await sort.Should().ThrowAsync<NotSupportedException>().WithMessage("*DateTimeOffset*(TASK-902)*");
    }

    // ----- rendering, no server -----

    [Fact]
    public void A_DateTime_comparison_renders_the_wall_clock_key_behind_a_text_range_on_the_property()
    {
        var u = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var parameters = new List<KeyValuePair<string, object?>>();
        var sql = CosmosFilterTranslator.Translate<RowView>(v => v.When > u, parameters, name => name == "When" ? "T" : name);
        sql.Should().StartWith("(c[\"T\"] >= @p0 AND CONCAT(SUBSTRING(c[\"T\"], ");
        parameters[0].Value.Should().Be("2026-01-01T10:00:00");
        parameters.Last().Value.Should().Be("2026-01-01T10:00:000000000");
    }
}
