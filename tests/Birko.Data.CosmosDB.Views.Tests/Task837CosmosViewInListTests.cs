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
/// not reachable through a view and there is nothing to chunk. TASK-837 refused the IN list by name, on every length.
/// <b>Inverted by TASK-839</b>: a collection Contains is now <c>ARRAY_CONTAINS</c>, and the shapes these refusal theories
/// covered (list, array, count, first, a short list, an aggregate view) are asserted against the oracle, split or
/// refused past the limit, in <see cref="Task839CosmosViewContainsTests"/>. What stays here is the string Contains pin.
/// Live tests are gated on <c>BIRKO_COSMOS_CONNECTION</c>.
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

    [Fact]
    public async Task A_string_Contains_is_still_a_substring_test()
    {
        if (_rows == null) return;
        var keys = new CosmosViewStore<KeyView>(_rows, Keys());

        (await keys.QueryAsync(v => v.Region!.Contains("E"))).Should().HaveCount(2);
        (await keys.CountAsync(v => v.Region!.Contains("S"))).Should().Be(1);
    }
}
