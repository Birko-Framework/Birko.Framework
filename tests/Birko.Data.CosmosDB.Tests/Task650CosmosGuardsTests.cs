using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.IndexManagement;
using Birko.Data.CosmosDB.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-650, two Cosmos DB guards. (1) An empty <c>PartitionKeyPath</c> passed validation as "the default" but reached
/// container creation as "", which only <c>null</c> fell back from. (2) For a name nothing registered, the index manager's
/// verbs disagreed: <c>ExistsAsync</c> answered true for a composite ELEMENT while <c>GetInfoAsync</c> found nothing and
/// <c>DropAsync</c> removed nothing; and <c>ExistsAsync</c>, a read, created the registry container. Live parts gated on
/// <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class Task650CosmosGuardsTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;

    public Task650CosmosGuardsTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        var conn = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
        {
            _output.WriteLine("SKIPPED (live parts): no live Cosmos. Set BIRKO_COSMOS_CONNECTION.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_COSMOS_CONNECTION is not.");
            return;
        }
        _client = new CosmosClient(conn, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task650_" + Guid.NewGuid().ToString("N"))).Database;
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/id")]
    public void The_container_is_partitioned_on_id_whenever_validation_accepts_the_path(string? path)
    {
        var settings = new Settings { PartitionKeyPath = path! }; // null is what an unset configuration value binds to

        settings.RequireAddressablePartitionKeyPath();
        settings.EffectivePartitionKeyPath.Should().Be("/id");
    }

    [Fact]
    public async Task A_container_on_an_empty_partition_path_is_refused_by_Cosmos()
    {
        // Why "" must not reach ContainerProperties: Cosmos refuses it (the store's creation used to send it).
        if (_db == null) return;
        var act = () => _db.CreateContainerIfNotExistsAsync(new ContainerProperties("EmptyPath", ""));

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task An_unregistered_composite_element_is_not_an_index_on_any_verb()
    {
        if (_db == null) return;
        var properties = new ContainerProperties("Rows", "/id");
        properties.IndexingPolicy.CompositeIndexes.Add(new Collection<CompositePath>
        {
            new() { Path = "/A" },
            new() { Path = "/B" },
        });
        await _db.CreateContainerIfNotExistsAsync(properties);
        var manager = new CosmosDBIndexManager(_db);

        (await manager.ExistsAsync("A", "Rows")).Should().BeFalse("GetInfoAsync and DropAsync know no index called A");
        (await manager.GetInfoAsync("A", "Rows")).Should().BeNull();
        await manager.DropAsync("A", "Rows");
        (await _db.GetContainer("Rows").ReadContainerAsync()).Resource.IndexingPolicy.CompositeIndexes.Should().ContainSingle("nothing was dropped");
    }

    [Fact]
    public async Task ExistsAsync_does_not_create_the_registry_container()
    {
        if (_db == null) return;
        await _db.CreateContainerIfNotExistsAsync("Rows", "/id");

        (await new CosmosDBIndexManager(_db).ExistsAsync("Code", "Rows")).Should().BeFalse();

        var registry = await _db.GetContainer(CosmosDBIndexManager.RegistryContainerName).ReadContainerStreamAsync();
        registry.StatusCode.Should().Be(HttpStatusCode.NotFound, "a read must not create the registry");
    }
}
