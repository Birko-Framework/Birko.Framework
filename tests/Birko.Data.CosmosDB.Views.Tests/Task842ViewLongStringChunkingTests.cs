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
/// TASK-842: a view's <c>Contains</c> list is chunked by serialized length too, through the same planner. 1,200 distinct
/// 600-character strings, with the view's DEFAULT chunk size and limit: one 1,000-value chunk would be over Cosmos DB's
/// 524,288 characters. Checked against the C# oracle on the live emulator, gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class Task842ViewLongStringChunkingTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<NameView> _oracle = new();

    public Task842ViewLongStringChunkingTests(ITestOutputHelper output) => _output = output;

    public class Row
    {
        public string id { get; set; } = "";
        public Guid Key { get; set; }
        public string? Name { get; set; }
        public int Num { get; set; }
    }

    public class NameView
    {
        public Guid Key { get; set; }
        public string? Label { get; set; }
        public int Number { get; set; }
    }

    private static ViewDefinition Names() => new ViewDefinitionBuilder<NameView>()
        .From<Row>()
        .Select<Row, Guid>(r => r.Key, v => v.Key)
        .Select<Row, string?>(r => r.Name, v => v.Label)
        .Select<Row, int>(r => r.Num, v => v.Number)
        .Build();

    private static string Long(int i) => $"k{i:D5}-" + new string('y', 593);

    /// <summary>Forty wanted names spread over the list, a case variant and a trailing-space near miss, a short one.</summary>
    private static readonly string[] Stored =
        Enumerable.Range(0, 40).Select(i => Long(i * 30))
            .Concat(new[] { Long(1).ToUpperInvariant(), Long(2) + " ", "short" }).ToArray();

    /// <summary>1,200 distinct long strings, with duplicates of the first ones at the end.</summary>
    private static List<string> Wanted()
        => Enumerable.Range(0, 1200).Select(Long).Concat(Enumerable.Range(0, 20).Select(Long)).ToList();

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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task842_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        for (var i = 0; i < Stored.Length; i++)
        {
            var row = new Row { id = Guid.NewGuid().ToString(), Key = Guid.NewGuid(), Name = Stored[i], Num = i };
            await _rows.CreateItemAsync(row, new PartitionKey(row.id));
            _oracle.Add(new NameView { Key = row.Key, Label = row.Name, Number = row.Num });
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    [Fact]
    public async Task A_long_string_view_list_one_count_chunk_overflows_is_read_and_counted_equal_to_the_oracle()
    {
        if (_rows == null) return;
        var wanted = Wanted();
        var thousand = System.Text.Json.JsonSerializer.Serialize(wanted.Take(1000).ToArray()).Length;
        _output.WriteLine($"a 1,000-value chunk's parameter alone is {thousand} characters");
        thousand.Should().BeGreaterThan(524_288, "one count-sized chunk would be over the limit");

        var store = new CosmosViewStore<NameView>(_rows, Names());
        var filters = new Expression<Func<NameView, bool>>[]
        {
            v => wanted.Contains(v.Label!),
            v => v.Number >= 0 && wanted.Contains(v.Label!),
        };
        foreach (var filter in filters)
        {
            var expected = _oracle.Where(filter.Compile()).Select(v => v.Key).OrderBy(k => k).ToList();
            expected.Should().HaveCount(40);
            (await store.QueryAsync(filter)).Select(v => v.Key).OrderBy(k => k).Should().Equal(expected);
            (await store.CountAsync(filter)).Should().Be(expected.Count);
        }
    }

    [Fact]
    public async Task A_single_view_value_too_large_for_any_chunk_is_refused_by_name()
    {
        if (_rows == null) return;
        var names = new List<string> { "short", "h" + new string('z', 600_000) };
        var store = new CosmosViewStore<NameView>(_rows, Names());
        var ex = (await store.Invoking(s => s.QueryAsync(v => names.Contains(v.Label!))).Should().ThrowAsync<NotSupportedException>()).Which;
        _output.WriteLine(ex.Message);
        ex.Message.Should().Contain("one value").And.Contain("524288").And.NotContain("zzzzzzzzzz");
        await store.Invoking(s => s.CountAsync(v => names.Contains(v.Label!))).Should().ThrowAsync<NotSupportedException>().WithMessage("*one value*");
    }
}
