using System;
using System.Threading.Tasks;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-886, live against the Cosmos emulator: an UNGROUPED aggregate view over no rows answers one row (Count 0, Sum 0,
/// Min / Avg null), as every provider does (the user's decision, 2026-11-10). The SUM over only NULLs is pinned by
/// <c>Task885CosmosViewAggregateTests</c>.
/// </summary>
public sealed class Task886CosmosViewAggregateEdgeTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;

    public Task886CosmosViewAggregateEdgeTests(ITestOutputHelper output) => _output = output;

    public class Row { public string? id { get; set; } public string? Grp { get; set; } public int? Score { get; set; } }

    public class WholeTotals
    {
        public long Cnt { get; set; }
        public int? SumScore { get; set; }
        public int? MinScore { get; set; }
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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task886v_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    [Fact]
    public async Task An_ungrouped_view_over_no_rows_answers_one_row()
    {
        if (_rows == null) return;
        var view = new ViewDefinitionBuilder<WholeTotals>()
            .From<Row>()
            .Count<Row>(v => v.Cnt)
            .Sum<Row, int?>(r => r.Score, v => v.SumScore)
            .Min<Row, int?>(r => r.Score, v => v.MinScore)
            .Avg<Row, double?>(r => r.Score, v => v.AvgScore)
            .Build();
        var store = new CosmosViewStore<WholeTotals>(_rows, view);

        (await store.QueryAsync()).Should().ContainSingle().Which.Should().BeEquivalentTo(new WholeTotals { Cnt = 0, SumScore = 0 });
        (await store.CountAsync()).Should().Be(1);
    }
}
