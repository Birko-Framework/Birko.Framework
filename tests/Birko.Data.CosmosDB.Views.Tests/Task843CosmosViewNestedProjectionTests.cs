using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-843: a view that projects a nested member (<c>r.Sub.X</c>) was recorded as source field <c>X</c> by the
/// shared builder, so Cosmos DB selected <c>c["X"]</c>, the document's top-level field (measured on the emulator,
/// vnext-preview, 2026-11-07: SubX, SubName and the two-level Level read the top-level X, Name and Level). The
/// projection now selects the whole path, <c>c["Sub"]["X"]</c>, and a filter or sort on that view property reads the
/// same path. Gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class Task843CosmosViewNestedProjectionTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<FlatView> _oracle = new();

    public Task843CosmosViewNestedProjectionTests(ITestOutputHelper output) => _output = output;

    public class Deeper { public int Level { get; set; } }

    public class Sub
    {
        public int X { get; set; }
        public string? Name { get; set; }
        public Deeper? Deep { get; set; }
    }

    /// <summary>A top-level <c>X</c> beside the nested <c>Sub.X</c>, with different values, so a flattened read shows.</summary>
    public class Row
    {
        public string id { get; set; } = "";
        public Guid Key { get; set; }
        public int X { get; set; }
        public string? Name { get; set; }
        public int Level { get; set; }
        public Sub Sub { get; set; } = new();
    }

    public class FlatView
    {
        public Guid Key { get; set; }
        public int TopX { get; set; }
        public int SubX { get; set; }
        public string? SubName { get; set; }
        public int? Level { get; set; }
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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task843_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        for (var i = 0; i < 6; i++)
        {
            var row = new Row
            {
                id = Guid.NewGuid().ToString(), Key = Guid.NewGuid(), X = 10 + i, Name = "top" + i, Level = 100 + i,
                Sub = new Sub { X = 5 - i, Name = "n" + i, Deep = i == 3 ? null : new Deeper { Level = i % 3 } },
            };
            await _rows.CreateItemAsync(row, new PartitionKey(row.id));
            _oracle.Add(new FlatView { Key = row.Key, TopX = row.X, SubX = row.Sub.X, SubName = row.Sub.Name, Level = row.Sub.Deep?.Level });
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private static ViewDefinition Nested() => new ViewDefinitionBuilder<FlatView>()
        .From<Row>()
        .Select<Row, Guid>(r => r.Key, v => v.Key)
        .Select<Row, int>(r => r.X, v => v.TopX)
        .Select<Row, int>(r => r.Sub.X, v => v.SubX)
        .Select<Row, string?>(r => r.Sub.Name, v => v.SubName)
        .Select<Row, int?>(r => r.Sub.Deep!.Level, v => v.Level)
        .Build();

    private static IEnumerable<(Guid, int, int, string?, int?)> Tuples(IEnumerable<FlatView> rows)
        => rows.Select(v => (v.Key, v.TopX, v.SubX, v.SubName, v.Level));

    [Fact]
    public void A_path_is_quoted_segment_by_segment_and_one_segment_is_unchanged()
    {
        Birko.Data.CosmosDB.Aggregation.CosmosAggregationHelper.PathRef(new[] { "Sub", "Deep", "Level" }).Should().Be("c[\"Sub\"][\"Deep\"][\"Level\"]");
        Birko.Data.CosmosDB.Aggregation.CosmosAggregationHelper.PathRef(new[] { "X" })
            .Should().Be(Birko.Data.CosmosDB.Aggregation.CosmosAggregationHelper.FieldRef("X"));
    }

    [Fact]
    public async Task A_nested_projection_reads_the_nested_values()
    {
        if (_rows == null) return;
        var rows = (await new CosmosViewStore<FlatView>(_rows, Nested()).QueryAsync()).ToList();
        Tuples(rows).Should().BeEquivalentTo(Tuples(_oracle));
    }

    [Fact]
    public async Task A_filter_and_a_count_on_a_nested_projection_test_the_nested_value()
    {
        if (_rows == null) return;
        var store = new CosmosViewStore<FlatView>(_rows, Nested());
        var expected = _oracle.Where(v => v.SubX >= 3).Select(v => v.Key).OrderBy(k => k).ToList();
        expected.Should().HaveCount(3);

        (await store.QueryAsync(v => v.SubX >= 3)).Select(v => v.Key).OrderBy(k => k).Should().Equal(expected);
        (await store.CountAsync(v => v.SubX >= 3)).Should().Be(3);
        (await store.CountAsync(v => v.Level == 1)).Should().Be(_oracle.Count(v => v.Level == 1));
    }

    [Fact]
    public async Task An_order_by_a_nested_projection_sorts_by_the_nested_value()
    {
        if (_rows == null) return;
        var rows = (await new CosmosViewStore<FlatView>(_rows, Nested()).QueryAsync(null, OrderBy<FlatView>.ByDescending(v => v.SubX))).ToList();
        rows.Select(v => v.SubX).Should().Equal(_oracle.Select(v => v.SubX).OrderByDescending(x => x));
    }
}
