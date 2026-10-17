using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Serialization;
using Birko.Data.Migrations.CosmosDB.Context;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.CosmosDB.Tests;

/// <summary>
/// TASK-659, live against the Cosmos emulator: the Cosmos migration steps read and write the same documents whichever
/// serializer the caller's <see cref="CosmosClient"/> uses: the SDK's default (Newtonsoft), or Birko's
/// <see cref="CosmosGuidIdSerializer"/> (System.Text.Json). They typed results and values as <c>JsonElement</c>, which
/// only the second can carry. Gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class Task659SerializerIndependenceLiveTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly List<(CosmosClient Client, Database Db)> _dbs = new();
    private string? _conn;

    public Task659SerializerIndependenceLiveTests(ITestOutputHelper output) => _output = output;

    public Task InitializeAsync()
    {
        _conn = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
        if (string.IsNullOrWhiteSpace(_conn))
        {
            _output.WriteLine("SKIPPED: no live Cosmos. Set BIRKO_COSMOS_CONNECTION.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_COSMOS_CONNECTION is not.");
        }
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var (client, db) in _dbs)
        {
            try { await db.DeleteAsync(); } catch { /* best-effort */ }
            client.Dispose();
        }
    }

    public static TheoryData<string> Serializers => new() { "newtonsoft", "system.text.json" };

    private async Task<(Database Db, Container Rows)> Fresh(string serializer)
    {
        var options = new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway };
        if (serializer == "system.text.json") options.Serializer = new CosmosGuidIdSerializer();
        var client = new CosmosClient(_conn, options);
        var db = (await client.CreateDatabaseIfNotExistsAsync("birko_task659_" + Guid.NewGuid().ToString("N"))).Database;
        _dbs.Add((client, db));
        var rows = (await db.CreateContainerIfNotExistsAsync("Rows", "/Tenant")).Container;
        foreach (var doc in new[] { Doc("1", "a"), Doc("2", "b") })
        {
            await rows.CreateItemStreamAsync(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(doc.ToString())), new PartitionKey("t"));
        }
        return (db, rows);
    }

    private static JObject Doc(string id, string name) => JObject.Parse(
        $"{{\"id\":\"{id}\",\"Tenant\":\"t\",\"Name\":\"{name}\",\"Amount\":1.25,\"Nested\":{{\"X\":1,\"Tags\":[\"p\",\"q\"],\"Deep\":{{\"Ok\":true}}}}}}");

    private static async Task<Dictionary<string, JObject>> All(Container container)
    {
        var all = new Dictionary<string, JObject>();
        using var iterator = container.GetItemQueryStreamIterator("SELECT * FROM c");
        while (iterator.HasMoreResults)
        {
            using var page = await iterator.ReadNextAsync();
            using var reader = new System.IO.StreamReader(page.Content);
            foreach (JObject d in (JArray)JObject.Parse(await reader.ReadToEndAsync())["Documents"]!)
            {
                foreach (var system in new[] { "_rid", "_self", "_etag", "_attachments", "_ts" }) d.Remove(system);
                all[(string)d["id"]!] = d;
            }
        }
        return all;
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public async Task RenameField_moves_a_nested_value_exactly(string serializer)
    {
        if (string.IsNullOrWhiteSpace(_conn)) return;
        var (db, rows) = await Fresh(serializer);

        new CosmosDBSchemaBuilder(db).RenameField("Rows", "Nested", "Moved");

        var doc = (await All(rows))["1"];
        doc.ContainsKey("Nested").Should().BeFalse();
        JToken.DeepEquals(doc["Moved"], Doc("1", "a")["Nested"]).Should().BeTrue($"the value moved exactly, got {doc["Moved"]}");
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public async Task UpdateDocuments_and_DeleteDocuments_act_on_the_matching_documents(string serializer)
    {
        if (string.IsNullOrWhiteSpace(_conn)) return;
        var (db, rows) = await Fresh(serializer);
        var migrator = new CosmosDBDataMigrator(db);

        migrator.UpdateDocuments("Rows", "{\"Name\":\"a\"}", new Dictionary<string, object?> { ["Flag"] = true });
        migrator.DeleteDocuments("Rows", "{\"Name\":\"b\"}");

        var all = await All(rows);
        all.Keys.Should().Equal("1");
        ((bool?)all["1"]["Flag"]).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Serializers))]
    public async Task CopyData_copies_every_document_exactly(string serializer)
    {
        if (string.IsNullOrWhiteSpace(_conn)) return;
        var (db, rows) = await Fresh(serializer);
        var copies = (await db.CreateContainerIfNotExistsAsync("Copies", "/Tenant")).Container;

        new CosmosDBDataMigrator(db).CopyData("Rows", "Copies");

        var source = await All(rows);
        var target = await All(copies);
        target.Keys.Should().BeEquivalentTo(source.Keys);
        foreach (var id in source.Keys)
            JToken.DeepEquals(target[id], source[id]).Should().BeTrue($"document {id} copied exactly, got {target[id]}");
    }
}
