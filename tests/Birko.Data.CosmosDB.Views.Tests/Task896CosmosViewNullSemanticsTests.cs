using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Serialization;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-896, live against the Cosmos emulator: a view filter over nullable view properties returns what the compiled C#
/// expression returns over the view rows, on a projection read, its count and a grouped view's WHERE, with documents that
/// hold a member as null and documents that omit it. The view translator takes the store's pre-pass
/// (<c>CosmosFilterRewrite.Prepare</c>); before it, <c>!(v.Points &gt; 20)</c> missed the null rows here too.
/// </summary>
/// <remarks>Gated on <c>BIRKO_COSMOS_CONNECTION</c>; with <c>BIRKO_REQUIRE_LIVE</c> set, an absent server is a failure. The
/// rendering tests need no server.</remarks>
public sealed class Task896CosmosViewNullSemanticsTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<ScoreView> _oracle = new();

    public Task896CosmosViewNullSemanticsTests(ITestOutputHelper output) => _output = output;

    public enum Status { New, Active, Closed }

    public class ScoreView
    {
        public string? Code { get; set; }
        public int A { get; set; }
        public int? Points { get; set; }
        public string? S { get; set; }
        public bool? Flag { get; set; }
        public Status? St { get; set; }
    }

    public class ByS
    {
        public string? S { get; set; }
        public long Cnt { get; set; }
    }

    // Code, A, Score (viewed as Points), S, Flag, St; null = stored null; Missing = not in the document at all.
    private static readonly object Missing = new();

    private static readonly object?[][] Seed =
    {
        new object?[] { "c1", 1, 10, "a", true, Status.New },
        new object?[] { "c2", 2, null, null, null, null },
        new object?[] { "c3", 3, 20, "b", false, Status.Active },
        new object?[] { "c4", 4, null, "", null, Status.Closed },
        new object?[] { "c5", 5, 30, "B", true, null },
        new object?[] { "c6", 6, Missing, Missing, Missing, Missing },
        new object?[] { "c7", 7, 25, "a", Missing, Status.Active },
        new object?[] { "c8", 8, Missing, "c", false, Status.New },
    };

    private static readonly int? Nul = null;

    /// <summary>Shapes the view must answer as C# does.</summary>
    public static readonly IReadOnlyDictionary<string, Expression<Func<ScoreView, bool>>> Shapes =
        new Dictionary<string, Expression<Func<ScoreView, bool>>>
        {
            ["Points.HasValue"] = v => v.Points.HasValue,
            ["!Points.HasValue"] = v => !v.Points.HasValue,
            ["Points.HasValue == false"] = v => v.Points.HasValue == false,
            ["Points == null"] = v => v.Points == null,
            ["Points != null"] = v => v.Points != null,
            ["Points == nul"] = v => v.Points == Nul,
            ["!(Points > 20)"] = v => !(v.Points > 20),
            ["!(20 < Points)"] = v => !(20 < v.Points),
            ["!(Points >= 20) && A > 2"] = v => !(v.Points >= 20) && v.A > 2,
            ["!(Points > 20 || A > 6)"] = v => !(v.Points > 20 || v.A > 6),
            ["A > 6 || !(Points <= 10)"] = v => v.A > 6 || !(v.Points <= 10),
            ["Points != 10"] = v => v.Points != 10,
            ["!(Points == 10)"] = v => !(v.Points == 10),
            ["S == null"] = v => v.S == null,
            ["!(S == \"a\")"] = v => !(v.S == "a"),
            ["S != \"a\""] = v => v.S != "a",
            ["string.IsNullOrEmpty(S)"] = v => string.IsNullOrEmpty(v.S),
            ["!string.IsNullOrEmpty(S)"] = v => !string.IsNullOrEmpty(v.S),
            ["string.CompareOrdinal(S, \"b\") < 0"] = v => string.CompareOrdinal(v.S, "b") < 0,
            ["!(string.CompareOrdinal(S, \"a\") > 0)"] = v => !(string.CompareOrdinal(v.S, "a") > 0),
            ["Flag == true"] = v => v.Flag == true,
            ["!(Flag == true)"] = v => !(v.Flag == true),
            ["Flag != false"] = v => v.Flag != false,
            ["St != Active"] = v => v.St != Status.Active,
            ["!(St > New)"] = v => !(v.St > Status.New),
            ["!new[] { 10, 20 }.Contains(Points)"] = v => !new int?[] { 10, 20 }.Contains(v.Points),
            ["new[] { 10, null }.Contains(Points)"] = v => new int?[] { 10, null }.Contains(v.Points),
            ["!new[] { \"a\" }.Contains(S)"] = v => !new[] { "a" }.Contains(v.S),
        };

    public static IEnumerable<object[]> Cases() => Shapes.Keys.Select(k => new object[] { k });

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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task896v_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        var names = new[] { "Code", "A", "Score", "S", "Flag", "St" };
        foreach (var values in Seed)
        {
            var doc = new JsonObject { ["id"] = Guid.NewGuid().ToString() };
            for (var i = 0; i < names.Length; i++)
            {
                if (ReferenceEquals(values[i], Missing)) continue;
                doc[names[i]] = values[i] is Status s ? JsonValue.Create((int)s) : JsonSerializer.SerializeToNode(values[i]);
            }
            await _rows.CreateItemAsync(doc, new PartitionKey(doc["id"]!.GetValue<string>()));
            _oracle.Add(new ScoreView
            {
                Code = (string)values[0]!,
                A = (int)values[1]!,
                Points = values[2] as int?,
                S = values[3] as string,
                Flag = values[4] as bool?,
                St = values[5] as Status?,
            });
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    public class Row
    {
        public string? Code { get; set; }
        public int A { get; set; }
        public int? Score { get; set; }
        public string? S { get; set; }
        public bool? Flag { get; set; }
        public Status? St { get; set; }
    }

    private static ViewDefinition View() => new ViewDefinitionBuilder<ScoreView>()
        .From<Row>()
        .Select<Row, string?>(r => r.Code, v => v.Code)
        .Select<Row, int>(r => r.A, v => v.A)
        .Select<Row, int?>(r => r.Score, v => v.Points)
        .Select<Row, string?>(r => r.S, v => v.S)
        .Select<Row, bool?>(r => r.Flag, v => v.Flag)
        .Select<Row, Status?>(r => r.St, v => v.St)
        .Build();

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_view_filter_over_null_and_missing_members_returns_what_CSharp_matches(string shape)
    {
        if (_rows == null) return;
        var filter = Shapes[shape];
        var expected = _oracle.Where(filter.Compile()).Select(v => v.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var store = new CosmosViewStore<ScoreView>(_rows, View());

        var read = (await store.QueryAsync(filter)).Select(v => v.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();
        read.Should().Equal(expected, shape);
        (await store.CountAsync(filter)).Should().Be(expected.Count, shape);
    }

    [Fact]
    public async Task A_grouped_view_filters_its_source_rows_as_CSharp_does()
    {
        if (_rows == null) return;
        var view = new ViewDefinitionBuilder<ByS>()
            .From<Row>()
            .GroupBy<Row, string?>(r => r.S)
            .Select<Row, string?>(r => r.S, v => v.S)
            .Count<Row>(v => v.Cnt)
            .Build();
        var store = new CosmosViewStore<ByS>(_rows, view);
        var cases = new (string Label, Expression<Func<ByS, bool>> Filter, Func<ScoreView, bool> Oracle)[]
        {
            ("!(S == \"a\")", v => !(v.S == "a"), r => !(r.S == "a")),
            ("S == null", v => v.S == null, r => r.S == null),
            ("string.IsNullOrEmpty(S)", v => string.IsNullOrEmpty(v.S), r => string.IsNullOrEmpty(r.S)),
            ("!(string.CompareOrdinal(S, \"a\") > 0)", v => !(string.CompareOrdinal(v.S, "a") > 0), r => !(string.CompareOrdinal(r.S, "a") > 0)),
        };
        foreach (var (label, filter, oracle) in cases)
        {
            var groups = (await store.QueryAsync(filter)).ToList();
            groups.Sum(g => g.Cnt).Should().Be(_oracle.Count(oracle), label);
        }
    }

    // ----- rendering, no server -----

    private static string Render(Expression<Func<ScoreView, bool>> filter)
        => CosmosFilterTranslator.Translate(filter, new List<KeyValuePair<string, object?>>(), name => name == "Points" ? "Score" : name);

    [Fact]
    public void HasValue_renders_as_defined_and_not_null_on_the_source_field()
    {
        Render(v => v.Points.HasValue).Should().Be("(IS_DEFINED(c[\"Score\"]) AND NOT (IS_NULL(c[\"Score\"])))");
        Render(v => v.Points == null).Should().Be("(NOT (IS_DEFINED(c[\"Score\"])) OR IS_NULL(c[\"Score\"]))");
    }

    [Fact]
    public void A_negated_ordering_is_guarded()
        => Render(v => !(v.Points > 20))
            .Should().Be("NOT (((IS_DEFINED(c[\"Score\"]) AND NOT (IS_NULL(c[\"Score\"]))) AND c[\"Score\"] > @p0))");

    [Fact]
    public void A_culture_comparison_is_refused_by_name()
    {
        var act = () => Render(v => string.Compare(v.S, "b") > 0);
        act.Should().Throw<NotSupportedException>().WithMessage("*(TASK-896)*");
    }
}
