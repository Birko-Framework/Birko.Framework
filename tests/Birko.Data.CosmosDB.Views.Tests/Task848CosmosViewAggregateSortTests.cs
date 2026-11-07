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
/// TASK-848: Cosmos DB cannot sort a grouped query by an aggregate (measured on the emulator, vnext-preview, 2026-11-07:
/// a bare alias is SC2001, a quoted alias or the aggregate expression 2206), and the store used to send it and get a 400.
/// It is now refused by name before any request; a sort by a group key, with a page, still runs on the server. Gated on
/// <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class Task848CosmosViewAggregateSortTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<Row> _source = new();

    public Task848CosmosViewAggregateSortTests(ITestOutputHelper output) => _output = output;

    public class Row
    {
        public string id { get; set; } = "";
        public string Category { get; set; } = "";
        public int Amount { get; set; }
    }

    public class ByCategory
    {
        public string? Name { get; set; }
        public int Total { get; set; }
        public int Rows { get; set; }
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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task848_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        for (var i = 0; i < 12; i++)
        {
            var row = new Row { id = Guid.NewGuid().ToString(), Category = "c" + (i % 5), Amount = i * 7 % 11 };
            await _rows.CreateItemAsync(row, new PartitionKey(row.id));
            _source.Add(row);
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private CosmosViewStore<ByCategory> Store() => new(_rows!, new ViewDefinitionBuilder<ByCategory>()
        .From<Row>()
        .GroupBy<Row, string>(r => r.Category)
        .Select<Row, string?>(r => r.Category, v => v.Name)
        .Sum<Row, int>(r => r.Amount, v => v.Total)
        .Count<Row>(v => v.Rows)
        .Build());

    private List<ByCategory> Oracle() => _source.GroupBy(r => r.Category)
        .Select(g => new ByCategory { Name = g.Key, Total = g.Sum(r => r.Amount), Rows = g.Count() }).ToList();

    [Fact]
    public async Task A_grouped_view_sorted_by_its_group_key_and_paged_follows_the_oracle()
    {
        if (_rows == null) return;
        var expected = Oracle().OrderByDescending(v => v.Name, StringComparer.Ordinal).Skip(1).Take(3).ToList();

        var page = (await Store().QueryAsync(null, OrderBy<ByCategory>.ByDescending(v => v.Name!), limit: 3, offset: 1)).ToList();

        page.Should().BeEquivalentTo(expected, o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData("Total")]
    [InlineData("Rows")]
    public async Task A_grouped_view_sorted_by_an_aggregate_is_refused_by_name(string property)
    {
        if (_rows == null) return;
        var read = () => Store().QueryAsync(null, OrderBy<ByCategory>.ByName(property), limit: 2);

        (await read.Should().ThrowExactlyAsync<NotSupportedException>()).WithMessage($"*ByCategory.{property}*TASK-848*");
    }
}
