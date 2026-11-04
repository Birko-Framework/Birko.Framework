using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Migrations.CosmosDB.Context;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.CosmosDB.Tests;

/// <summary>
/// TASK-722, live on the emulator. Measured before the fix: <c>RenameField</c> and <c>UpdateDocuments</c> built their patch
/// paths as <c>/{name}</c>, unescaped, so a field named <c>a/b</c> was the nested path a → b — the rename wrote the new
/// field and its refused remove of the old one was swallowed, leaving both; the update was refused with 400. Escaping as a
/// JSON pointer (<c>~1</c>, as <c>DropField</c> did) did not help: the emulator stored the escape literally, as a field named
/// <c>a~1b</c>. So a name holding <c>/</c> or <c>~</c> is refused, before anything is read or written.
/// </summary>
public sealed class Task722PatchPathLiveTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;

    public Task722PatchPathLiveTests(ITestOutputHelper output) => _output = output;

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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task722_" + Guid.NewGuid().ToString("N"))).Database;
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private async Task<Container> Seeded()
    {
        var container = (await _db!.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        await container.CreateItemAsync(new Dictionary<string, object?> { ["id"] = "1", ["a/b"] = 7, ["plain"] = 1 }, new PartitionKey("1"));
        return container;
    }

    private async Task<JObject> Only(Container container)
    {
        var iterator = container.GetItemQueryIterator<JObject>("SELECT * FROM c");
        var all = new List<JObject>();
        while (iterator.HasMoreResults) all.AddRange(await iterator.ReadNextAsync());
        return all.Single();
    }

    private static void Untouched(JObject doc)
    {
        doc.Value<int>("a/b").Should().Be(7);
        doc.ContainsKey("a").Should().BeFalse();
        doc.Properties().Select(p => p.Name).Should().NotContain(n => n.Contains('~'));
    }

    [Fact]
    public async Task RenameField_refuses_a_name_a_patch_path_cannot_hold()
    {
        if (_db == null) return;
        var container = await Seeded();

        FluentActions.Invoking(() => new CosmosDBSchemaBuilder(_db).RenameField("Rows", "a/b", "c")).Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => new CosmosDBSchemaBuilder(_db).RenameField("Rows", "plain", "c~d")).Should().Throw<NotSupportedException>();

        var doc = await Only(container);
        Untouched(doc);
        doc.Value<int>("plain").Should().Be(1);
    }

    [Fact]
    public async Task UpdateDocuments_and_DropField_refuse_a_name_a_patch_path_cannot_hold()
    {
        if (_db == null) return;
        var container = await Seeded();

        FluentActions.Invoking(() => new CosmosDBDataMigrator(_db).UpdateDocuments("Rows", "{\"id\":\"1\"}", new Dictionary<string, object> { ["a/b"] = 8 }))
            .Should().Throw<NotSupportedException>();
        FluentActions.Invoking(() => new CosmosDBSchemaBuilder(_db).DropField("Rows", "a/b")).Should().Throw<NotSupportedException>();

        Untouched(await Only(container));
    }

    [Fact]
    public async Task A_plain_name_still_renames()
    {
        if (_db == null) return;
        var container = await Seeded();

        new CosmosDBSchemaBuilder(_db).RenameField("Rows", "plain", "renamed");

        var doc = await Only(container);
        doc.ContainsKey("plain").Should().BeFalse();
        doc.Value<int>("renamed").Should().Be(1);
    }
}
