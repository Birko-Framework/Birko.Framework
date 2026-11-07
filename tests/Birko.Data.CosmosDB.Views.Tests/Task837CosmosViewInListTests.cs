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
/// TASK-837: a Cosmos DB view filter <c>ids.Contains(v.Col)</c>. The view store translates its filter itself
/// (<c>CosmosFilterTranslator</c>, parameters bound), not through the SDK's LINQ provider, and it has no IN: measured on
/// the emulator (vnext-preview, 2026-11-07) a 2-value list already failed, with "A MemberAccess operand could not be
/// evaluated to a constant" (list) or "Method Contains is not supported" (array), so TASK-834's query-text ceiling is
/// not reachable through a view and there is nothing to chunk. The refusal now names the IN list, on every length, before
/// any request. Live tests are gated on <c>BIRKO_COSMOS_CONNECTION</c>; the no-request test needs no server.
/// </summary>
public sealed class Task837CosmosViewInListTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;
    private readonly List<Guid> _keys = new();

    public Task837CosmosViewInListTests(ITestOutputHelper output) => _output = output;

    public class Row { public Guid Key { get; set; } public string? Region { get; set; } public int Amount { get; set; } }
    public class KeyView { public Guid Key { get; set; } public string? Region { get; set; } }
    public class RegionTotalView { public string? Region { get; set; } public Guid Key { get; set; } public int Total { get; set; } }

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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task837_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid().ToString();
            var key = Guid.NewGuid();
            _keys.Add(key);
            await _rows.CreateItemAsync(new { id, Key = key, Region = i < 2 ? "EU" : "US", Amount = i }, new PartitionKey(id));
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private static ViewDefinition Keys() => new ViewDefinitionBuilder<KeyView>()
        .From<Row>()
        .Select<Row, Guid>(r => r.Key, v => v.Key)
        .Select<Row, string?>(r => r.Region, v => v.Region)
        .Build();

    private static ViewDefinition Totals() => new ViewDefinitionBuilder<RegionTotalView>()
        .From<Row>()
        .GroupBy<Row, string?>(r => r.Region)
        .Select<Row, string?>(r => r.Region, v => v.Region)
        .Sum<Row, int>(r => r.Amount, v => v.Total)
        .Build();

    public static IEnumerable<object[]> Shapes() => new[]
    {
        new object[] { "list read" }, new object[] { "array read" }, new object[] { "list count" },
        new object[] { "first" }, new object[] { "short list read" }, new object[] { "aggregate read" },
        new object[] { "aggregate count" },
    };

    private static Func<Task> Act(string shape, Container container, IReadOnlyList<Guid> known)
    {
        // Past TASK-834's measured ceiling of 13,105 Guids, and a 2-value list: both refused the same way.
        var ids = known.Concat(Enumerable.Range(0, 13_200).Select(_ => Guid.NewGuid())).ToList();
        var array = ids.ToArray();
        var few = known.Take(2).ToList();
        var keys = new CosmosViewStore<KeyView>(container, Keys());
        var totals = new CosmosViewStore<RegionTotalView>(container, Totals());
        return shape switch
        {
            "list read" => () => keys.QueryAsync(v => ids.Contains(v.Key)),
            "array read" => () => keys.QueryAsync(v => array.Contains(v.Key)),
            "list count" => () => keys.CountAsync(v => ids.Contains(v.Key)),
            "first" => () => keys.QueryFirstAsync(v => ids.Contains(v.Key)),
            "short list read" => () => keys.QueryAsync(v => few.Contains(v.Key) && v.Region == "EU"),
            "aggregate read" => () => totals.QueryAsync(v => ids.Contains(v.Key)),
            "aggregate count" => () => totals.CountAsync(v => ids.Contains(v.Key)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task A_view_IN_list_is_refused_by_name_on_the_emulator(string shape)
    {
        if (_rows == null) return;

        (await Act(shape, _rows, _keys).Should().ThrowAsync<NotSupportedException>())
            .Which.Message.Should().Contain("IN list").And.Contain("TASK-837").And.NotContain(_keys[0].ToString());
    }

    /// <summary>
    /// Before any request: the container points at a port nothing listens on, so a request would fail with a network
    /// error, never a <see cref="NotSupportedException"/>.
    /// </summary>
    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task A_view_IN_list_is_refused_before_any_request(string shape)
    {
        using var client = new CosmosClient(
            "AccountEndpoint=http://127.0.0.1:1/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;",
            new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway, RequestTimeout = TimeSpan.FromSeconds(2), MaxRetryAttemptsOnRateLimitedRequests = 0 });
        var container = client.GetContainer("db", "rows");

        await Act(shape, container, new[] { Guid.NewGuid(), Guid.NewGuid() }).Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*IN list*");
    }

    [Fact]
    public async Task A_string_Contains_is_still_a_substring_test()
    {
        if (_rows == null) return;
        var keys = new CosmosViewStore<KeyView>(_rows, Keys());

        (await keys.QueryAsync(v => v.Region!.Contains("E"))).Should().HaveCount(2);
        (await keys.CountAsync(v => v.Region!.Contains("S"))).Should().Be(1);
    }
}
