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
/// TASK-841: a view's string <c>Contains</c> list past the limit is split, since Cosmos DB's <c>ARRAY_CONTAINS</c>
/// matches only the ordinally equal string (measured in <c>Task841StringChunkingLiveTests</c>). Checked against the C#
/// oracle on the live emulator, gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class Task841ViewStringChunkingTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<NameView> _oracle = new();

    public Task841ViewStringChunkingTests(ITestOutputHelper output) => _output = output;

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

    private const int Chunk = 3;
    private const int Max = 2000;

    /// <summary>Chunks of 3 and a 2,000-character limit, which the whole list is over and one chunk is under.</summary>
    private sealed class Tuned : CosmosViewStore<NameView>
    {
        public Tuned(Container c) : base(c, Names()) { }
        protected override int InListChunkSize => Chunk;
        protected override int MaxQueryTextLength => Max;
    }

    private static ViewDefinition Names() => new ViewDefinitionBuilder<NameView>()
        .From<Row>()
        .Select<Row, Guid>(r => r.Key, v => v.Key)
        .Select<Row, string?>(r => r.Name, v => v.Label)
        .Select<Row, int>(r => r.Num, v => v.Number)
        .Build();

    private static string Long(int i) => $"n{i}-" + new string('x', 300);

    private static readonly string[] Stored =
        new[] { "a", "A", "a ", "\u00e9", "e\u0301", "e", "Stra\u00dfe", "STRASSE", "strasse" }
            .Concat(Enumerable.Range(0, 12).Select(Long)).ToArray();

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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task841_" + Guid.NewGuid().ToString("N"))).Database;
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

    /// <summary>
    /// One of each confusable pair, long names, near misses of two stored ones (case, a trailing space), an absent one,
    /// and duplicates in later chunks: 12 distinct values, 9 of them stored.
    /// </summary>
    private static List<string> Wanted() => new()
    {
        "a", "e\u0301", "STRASSE", Long(0), Long(1), Long(2), Long(3), Long(4), Long(5).ToUpperInvariant(), Long(6) + " ",
        "absent", "a", Long(0), "e\u0301", Long(11),
    };

    [Fact]
    public async Task A_chunked_string_view_read_and_count_equal_the_oracle()
    {
        if (_rows == null) return;
        var wanted = Wanted();
        var store = new Tuned(_rows);
        var filters = new Expression<Func<NameView, bool>>[]
        {
            v => wanted.Contains(v.Label!),
            v => v.Number >= 0 && wanted.Contains(v.Label!),
        };

        foreach (var filter in filters)
        {
            var expected = _oracle.Where(filter.Compile()).Select(v => v.Key).OrderBy(k => k).ToList();
            expected.Should().HaveCount(9);
            (await store.QueryAsync(filter)).Select(v => v.Key).OrderBy(k => k).Should().Equal(expected);
            (await store.CountAsync(filter)).Should().Be(expected.Count);
        }

        // The whole list is over the lowered limit: without chunks the view would refuse it.
        var serialized = System.Text.Json.JsonSerializer.Serialize(wanted.Distinct(StringComparer.Ordinal).ToArray()).Length;
        _output.WriteLine($"the list alone serializes to {serialized} characters, over the lowered limit of {Max}");
        serialized.Should().BeGreaterThan(Max);
    }
}
