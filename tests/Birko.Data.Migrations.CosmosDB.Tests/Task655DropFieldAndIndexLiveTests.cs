using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.IndexManagement;
using Birko.Data.Migrations.CosmosDB.Context;
using Birko.Data.Patterns.Schema;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.CosmosDB.Tests;

/// <summary>
/// TASK-655, live against the Cosmos emulator: <c>DropField</c> removes the field from every document (it only excluded
/// the path from indexing, so every document kept it), and <c>DropIndex</c> drops the index <c>CreateIndex</c> created
/// (it was a no-op). Gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class Task655DropFieldAndIndexLiveTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;

    public Task655DropFieldAndIndexLiveTests(ITestOutputHelper output) => _output = output;

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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task655_" + Guid.NewGuid().ToString("N"))).Database;
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private async Task<List<JObject>> All(Container container)
    {
        var all = new List<JObject>();
        var iterator = container.GetItemQueryIterator<JObject>("SELECT * FROM c");
        while (iterator.HasMoreResults) all.AddRange(await iterator.ReadNextAsync());
        return all;
    }

    private static bool HasSecret(JObject d) => d.ContainsKey("Secret");

    [Fact]
    public async Task DropField_removes_the_field_from_every_document_in_its_partition()
    {
        if (_db == null) return;
        var container = (await _db.CreateContainerIfNotExistsAsync("Rows", "/Tenant")).Container;
        await container.CreateItemAsync(new Dictionary<string, object?> { ["id"] = "1", ["Tenant"] = "a", ["Secret"] = "x", ["Keep"] = 1 }, new PartitionKey("a"));
        await container.CreateItemAsync(new Dictionary<string, object?> { ["id"] = "2", ["Tenant"] = "b", ["Secret"] = null, ["Keep"] = 2 }, new PartitionKey("b"));
        await container.CreateItemAsync(new Dictionary<string, object?> { ["id"] = "3", ["Tenant"] = "b", ["Keep"] = 3 }, new PartitionKey("b"));

        new CosmosDBSchemaBuilder(_db).DropField("Rows", "Secret");

        var all = await All(container);
        all.Should().HaveCount(3);
        all.Where(HasSecret).Should().BeEmpty("the field is gone from every document, a null one included");
        all.Select(d => d.Value<int>("Keep")).Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task DropIndex_drops_the_index_CreateIndex_created()
    {
        if (_db == null) return;
        var schema = new CosmosDBSchemaBuilder(_db);
        schema.CreateCollection("Orders").WithField("id", FieldType.String, isPrimary: true).WithField("Code", FieldType.String).Build();
        schema.CreateIndex("Orders", "IX_Code").WithField("Code").Build();
        var manager = new CosmosDBIndexManager(_db);
        (await manager.ExistsAsync("IX_Code", "Orders")).Should().BeTrue();

        schema.DropIndex("Orders", "IX_Code");

        (await manager.ExistsAsync("IX_Code", "Orders")).Should().BeFalse();
    }
}
