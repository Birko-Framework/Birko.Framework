using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Aggregation;
using Birko.Data.CosmosDB.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-844: the view store wrote every identifier bare, so a name Cosmos SQL reads as a keyword failed every query on
/// the view with a 400 (measured on the emulator, vnext-preview, 2026-11-07, SC1001 "incorrect syntax near"):
/// a projection alias (<c>c["Sub"] AS Inner</c>, <c>c["Value"] AS Value</c>), a grouped view's aliases, and a filter or
/// sort on a source field named so (<c>c.Value</c>, <c>c.Sub.Value</c>). Aliases are now quoted strings
/// (<c>AS "Value"</c>, <see cref="CosmosAggregationHelper.Alias"/>) and paths are bracket-quoted
/// (<c>c["Sub"]["Value"]</c>). The live tests are gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class Task844CosmosViewReservedNameTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<Row> _source = new();

    public Task844CosmosViewReservedNameTests(ITestOutputHelper output) => _output = output;

    public class Sub { public int Value { get; set; } }

    /// <summary>Source fields named like Cosmos SQL keywords: <c>Value</c>, <c>Select</c>, and <c>Sub.Value</c>.</summary>
    public class Row
    {
        public string id { get; set; } = "";
        public Guid Key { get; set; }
        public int Value { get; set; }
        public string? Select { get; set; }
        public int G { get; set; }
        public Sub Sub { get; set; } = new();
    }

    /// <summary>View properties named like keywords: <c>Inner</c>, <c>Value</c>, <c>Order</c>, <c>Top</c>.</summary>
    public class KeywordView
    {
        public Guid Key { get; set; }
        public Sub? Inner { get; set; }
        public int Value { get; set; }
        public string? Order { get; set; }
        public int Top { get; set; }
    }

    /// <summary>Plain view names over keyword source fields: only the WHERE / ORDER BY paths carry a keyword.</summary>
    public class PlainView
    {
        public Guid Key { get; set; }
        public int Amount { get; set; }
        public Sub? Child { get; set; }
    }

    public class GroupView
    {
        public int Value { get; set; }
        public int Inner { get; set; }
        public int Select { get; set; }
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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task844_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        for (var i = 0; i < 6; i++)
        {
            var row = new Row
            {
                id = Guid.NewGuid().ToString(), Key = Guid.NewGuid(), Value = 10 + i, Select = "s" + i, G = i % 2,
                Sub = new Sub { Value = 5 - i },
            };
            await _rows.CreateItemAsync(row, new PartitionKey(row.id));
            _source.Add(row);
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private static ViewDefinition Keywords() => new ViewDefinitionBuilder<KeywordView>()
        .From<Row>()
        .Select<Row, Guid>(r => r.Key, v => v.Key)
        .Select<Row, Sub?>(r => r.Sub, v => v.Inner)
        .Select<Row, int>(r => r.Value, v => v.Value)
        .Select<Row, string?>(r => r.Select, v => v.Order)
        .Select<Row, int>(r => r.Sub.Value, v => v.Top)
        .Build();

    private IEnumerable<KeywordView> Oracle() => _source.Select(r => new KeywordView
    {
        Key = r.Key, Inner = new Sub { Value = r.Sub.Value }, Value = r.Value, Order = r.Select, Top = r.Sub.Value,
    });

    private static IEnumerable<(Guid, int?, int, string?, int)> Tuples(IEnumerable<KeywordView> rows)
        => rows.Select(v => (v.Key, v.Inner?.Value, v.Value, v.Order, v.Top));

    [Fact]
    public void An_alias_is_a_quoted_string_escaped_like_a_field_name()
    {
        CosmosAggregationHelper.Alias("Value").Should().Be("\"Value\"");
        CosmosAggregationHelper.Alias("we\"ird\\").Should().Be("\"we\\\"ird\\\\\"");
    }

    [Fact]
    public void The_aggregate_helper_quotes_group_and_aggregate_aliases()
    {
        var (select, groupBy) = CosmosAggregationHelper.BuildAggregateSqlParts(
            new[] { ("G", "Value") },
            new (AggregateFunction, string?, string)[] { (AggregateFunction.Count, null, "Inner") });
        select.Should().Be("SELECT c[\"G\"] AS \"Value\", COUNT(1) AS \"Inner\" FROM c");
        groupBy.Should().Be(" GROUP BY c[\"G\"]");
    }

    [Fact]
    public async Task A_view_with_keyword_properties_reads_and_counts_the_oracle()
    {
        if (_rows == null) return;
        var store = new CosmosViewStore<KeywordView>(_rows, Keywords());

        Tuples(await store.QueryAsync()).Should().BeEquivalentTo(Tuples(Oracle()));
        (await store.CountAsync()).Should().Be(_source.Count);
        (await store.QueryFirstAsync(v => v.Key == _source[2].Key))!.Order.Should().Be("s2");
    }

    [Fact]
    public async Task A_filter_on_a_keyword_property_and_on_a_member_named_Value_tests_the_oracle()
    {
        if (_rows == null) return;
        var store = new CosmosViewStore<KeywordView>(_rows, Keywords());
        var oracle = Oracle().ToList();

        (await store.QueryAsync(v => v.Value >= 13)).Select(v => v.Key)
            .Should().BeEquivalentTo(oracle.Where(v => v.Value >= 13).Select(v => v.Key));
        (await store.CountAsync(v => v.Value >= 13)).Should().Be(oracle.Count(v => v.Value >= 13));

        (await store.QueryAsync(v => v.Inner!.Value < 3)).Select(v => v.Key)
            .Should().BeEquivalentTo(oracle.Where(v => v.Inner!.Value < 3).Select(v => v.Key));
        (await store.CountAsync(v => v.Top < 3 && v.Order != "s5")).Should().Be(oracle.Count(v => v.Top < 3 && v.Order != "s5"));
    }

    [Fact]
    public async Task A_sort_on_a_keyword_property_and_a_page_follow_the_oracle()
    {
        if (_rows == null) return;
        var store = new CosmosViewStore<KeywordView>(_rows, Keywords());
        var oracle = Oracle().ToList();

        (await store.QueryAsync(null, OrderBy<KeywordView>.ByDescending(v => v.Value))).Select(v => v.Value)
            .Should().Equal(oracle.Select(v => v.Value).OrderByDescending(x => x));
        (await store.QueryAsync(v => v.Value > 10, OrderBy<KeywordView>.By(v => v.Top), limit: 2, offset: 1)).Select(v => v.Top)
            .Should().Equal(oracle.Where(v => v.Value > 10).Select(v => v.Top).OrderBy(x => x).Skip(1).Take(2));
    }

    [Fact]
    public async Task A_filter_and_a_sort_on_a_source_field_named_Value_read_the_field()
    {
        if (_rows == null) return;
        var definition = new ViewDefinitionBuilder<PlainView>()
            .From<Row>()
            .Select<Row, Guid>(r => r.Key, v => v.Key)
            .Select<Row, int>(r => r.Value, v => v.Amount)
            .Select<Row, Sub?>(r => r.Sub, v => v.Child)
            .Build();
        var store = new CosmosViewStore<PlainView>(_rows, definition);

        (await store.CountAsync(v => v.Amount > 12)).Should().Be(_source.Count(r => r.Value > 12));
        (await store.CountAsync(v => v.Child!.Value > 2)).Should().Be(_source.Count(r => r.Sub.Value > 2));
        (await store.QueryAsync(v => v.Child!.Value <= 2, OrderBy<PlainView>.ByDescending(v => v.Amount))).Select(v => v.Amount)
            .Should().Equal(_source.Where(r => r.Sub.Value <= 2).Select(r => r.Value).OrderByDescending(x => x));
    }

    [Fact]
    public async Task A_grouped_view_with_keyword_aliases_reads_and_counts_the_oracle()
    {
        if (_rows == null) return;
        var definition = new ViewDefinitionBuilder<GroupView>()
            .From<Row>()
            .GroupBy<Row, int>(r => r.G)
            .Select<Row, int>(r => r.G, v => v.Value)
            .Count<Row>(v => v.Inner)
            .Sum<Row, int>(r => r.Value, v => v.Select)
            .Build();
        var store = new CosmosViewStore<GroupView>(_rows, definition);
        var oracle = _source.GroupBy(r => r.G)
            .Select(g => new GroupView { Value = g.Key, Inner = g.Count(), Select = g.Sum(r => r.Value) }).ToList();

        (await store.QueryAsync()).Should().BeEquivalentTo(oracle);
        (await store.CountAsync()).Should().Be(oracle.Count);
        (await store.QueryAsync(v => v.Value == 1)).Should().BeEquivalentTo(oracle.Where(v => v.Value == 1));
        (await store.CountAsync(v => v.Value == 1)).Should().Be(1);
    }
}
