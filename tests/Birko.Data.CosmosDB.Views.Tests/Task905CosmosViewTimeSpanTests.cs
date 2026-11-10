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
/// TASK-905, live against the Cosmos emulator: a view filter over a <see cref="TimeSpan"/> view property answers what C#
/// answers over the view rows, on a projection read and its count; a sort by one is refused by name. The documents are
/// written as Birko's serializer writes them, <c>[-][d.]hh:mm:ss[.fffffff]</c>, whose text Cosmos DB compares.
/// </summary>
/// <remarks>Gated on <c>BIRKO_COSMOS_CONNECTION</c>; with <c>BIRKO_REQUIRE_LIVE</c> set, an absent server is a failure.</remarks>
public sealed class Task905CosmosViewTimeSpanTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<RowView> _oracle = new();

    public Task905CosmosViewTimeSpanTests(ITestOutputHelper output) => _output = output;

    public class Row
    {
        public string? Code { get; set; }
        public TimeSpan D { get; set; }
        public TimeSpan? DN { get; set; }
        public TimeSpan D2 { get; set; }
    }

    public class RowView
    {
        public string? Code { get; set; }
        public TimeSpan Span { get; set; }
        public TimeSpan? Maybe { get; set; }
        public TimeSpan D2 { get; set; }
    }

    private static TimeSpan S(double seconds) => TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));

    private static readonly (Row Row, string[] Missing)[] Seed =
    {
        (new() { Code = "c01", D = TimeSpan.Zero, DN = null, D2 = S(1) }, Array.Empty<string>()),
        (new() { Code = "c02", D = S(1.5), DN = TimeSpan.FromHours(23), D2 = S(1.25) }, Array.Empty<string>()),
        (new() { Code = "c03", D = TimeSpan.FromHours(23), D2 = new TimeSpan(1, 2, 3, 4) }, new[] { "DN" }),
        (new() { Code = "c04", D = new TimeSpan(1, 2, 3, 4), DN = S(-1), D2 = TimeSpan.FromHours(23) }, Array.Empty<string>()),
        (new() { Code = "c05", D = TimeSpan.FromDays(10), DN = TimeSpan.FromDays(2), D2 = TimeSpan.FromDays(2) }, Array.Empty<string>()),
        (new() { Code = "c06", D = TimeSpan.FromDays(2), DN = TimeSpan.FromDays(10), D2 = TimeSpan.FromDays(10) }, Array.Empty<string>()),
        (new() { Code = "c07", D = S(-1), DN = S(-2), D2 = S(-2) }, Array.Empty<string>()),
        (new() { Code = "c08", D = S(-2), DN = S(-1), D2 = S(-1) }, Array.Empty<string>()),
        (new() { Code = "c09", D = TimeSpan.FromDays(-1), DN = TimeSpan.Zero, D2 = S(-0.5) }, Array.Empty<string>()),
        (new() { Code = "c10", D = TimeSpan.MaxValue, DN = TimeSpan.MinValue, D2 = TimeSpan.Zero }, Array.Empty<string>()),
        (new() { Code = "c11", D = TimeSpan.MinValue, DN = TimeSpan.MaxValue, D2 = TimeSpan.MinValue }, Array.Empty<string>()),
        (new() { Code = "c12", DN = S(1.5) }, new[] { "D", "D2" }),
    };

    private static readonly (string Name, ExpressionType Op)[] Ops =
    {
        ("<", ExpressionType.LessThan), ("<=", ExpressionType.LessThanOrEqual), (">", ExpressionType.GreaterThan),
        (">=", ExpressionType.GreaterThanOrEqual), ("==", ExpressionType.Equal), ("!=", ExpressionType.NotEqual),
    };

    internal static (string Label, Expression<Func<RowView, bool>> Filter)[] Shapes()
    {
        var shapes = new List<(string, Expression<Func<RowView, bool>>)>();
        var values = new (string, TimeSpan)[]
        {
            ("0", TimeSpan.Zero), ("23h", TimeSpan.FromHours(23)), ("1d2h3m4s", new TimeSpan(1, 2, 3, 4)), ("2d", TimeSpan.FromDays(2)),
            ("-1s", S(-1)), ("-1.5s", S(-1.5)), ("min", TimeSpan.MinValue),
        };
        foreach (var (name, op) in Ops)
        {
            foreach (var (label, value) in values)
            {
                var v = Expression.Parameter(typeof(RowView), "v");
                shapes.Add(($"Span {name} {label}", Expression.Lambda<Func<RowView, bool>>(
                    Expression.MakeBinary(op, Expression.Property(v, "Span"), Expression.Constant(value)), v)));
                shapes.Add(($"Maybe {name} {label}", Expression.Lambda<Func<RowView, bool>>(
                    Expression.MakeBinary(op, Expression.Property(v, "Maybe"), Expression.Constant(value, typeof(TimeSpan?))), v)));
            }
            var w = Expression.Parameter(typeof(RowView), "v");
            shapes.Add(($"Span {name} D2", Expression.Lambda<Func<RowView, bool>>(
                Expression.MakeBinary(op, Expression.Property(w, "Span"), Expression.Property(w, "D2")), w)));
        }

        var h23 = TimeSpan.FromHours(23);
        var m1 = S(-1);
        var zero = TimeSpan.Zero;
        var list = new[] { h23, m1 };
        shapes.AddRange(new (string, Expression<Func<RowView, bool>>)[]
        {
            ("!(Span > h23)", v => !(v.Span > h23)),
            ("!(Maybe < m1)", v => !(v.Maybe < m1)),
            ("[h23, -1s].Contains(Span)", v => list.Contains(v.Span)),
            ("(Maybe ?? zero) > m1", v => (v.Maybe ?? zero) > m1),
            ("Span.Equals(h23)", v => v.Span.Equals(h23)),
            ("TimeSpan.Compare(Span, m1) >= 0", v => TimeSpan.Compare(v.Span, m1) >= 0),
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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task905v_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        foreach (var (row, missing) in Seed)
        {
            var doc = (JsonObject)JsonSerializer.SerializeToNode(row)!;
            foreach (var name in missing) doc.Remove(name);
            doc["id"] = Guid.NewGuid().ToString();
            await _rows.CreateItemAsync(doc, new PartitionKey(doc["id"]!.GetValue<string>()));
        }
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
        .Select<Row, TimeSpan>(r => r.D, v => v.Span)
        .Select<Row, TimeSpan?>(r => r.DN, v => v.Maybe)
        .Select<Row, TimeSpan>(r => r.D2, v => v.D2)
        .Build();

    private static string Codes(IEnumerable<RowView> rows) => string.Join(",", rows.Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal));

    [Fact]
    public async Task A_view_filter_on_a_TimeSpan_answers_as_CSharp()
    {
        if (_rows == null) return;
        _oracle.Should().HaveCount(Seed.Length);
        var store = new CosmosViewStore<RowView>(_rows, View());
        var failures = new List<string>();
        var wrong = new HashSet<string>();
        foreach (var (label, filter) in Shapes())
        {
            var expected = _oracle.Where(filter.Compile()).ToList();
            try
            {
                var read = Codes(await store.QueryAsync(filter));
                if (read != Codes(expected)) { failures.Add($"{label} [read]: C# {{{Codes(expected)}}} got {{{read}}}"); wrong.Add(label); }
                var count = await store.CountAsync(filter);
                if (count != expected.Count) { failures.Add($"{label} [count]: C# {expected.Count} got {count}"); wrong.Add(label); }
            }
            catch (Exception e)
            {
                failures.Add($"{label}: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
                wrong.Add(label);
            }
        }
        foreach (var f in failures) _output.WriteLine(f);
        _output.WriteLine($"{failures.Count} failures, {wrong.Count} of {Shapes().Length} shapes wrong");
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact]
    public async Task A_view_sort_or_a_computed_filter_on_a_TimeSpan_is_refused_by_name()
    {
        var member = () => CosmosFilterTranslator.Translate<RowView>(v => v.Span.TotalHours > 1, new List<KeyValuePair<string, object?>>());
        member.Should().Throw<NotSupportedException>().WithMessage("*TimeSpan.TotalHours*(TASK-905)*");
        var h = TimeSpan.FromHours(1);
        var computed = () => CosmosFilterTranslator.Translate<RowView>(v => v.Span + h > h, new List<KeyValuePair<string, object?>>());
        computed.Should().Throw<NotSupportedException>().WithMessage("*TimeSpan*(TASK-905)*");
        if (_rows == null) return;
        var store = new CosmosViewStore<RowView>(_rows, View());
        var sort = async () => await store.QueryAsync(null, OrderBy<RowView>.By(v => v.Span), 2, null);
        await sort.Should().ThrowAsync<NotSupportedException>().WithMessage("*TimeSpan*(TASK-905)*");
        var unpaged = async () => await store.QueryAsync(null, OrderBy<RowView>.ByDescending(v => v.Maybe));
        await unpaged.Should().ThrowAsync<NotSupportedException>().WithMessage("*TimeSpan*(TASK-905)*");
    }

    [Fact]
    public void A_TimeSpan_ordering_renders_the_sign_and_the_magnitude_key_on_the_property()
    {
        var h23 = TimeSpan.FromHours(23);
        var parameters = new List<KeyValuePair<string, object?>>();
        var sql = CosmosFilterTranslator.Translate<RowView>(v => v.Span > h23, parameters, name => name == "Span" ? "D" : name);
        sql.Should().StartWith("(c[\"D\"] >= @p0 AND CONCAT(CONCAT(SUBSTRING(\"0000000000\", INDEX_OF(REPLACE(REPLACE(c[\"D\"], ");
        parameters[0].Value.Should().Be("0");
        parameters.Last().Value.Should().Be("0000000023:00:000000000");
    }
}
