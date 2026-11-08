using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Views;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-867: a Cosmos DB view filter with an ordering against null returns what C# matches, on the live emulator. In C#
/// <c>v.Score &gt; nul</c> with a null <c>nul</c> is false; Cosmos DB's <c>c.Score &gt; null</c> is undefined, and so is its
/// NOT, so before the fold <c>!(v.Score &gt; nul) &amp;&amp; v.A &gt; 4</c> missed rows (measured). The translator now folds the
/// ordering to <c>false</c> before rendering. Gated on <c>BIRKO_COSMOS_CONNECTION</c>; the rendering tests need no server.
/// </summary>
public sealed class Task867CosmosViewNullOrderingTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<ScoreView> _oracle = new();

    public Task867CosmosViewNullOrderingTests(ITestOutputHelper output) => _output = output;

    public class Row
    {
        public string id { get; set; } = "";
        public string? Code { get; set; }
        public int A { get; set; }
        public int? Score { get; set; }
    }

    public class ScoreView
    {
        public string? Code { get; set; }
        public int A { get; set; }
        public int? Points { get; set; }
    }

    private static readonly int? Nul = null;

    public static readonly IReadOnlyDictionary<string, Expression<Func<ScoreView, bool>>> Shapes =
        new Dictionary<string, Expression<Func<ScoreView, bool>>>
        {
            ["Points > nul"] = v => v.Points > Nul,
            ["!(Points > nul)"] = v => !(v.Points > Nul),
            ["nul <= Points"] = v => Nul <= v.Points,
            ["Points > nul || A > 4"] = v => v.Points > Nul || v.A > 4,
            ["!(Points > nul || A > 3)"] = v => !(v.Points > Nul || v.A > 3),
            ["!(Points > nul) && A > 4"] = v => !(v.Points > Nul) && v.A > 4,
            ["!(Points < nul && A > 1)"] = v => !(v.Points < Nul && v.A > 1),
            ["Points != nul"] = v => v.Points != Nul,
            ["Points == nul"] = v => v.Points == Nul,
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
        _client = new CosmosClient(conn, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task867_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        int?[] scores = { null, 5, null, 8, null, 2 };
        for (var i = 0; i < scores.Length; i++)
        {
            var row = new Row { id = Guid.NewGuid().ToString(), Code = "r" + (i + 1), A = i + 1, Score = scores[i] };
            await _rows.CreateItemAsync(row, new PartitionKey(row.id));
            _oracle.Add(new ScoreView { Code = row.Code, A = row.A, Points = row.Score });
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private static ViewDefinition View() => new ViewDefinitionBuilder<ScoreView>()
        .From<Row>()
        .Select<Row, string?>(r => r.Code, v => v.Code)
        .Select<Row, int>(r => r.A, v => v.A)
        .Select<Row, int?>(r => r.Score, v => v.Points)
        .Build();

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_view_filter_with_a_null_ordering_returns_what_CSharp_matches(string shape)
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
    public void The_ordering_is_folded_before_rendering()
    {
        var p = new List<KeyValuePair<string, object?>>();
        CosmosFilterTranslator.Translate<ScoreView>(v => !(v.Points > Nul) && v.A > 4, p).Should().Be("c[\"A\"] > @p0");
        CosmosFilterTranslator.Translate<ScoreView>(v => v.Points > Nul, p).Should().Be("false");
        CosmosFilterTranslator.Translate<ScoreView>(v => !(v.Points > Nul), p).Should().BeEmpty("a filter that folds to true constrains nothing");
    }
}
