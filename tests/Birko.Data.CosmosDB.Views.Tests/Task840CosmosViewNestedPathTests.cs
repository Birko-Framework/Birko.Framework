using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Views;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-840: a Cosmos DB view filter on a nested property (<c>v.Child.X</c>) rendered <c>c.X</c>, keeping only the
/// last member name, so it compared a same-named top-level field (or a missing one) and answered the wrong rows with
/// no error. Measured on the emulator first (vnext-preview, 2026-11-07). The path is now rendered in full, its first
/// segment mapped through the view; a member that is not a document path (string <c>Length</c>, a
/// collection's <c>Count</c>, a captured variable) is refused by name before any request. <c>HasValue</c> was refused
/// here too until TASK-896, which translates it exactly (IS_DEFINED and not IS_NULL).
/// Live tests are gated on <c>BIRKO_COSMOS_CONNECTION</c>; the rendering and refusal tests need no server.
/// </summary>
public sealed class Task840CosmosViewNestedPathTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<NestedView> _oracle = new();

    public Task840CosmosViewNestedPathTests(ITestOutputHelper output) => _output = output;

    public class Sub
    {
        public int X { get; set; }
        public string? Name { get; set; }
        public List<string> Tags { get; set; } = new();
        public Deeper? Deep { get; set; }
    }

    public class Deeper { public int Level { get; set; } }

    /// <summary>The source document: a top-level <c>X</c> beside the nested <c>Sub.X</c>, with different values.</summary>
    public class Row
    {
        public string id { get; set; } = "";
        public Guid Key { get; set; }
        public int X { get; set; }
        public int? Maybe { get; set; }
        public Sub Sub { get; set; } = new();
    }

    /// <summary>The view renames <c>Sub</c> to <c>Child</c>, so the first segment goes through the mapping.</summary>
    public class NestedView
    {
        public Guid Key { get; set; }
        public int? Maybe { get; set; }
        public Sub? Child { get; set; }
    }

    public struct Point { public int A { get; set; } }

    public class ShapesView
    {
        public string? Label { get; set; }
        public int? Maybe { get; set; }
        public List<string>? Tags { get; set; }
        public Point Point { get; set; }
        public Sub? Child { get; set; }
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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task840_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        for (var i = 0; i < 8; i++)
        {
            var row = new Row
            {
                id = Guid.NewGuid().ToString(), Key = Guid.NewGuid(), X = 7 - i, Maybe = i % 2 == 0 ? i : null,
                Sub = new Sub { X = i, Name = "n" + i, Tags = new List<string> { "t" + (i % 3) }, Deep = new Deeper { Level = i % 4 } },
            };
            await _rows.CreateItemAsync(row, new PartitionKey(row.id));
            _oracle.Add(new NestedView { Key = row.Key, Maybe = row.Maybe, Child = row.Sub });
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private static ViewDefinition Mapped() => new ViewDefinitionBuilder<NestedView>()
        .From<Row>()
        .Select<Row, Guid>(r => r.Key, v => v.Key)
        .Select<Row, int?>(r => r.Maybe, v => v.Maybe)
        .Select<Row, Sub?>(r => r.Sub, v => v.Child)
        .Build();

    public static IEnumerable<object[]> Cases() => new[]
    {
        "nested equal", "nested range", "nested string", "two levels", "nested and top", "negated nested", "or",
        "nested array contains", "list contains nested", "nullable value",
    }.Select(c => new object[] { c });

    private static Expression<Func<NestedView, bool>> Case(string name)
    {
        var xs = new List<int> { 1, 4, 9 };
        return name switch
        {
            "nested equal" => v => v.Child!.X == 2,
            "nested range" => v => v.Child!.X >= 3 && v.Child!.X < 6,
            "nested string" => v => v.Child!.Name == "n6",
            "two levels" => v => v.Child!.Deep!.Level == 1,
            "nested and top" => v => v.Child!.X == 4 && v.Maybe == 4,
            "negated nested" => v => !(v.Child!.X == 2),
            "or" => v => v.Child!.X == 0 || v.Child!.Name == "n7",
            "nested array contains" => v => v.Child!.Tags.Contains("t1"),
            "list contains nested" => v => xs.Contains(v.Child!.X),
            "nullable value" => v => v.Maybe!.Value == 6,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    private async Task MatchesOracle(CosmosViewStore<NestedView> store, Expression<Func<NestedView, bool>> filter, string label)
    {
        var compiled = filter.Compile();
        var expected = _oracle.Where(v => { try { return compiled(v); } catch (InvalidOperationException) { return false; } })
            .Select(v => v.Key).OrderBy(x => x).ToList();
        expected.Should().NotBeEmpty("the case must select something, or a filter matching nothing would pass ({0})", label);
        var read = (await store.QueryAsync(filter)).ToList();
        read.Select(v => v.Key).OrderBy(x => x).Should().Equal(expected, label);
        (await store.CountAsync(filter)).Should().Be(expected.Count, label);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_nested_view_filter_returns_the_oracle_rows(string name)
    {
        if (_rows == null) return;
        await MatchesOracle(new CosmosViewStore<NestedView>(_rows, Mapped()), Case(name), name);
    }

    /// <summary>A view with no field list selects the whole document (<c>SELECT VALUE c</c>): the path is the source's.</summary>
    [Fact]
    public async Task A_nested_filter_on_a_whole_document_view_returns_the_oracle_rows()
    {
        if (_rows == null) return;
        var store = new CosmosViewStore<Row>(_rows, new ViewDefinitionBuilder<Row>().From<Row>().Build());
        var read = (await store.QueryAsync(r => r.Sub.X == 2)).ToList();
        read.Select(r => r.Sub.X).Should().Equal(2);
        read.Single().X.Should().Be(5, "the top-level X of the row whose Sub.X is 2");
        (await store.CountAsync(r => r.Sub.Deep!.Level == 3)).Should().Be(2);
    }

    /// <summary>For comparison: the SDK's LINQ provider, which the base store uses, renders the whole path.</summary>
    [Fact]
    public void The_SDK_LINQ_provider_renders_the_whole_path()
    {
        if (_rows == null) return;
        var text = _rows.GetItemLinqQueryable<Row>().Where(r => r.Sub.X == 2).ToQueryDefinition().QueryText;
        _output.WriteLine(text);
        text.Should().Contain("[\"Sub\"][\"X\"]");
    }

    [Fact]
    public void The_whole_path_is_rendered_and_only_its_first_segment_is_mapped()
    {
        var map = new Dictionary<string, string> { ["Child"] = "Sub", ["Maybe"] = "SrcMaybe" };
        var p = new List<KeyValuePair<string, object?>>();
        var xs = new List<int> { 1 };
        CosmosFilterTranslator.Translate<NestedView>(v => v.Child!.Deep!.Level == 1 && v.Child!.Tags.Contains("a"), p, n => map[n])
            .Should().Be("(c[\"Sub\"][\"Deep\"][\"Level\"] = @p0 AND ARRAY_CONTAINS(c[\"Sub\"][\"Tags\"], @p1))");
        CosmosFilterTranslator.Translate<NestedView>(v => xs.Contains(v.Child!.X) || v.Maybe!.Value == 3, p, n => map[n])
            .Should().Be("(ARRAY_CONTAINS(@p2, c[\"Sub\"][\"X\"]) OR c[\"SrcMaybe\"] = @p3)");
    }

    private static Container Unreachable(out CosmosClient client)
    {
        client = new CosmosClient(
            "AccountEndpoint=http://127.0.0.1:1/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;",
            new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway, RequestTimeout = TimeSpan.FromSeconds(2), MaxRetryAttemptsOnRateLimitedRequests = 0 });
        return client.GetContainer("db", "rows");
    }

    public static IEnumerable<object[]> NotAPath() => new[]
    {
        new object[] { "string length", "'Length' of String" },
        new object[] { "count", "'Count' of List" }, new object[] { "struct member", "'A' of Point" },
        new object[] { "captured variable", "does not read the view" }, new object[] { "contains on length", "'Length' of String" },
    };

    /// <summary>
    /// Before any request: the container points at a port nothing listens on. Each of these used to render a path
    /// ending in the member's name (<c>c.Length</c>, <c>c.Count</c>, <c>c.A</c>), a field the document does not have.
    /// </summary>
    [Theory]
    [MemberData(nameof(NotAPath))]
    public async Task A_member_that_is_not_a_document_path_is_refused_by_name_before_any_request(string shape, string expected)
    {
        var container = Unreachable(out var client);
        using var _ = client;
        var store = new CosmosViewStore<ShapesView>(container, new ViewDefinitionBuilder<ShapesView>().From<ShapesView>().Build());
        var local = new Sub { X = 3 };
        var ints = new List<int> { 1 };
        Func<Task> act = shape switch
        {
            "string length" => () => store.QueryAsync(v => v.Label!.Length > 3),
            "count" => () => store.QueryAsync(v => v.Tags!.Count > 1),
            "struct member" => () => store.QueryAsync(v => v.Point.A == 1),
            "captured variable" => () => store.QueryAsync(v => local.X == 3),
            "contains on length" => () => store.CountAsync(v => ints.Contains(v.Child!.Name!.Length)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain(expected).And.Contain("TASK-840");
    }
}
