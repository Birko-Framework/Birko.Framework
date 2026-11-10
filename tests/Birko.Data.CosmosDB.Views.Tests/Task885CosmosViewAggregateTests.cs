using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Aggregation;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-885, live against the Cosmos emulator: a grouped view's SUM, AVG and MIN over a nullable member skip the nulls,
/// as C# does. Before, a group holding a null answered no SUM and no AVG and a null MIN (measured: g1 sum/min/avg empty
/// where C# gives 10), because Cosmos DB's aggregates treat a null as a value (<see cref="CosmosAggregationHelper.BuildFunctionSql"/>).
/// The oracle is C# over the same rows: <c>g.Sum(r =&gt; r.Score)</c> (0 over no non-null value, as the view answers),
/// <c>Min</c>, <c>Max</c>, <c>Average</c> (null over none).
/// </summary>
public sealed class Task885CosmosViewAggregateTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<Row> _source = new()
    {
        new() { Grp = "g1", A = 1, Score = 10 },
        new() { Grp = "g1", A = 2, Score = null },
        new() { Grp = "g2", A = 3, Score = 30 },
        new() { Grp = "g2", A = 4, Score = 40 },
        new() { Grp = "g3", A = 5, Score = null },
        new() { Grp = "g3", A = 6, Score = 6 },
        new() { Grp = "g4", A = 7, Score = null },
    };

    public Task885CosmosViewAggregateTests(ITestOutputHelper output) => _output = output;

    public class Row { public string? id { get; set; } public string? Grp { get; set; } public int A { get; set; } public int? Score { get; set; } }

    public class Totals
    {
        public string? Grp { get; set; }
        public long Cnt { get; set; }
        public int? SumScore { get; set; }
        public int? MinScore { get; set; }
        public int? MaxScore { get; set; }
        public double? AvgScore { get; set; }
    }

    public class WholeTotals
    {
        public long Cnt { get; set; }
        public int? SumScore { get; set; }
        public int? MinScore { get; set; }
        public int? MaxScore { get; set; }
        public double? AvgScore { get; set; }
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
        _client = new CosmosClient(conn, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task885v_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        foreach (var row in _source)
        {
            row.id = Guid.NewGuid().ToString();
            await _rows.CreateItemAsync(row, new PartitionKey(row.id));
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    [Fact]
    public async Task A_grouped_view_skips_nulls_like_CSharp()
    {
        if (_rows == null) return;
        var store = new CosmosViewStore<Totals>(_rows, GroupedWithAvg());

        foreach (var filter in new Expression<Func<Totals, bool>>?[] { null, v => v.Grp != "g2" })
        {
            var rows = _source.Where(r => filter == null || r.Grp != "g2");
            var expected = rows.GroupBy(r => r.Grp).Select(g => new Totals
            {
                Grp = g.Key,
                Cnt = g.Count(),
                SumScore = g.Sum(r => r.Score),
                MinScore = g.Min(r => r.Score),
                MaxScore = g.Max(r => r.Score),
                AvgScore = g.Average(r => r.Score),
            }).ToList();

            var actual = (await store.QueryAsync(filter)).ToList();
            _output.WriteLine(string.Join("; ", actual.Select(t => $"{t.Grp}: n={t.Cnt} sum={t.SumScore} min={t.MinScore} max={t.MaxScore} avg={t.AvgScore}")));
            actual.Should().BeEquivalentTo(expected);
        }
    }

    [Fact]
    public async Task An_ungrouped_aggregate_view_skips_nulls_like_CSharp()
    {
        if (_rows == null) return;
        var view = new ViewDefinitionBuilder<WholeTotals>()
            .From<Row>()
            .Count<Row>(v => v.Cnt)
            .Sum<Row, int?>(r => r.Score, v => v.SumScore)
            .Min<Row, int?>(r => r.Score, v => v.MinScore)
            .Max<Row, int?>(r => r.Score, v => v.MaxScore)
            .Avg<Row, double?>(r => r.Score, v => v.AvgScore)
            .Build();
        var actual = (await new CosmosViewStore<WholeTotals>(_rows, view).QueryAsync()).Should().ContainSingle().Subject;

        actual.Should().BeEquivalentTo(new WholeTotals
        {
            Cnt = 7,
            SumScore = _source.Sum(r => r.Score),
            MinScore = _source.Min(r => r.Score),
            MaxScore = _source.Max(r => r.Score),
            AvgScore = _source.Average(r => r.Score),
        });
    }

    private static ViewDefinition GroupedWithAvg() => new ViewDefinitionBuilder<Totals>()
        .From<Row>()
        .GroupBy<Row, string?>(r => r.Grp)
        .Select<Row, string?>(r => r.Grp, v => v.Grp)
        .Count<Row>(v => v.Cnt)
        .Sum<Row, int?>(r => r.Score, v => v.SumScore)
        .Min<Row, int?>(r => r.Score, v => v.MinScore)
        .Max<Row, int?>(r => r.Score, v => v.MaxScore)
        .Avg<Row, double?>(r => r.Score, v => v.AvgScore)
        .Build();
}
