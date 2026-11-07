using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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
/// TASK-839: a collection <c>Contains</c> in a Cosmos DB view filter is <c>ARRAY_CONTAINS</c>, the list bound as one
/// array parameter, checked against the C# oracle on the live emulator. Measured first (vnext-preview, 2026-11-07): the
/// server counts the serialized parameter values toward its 524,288-character limit (13,795 Guids pass, 25,000 fail
/// with SC3020), so a long list is split by TASK-834's planner, and what cannot be split refuses past the limit.
/// Live tests are gated on <c>BIRKO_COSMOS_CONNECTION</c>; the refusal and rendering tests need no server.
/// </summary>
public sealed class Task839CosmosViewContainsTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<ItemView> _oracle = new();

    public Task839CosmosViewContainsTests(ITestOutputHelper output) => _output = output;

    public class Row
    {
        public string id { get; set; } = "";
        public Guid Key { get; set; }
        public int Num { get; set; }
        public string? Name { get; set; }
        public List<string> Tags { get; set; } = new();
        public List<int> Nums { get; set; } = new();
        public string? Region { get; set; }
        public int Amount { get; set; }
    }

    public class Nested { public List<string> Tags { get; set; } = new(); }

    /// <summary>Renamed view properties, so every path goes through <c>MapViewPropertyToSource</c>.</summary>
    public class ItemView
    {
        public Guid Key { get; set; }
        public int Number { get; set; }
        public string? Label { get; set; }
        public List<string>? Labels { get; set; }
        public List<int>? Scores { get; set; }
        public string? Region { get; set; }
        public Nested? Inner { get; set; }
    }

    public class RegionTotalView { public string? Region { get; set; } public int Total { get; set; } }

    /// <summary>A store with lowered limits, as the protected knobs allow.</summary>
    private sealed class Tuned : CosmosViewStore<ItemView>
    {
        private readonly int _chunk;
        private readonly int _max;
        public Tuned(Container c, int chunk, int max) : base(c, Items()) { _chunk = chunk; _max = max; }
        protected override int InListChunkSize => _chunk;
        protected override int MaxQueryTextLength => _max;
    }

    private sealed class TunedTotals : CosmosViewStore<RegionTotalView>
    {
        public TunedTotals(Container c) : base(c, Totals()) { }
        protected override int InListChunkSize => 10;
        protected override int MaxQueryTextLength => 2000;
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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task839_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        var names = new[] { "Alpha", "alpha", "n2", "n3", "n4", "n5", "n6", "n7" };
        for (var i = 0; i < names.Length; i++)
        {
            var row = new Row
            {
                id = Guid.NewGuid().ToString(), Key = Guid.NewGuid(), Num = i * 10, Name = names[i],
                Tags = new List<string> { "t" + (i % 3), "all" }, Nums = new List<int> { i, 100 },
                Region = i < 4 ? "EU" : "US", Amount = i,
            };
            await _rows.CreateItemAsync(row, new PartitionKey(row.id));
            _oracle.Add(new ItemView { Key = row.Key, Number = row.Num, Label = row.Name, Labels = row.Tags, Scores = row.Nums, Region = row.Region });
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private static ViewDefinition Items() => new ViewDefinitionBuilder<ItemView>()
        .From<Row>()
        .Select<Row, Guid>(r => r.Key, v => v.Key)
        .Select<Row, int>(r => r.Num, v => v.Number)
        .Select<Row, string?>(r => r.Name, v => v.Label)
        .Select<Row, List<string>>(r => r.Tags, v => v.Labels!)
        .Select<Row, List<int>>(r => r.Nums, v => v.Scores!)
        .Select<Row, string?>(r => r.Region, v => v.Region)
        .Build();

    private static ViewDefinition Totals() => new ViewDefinitionBuilder<RegionTotalView>()
        .From<Row>()
        .GroupBy<Row, string?>(r => r.Region)
        .Select<Row, string?>(r => r.Region, v => v.Region)
        .Sum<Row, int>(r => r.Amount, v => v.Total)
        .Build();

    public static IEnumerable<object[]> Cases() => new[]
    {
        "guid list", "guid array", "guid sequence", "int list", "string list exact", "array property string",
        "array property int", "negated list", "negated array property", "or", "and", "empty list", "negated empty list",
    }.Select(c => new object[] { c });

    private Expression<Func<ItemView, bool>> Case(string name)
    {
        var k = _oracle.Select(v => v.Key).ToList();
        var ids = new List<Guid> { k[0], k[2], k[5], Guid.NewGuid(), k[2] };
        var array = ids.ToArray();
        IEnumerable<Guid> sequence = ids;
        var nums = new List<int> { 10, 30, 31, 70 };
        var names = new List<string> { "alpha", "n3", "N4", "nope" };
        var empty = new List<Guid>();
        return name switch
        {
            "guid list" => v => ids.Contains(v.Key),
            "guid array" => v => array.Contains(v.Key),
            "guid sequence" => v => sequence.Contains(v.Key),
            "int list" => v => nums.Contains(v.Number),
            "string list exact" => v => names.Contains(v.Label!),
            "array property string" => v => v.Labels!.Contains("t1"),
            "array property int" => v => v.Scores!.Contains(3),
            "negated list" => v => !ids.Contains(v.Key),
            "negated array property" => v => !v.Labels!.Contains("t1"),
            "or" => v => ids.Contains(v.Key) || v.Labels!.Contains("t2"),
            "and" => v => ids.Contains(v.Key) && v.Region == "EU",
            "empty list" => v => empty.Contains(v.Key),
            "negated empty list" => v => !empty.Contains(v.Key),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private async Task MatchesOracle(CosmosViewStore<ItemView> store, Expression<Func<ItemView, bool>> filter, string label)
    {
        var expected = _oracle.Where(filter.Compile()).Select(v => v.Key).OrderBy(x => x).ToList();
        var read = (await store.QueryAsync(filter)).ToList();
        read.Select(v => v.Key).OrderBy(x => x).Should().Equal(expected, label);
        (await store.CountAsync(filter)).Should().Be(expected.Count, label);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_view_Contains_returns_the_oracle_rows(string name)
    {
        if (_rows == null) return;
        var filter = Case(name);
        await MatchesOracle(new CosmosViewStore<ItemView>(_rows, Items()), filter, name);
        // Chunks of 2 under the default limit: plain AND conjuncts split, the rest run as one query.
        await MatchesOracle(new Tuned(_rows, 2, 524_288), filter, name + " (chunks of 2)");
    }

    [Fact]
    public async Task The_empty_list_case_is_not_vacuous()
    {
        if (_rows == null) return;
        var store = new CosmosViewStore<ItemView>(_rows, Items());
        (await store.CountAsync(Case("empty list"))).Should().Be(0);
        (await store.CountAsync(Case("negated empty list"))).Should().Be(_oracle.Count);
        (await store.CountAsync(Case("guid list"))).Should().Be(3);
        (await store.CountAsync(Case("string list exact"))).Should().Be(2, "'alpha' and 'n3' only: 'Alpha' and 'n4' differ by case");
    }

    /// <summary>Past TASK-834's 13,105-Guid text ceiling and past the 13,795-Guid parameter ceiling measured here.</summary>
    [Fact]
    public async Task A_read_and_count_with_20000_ids_succeed_through_chunks()
    {
        if (_rows == null) return;
        var known = _oracle.Take(5).Select(v => v.Key).ToList();
        var ids = Enumerable.Range(0, 20_000).Select(_ => Guid.NewGuid()).Concat(known).Concat(known).ToList();
        var store = new CosmosViewStore<ItemView>(_rows, Items());

        (await store.QueryAsync(v => ids.Contains(v.Key))).Select(v => v.Key).Should().BeEquivalentTo(known);
        (await store.CountAsync(v => ids.Contains(v.Key) && v.Region == "EU")).Should().Be(4);
        var array = ids.ToArray();
        (await store.CountAsync(v => array.Contains(v.Key))).Should().Be(5);
    }

    [Fact]
    public void The_list_is_bound_as_one_parameter_and_the_query_text_does_not_grow()
    {
        var few = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var many = Enumerable.Range(0, 20_000).Select(_ => Guid.NewGuid()).ToList();
        var pFew = new List<KeyValuePair<string, object?>>();
        var pMany = new List<KeyValuePair<string, object?>>();

        var textFew = CosmosFilterTranslator.Translate<ItemView>(v => few.Contains(v.Key) && v.Region == "EU", pFew);
        var textMany = CosmosFilterTranslator.Translate<ItemView>(v => many.Contains(v.Key) && v.Region == "EU", pMany);

        textMany.Should().Be(textFew).And.Be("(ARRAY_CONTAINS(@p0, c.Key) AND c.Region = @p1)");
        pMany.Should().HaveCount(2);
        pMany[0].Value.Should().BeOfType<object[]>().Which.Should().HaveCount(20_000);
    }

    [Fact]
    public void The_paths_go_through_the_view_to_source_mapping()
    {
        var map = new Dictionary<string, string> { ["Key"] = "SrcKey", ["Labels"] = "SrcTags" };
        var ids = new List<Guid> { Guid.NewGuid() };
        var p = new List<KeyValuePair<string, object?>>();
        CosmosFilterTranslator.Translate<ItemView>(v => ids.Contains(v.Key) || !v.Labels!.Contains("x"), p, n => map[n])
            .Should().Be("(ARRAY_CONTAINS(@p0, c.SrcKey) OR NOT (ARRAY_CONTAINS(c.SrcTags, @p1)))");
        CosmosFilterTranslator.Translate<ItemView>(v => new List<int>().Contains(v.Number), p).Should().Be("false");
    }

    private static Container Unreachable(out CosmosClient client)
    {
        client = new CosmosClient(
            "AccountEndpoint=http://127.0.0.1:1/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;",
            new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway, RequestTimeout = TimeSpan.FromSeconds(2), MaxRetryAttemptsOnRateLimitedRequests = 0 });
        return client.GetContainer("db", "rows");
    }

    public static IEnumerable<object[]> PastTheLimit() => new[]
    {
        new object[] { "negated", "NOT IN" }, new object[] { "or", "under an OR" }, new object[] { "sorted", "sort" },
        new object[] { "paged", "sort, limit or offset" }, new object[] { "first", "sort, limit or offset" },
        new object[] { "strings", "strings" }, new object[] { "grouped read", "grouped view" },
        new object[] { "grouped count", "grouped view" },
    };

    /// <summary>
    /// Before any request: the container points at a port nothing listens on, so a request would fail with a network
    /// error, never a <see cref="NotSupportedException"/>. Limits lowered to chunks of 10 and 2,000 characters.
    /// </summary>
    [Theory]
    [MemberData(nameof(PastTheLimit))]
    public async Task A_list_that_cannot_be_split_is_refused_past_the_limit_before_any_request(string shape, string reason)
    {
        var container = Unreachable(out var client);
        using var _ = client;
        var ids = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToList();
        var strings = Enumerable.Range(0, 100).Select(i => "value-" + i.ToString("D10")).ToList();
        var store = new Tuned(container, 10, 2000);
        var totals = new TunedTotals(container);
        Func<Task> act = shape switch
        {
            "negated" => () => store.QueryAsync(v => !ids.Contains(v.Key)),
            "or" => () => store.CountAsync(v => ids.Contains(v.Key) || v.Region == "EU"),
            "sorted" => () => store.QueryAsync(v => ids.Contains(v.Key), OrderBy<ItemView>.By(v => v.Number)),
            "paged" => () => store.QueryAsync(v => ids.Contains(v.Key), null, 5),
            "first" => () => store.QueryFirstAsync(v => ids.Contains(v.Key)),
            "strings" => () => store.QueryAsync(v => strings.Contains(v.Label!)),
            "grouped read" => () => totals.QueryAsync(v => strings.Contains(v.Region!)),
            "grouped count" => () => totals.CountAsync(v => strings.Contains(v.Region!)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        (await act.Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain(reason).And.Contain("2000").And.NotContain(ids[0].ToString()).And.NotContain(strings[0]);
    }

    public static IEnumerable<object[]> Unsupported() => new[]
    {
        new object[] { "computed", "computed expression" }, new object[] { "comparer", "comparer" },
        new object[] { "nested collection", "nested collection" }, new object[] { "both sides", "both" },
        new object[] { "nested member", "nested member" }, new object[] { "aggregate property", "'RegionTotalView.Total'" },
    };

    [Theory]
    [MemberData(nameof(Unsupported))]
    public async Task An_unsupported_Contains_shape_is_refused_by_name_before_any_request(string shape, string expected)
    {
        var container = Unreachable(out var client);
        using var _ = client;
        var ids = new List<Guid> { Guid.NewGuid() };
        var ints = new List<int> { 1, 2 };
        var lists = new List<List<string>> { new() { "a" } };
        var store = new CosmosViewStore<ItemView>(container, Items());
        var totals = new CosmosViewStore<RegionTotalView>(container, Totals());
        Func<Task> act = shape switch
        {
            "computed" => () => store.QueryAsync(v => ints.Contains(v.Number + 1)),
            "comparer" => () => store.CountAsync(v => ids.Contains(v.Key, EqualityComparer<Guid>.Default)),
            "nested collection" => () => store.QueryAsync(v => lists.Contains(v.Labels!)),
            "both sides" => () => store.QueryAsync(v => v.Labels!.Contains(v.Label!)),
            "nested member" => () => store.QueryAsync(v => v.Inner!.Tags.Contains("a")),
            "aggregate property" => () => totals.QueryAsync(v => ints.Contains(v.Total)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain(expected);
    }
}
