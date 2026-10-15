using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.IndexManagement;
using Birko.Data.Patterns.IndexManagement;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;
using IndexDefinition = Birko.Data.Patterns.IndexManagement.IndexDefinition;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-628 (SH-M433), live against the Cosmos emulator in gateway mode: the index manager answers by the name
/// <c>CreateAsync</c> was given (an included path or a composite), through the <c>__birko_indexes</c> registry, and
/// honours or refuses <c>Unique</c>, <c>Sparse</c> and <c>ExpireAfter</c> instead of dropping them.
/// Gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class CosmosDBIndexManagerRegistryLiveTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;

    public CosmosDBIndexManagerRegistryLiveTests(ITestOutputHelper output) => _output = output;

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
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task628_" + Guid.NewGuid().ToString("N"))).Database;
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private CosmosDBIndexManager Manager() => new(_db!);

    private async Task<string> ExistingContainer(string name = "Rows")
    {
        await _db!.CreateContainerIfNotExistsAsync(name, "/id");
        return name;
    }

    private async Task<IndexingPolicy> Policy(string container)
        => (await _db!.GetContainer(container).ReadContainerAsync()).Resource.IndexingPolicy;

    private static IndexDefinition Index(string name, params IndexField[] fields) => new() { Name = name, Fields = fields };

    // ── Names ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_single_field_index_is_found_listed_and_dropped_by_its_name()
    {
        if (_db == null) return;
        var scope = await ExistingContainer();
        var manager = Manager();

        await manager.CreateAsync(Index("IX_Code", IndexField.Ascending("Code")), scope);

        (await manager.ExistsAsync("IX_Code", scope)).Should().BeTrue("the name CreateAsync was given finds the index");
        var info = await manager.GetInfoAsync("IX_Code", scope);
        info!.Fields.Select(f => f.Name).Should().Equal("Code");
        var list = await manager.ListAsync(scope);
        list.Select(i => i.Name).Should().Contain("IX_Code").And.NotContain("/Code/?", "a registered path is listed by its name");

        await manager.DropAsync("IX_Code", scope);

        (await manager.ExistsAsync("IX_Code", scope)).Should().BeFalse();
        (await Policy(scope)).IncludedPaths.Should().NotContain(p => p.Path == "/Code/?");
    }

    [Fact]
    public async Task A_composite_index_is_found_and_dropped_by_its_name()
    {
        if (_db == null) return;
        var scope = await ExistingContainer();
        var manager = Manager();

        await manager.CreateAsync(Index("IX_AB", IndexField.Ascending("A"), IndexField.Descending("B")), scope);

        (await manager.ExistsAsync("IX_AB", scope)).Should().BeTrue();
        (await manager.GetInfoAsync("IX_AB", scope))!.Fields.Select(f => (f.Name, f.IsDescending)).Should().Equal(("A", false), ("B", true));

        await manager.DropAsync("IX_AB", scope);

        (await manager.ExistsAsync("IX_AB", scope)).Should().BeFalse();
        (await Policy(scope)).CompositeIndexes.Should().BeEmpty("the composite itself is removed, not only an included path");
    }

    [Fact]
    public async Task An_unregistered_composite_is_listed_by_its_paths_which_do_not_shift()
    {
        if (_db == null) return;
        var scope = await ExistingContainer();
        var manager = Manager();
        await manager.CreateAsync(Index("IX_First", IndexField.Ascending("A"), IndexField.Ascending("B")), scope);
        var policy = await Policy(scope);
        policy.CompositeIndexes.Add(new System.Collections.ObjectModel.Collection<CompositePath>
        {
            new() { Path = "/C", Order = CompositePathSortOrder.Ascending },
            new() { Path = "/D", Order = CompositePathSortOrder.Descending },
        });
        await manager.SetIndexingPolicyAsync(scope, policy);

        (await manager.ListAsync(scope)).Select(i => i.Name).Should().Contain("(/C ASC, /D DESC)");
        await manager.DropAsync("IX_First", scope);
        (await manager.ListAsync(scope)).Select(i => i.Name).Should().Contain("(/C ASC, /D DESC)", "removing another composite does not rename it");
    }

    [Fact]
    public async Task A_name_nothing_registered_is_answered_by_path_as_before()
    {
        if (_db == null) return;
        var scope = await ExistingContainer();
        var manager = Manager();
        var policy = await Policy(scope);
        policy.IncludedPaths.Add(new IncludedPath { Path = "/Legacy/?" });
        await manager.SetIndexingPolicyAsync(scope, policy);

        (await manager.ExistsAsync("Legacy", scope)).Should().BeTrue();
        (await manager.ListAsync(scope)).Select(i => i.Name).Should().Contain("/Legacy/?");
        (await manager.ExistsAsync("IX_Nothing", scope)).Should().BeFalse();
    }

    [Fact]
    public async Task Dropping_one_of_two_names_for_the_same_path_keeps_the_path()
    {
        if (_db == null) return;
        var scope = await ExistingContainer();
        var manager = Manager();
        await manager.CreateAsync(Index("IX_One", IndexField.Ascending("Code")), scope);
        await manager.CreateAsync(Index("IX_Two", IndexField.Ascending("Code")), scope);

        await manager.DropAsync("IX_One", scope);

        (await manager.ExistsAsync("IX_One", scope)).Should().BeFalse();
        (await manager.ExistsAsync("IX_Two", scope)).Should().BeTrue();
    }

    [Fact]
    public async Task Recreating_is_idempotent_a_changed_definition_is_refused_and_a_dropped_index_can_return()
    {
        if (_db == null) return;
        var scope = await ExistingContainer();
        var manager = Manager();
        await manager.CreateAsync(Index("IX_Code", IndexField.Ascending("Code")), scope);

        await manager.CreateAsync(Index("IX_Code", IndexField.Ascending("Code")), scope);
        await manager.Invoking(m => m.CreateAsync(Index("IX_Code", IndexField.Ascending("Other")), scope))
            .Should().ThrowAsync<IndexManagementException>();

        await manager.DropAsync("IX_Code", scope);
        await manager.CreateAsync(Index("IX_Code", IndexField.Ascending("Code")), scope);

        (await manager.ExistsAsync("IX_Code", scope)).Should().BeTrue();
        (await Policy(scope)).ExcludedPaths.Should().NotContain(p => p.Path == "/Code/?", "the drop's exclusion is lifted");
    }

    [Fact]
    public async Task The_registry_is_its_own_container_and_entity_reads_never_see_it()
    {
        if (_db == null) return;
        var scope = await ExistingContainer();
        await Manager().CreateAsync(Index("IX_Code", IndexField.Ascending("Code")), scope);

        var rows = _db.GetContainer(scope).GetItemQueryIterator<dynamic>("SELECT * FROM c");
        var count = 0;
        while (rows.HasMoreResults) count += (await rows.ReadNextAsync()).Count;
        count.Should().Be(0);

        using var entry = await _db.GetContainer(CosmosDBIndexManager.RegistryContainerName).ReadItemStreamAsync(scope, new PartitionKey(scope));
        entry.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await new StreamReader(entry.Content, Encoding.UTF8).ReadToEndAsync();
        _output.WriteLine(json);
        json.Should().Contain("IX_Code").And.Contain("/Code/?");
    }

    // ── Flags ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Why <c>Unique</c> is refused rather than mapped to a unique key: measured, a key on a container partitioned on
    /// <c>/id</c> (the only partitioning the stores support) rejects nothing, because a unique key holds within a logical
    /// partition and every document is its own. If this ever starts answering 409, the refusal can be revisited.
    /// </summary>
    [Fact]
    public async Task A_unique_key_on_an_id_partitioned_container_rejects_nothing()
    {
        if (_db == null) return;
        var properties = new ContainerProperties("Keyed", "/id");
        properties.UniqueKeyPolicy.UniqueKeys.Add(new UniqueKey { Paths = { "/Code" } });
        var container = (await _db.CreateContainerAsync(properties)).Container;

        using var first = await container.CreateItemStreamAsync(Doc("a", "X"), new PartitionKey("a"));
        using var second = await container.CreateItemStreamAsync(Doc("b", "X"), new PartitionKey("b"));

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created, "the unique key does not reach across partitions");
    }

    private static MemoryStream Doc(string id, string code)
        => new(Encoding.UTF8.GetBytes($"{{\"id\":\"{id}\",\"Code\":\"{code}\"}}"));

    [Fact]
    public async Task Unique_is_refused_before_anything_is_written()
    {
        if (_db == null) return;
        var scope = await ExistingContainer();
        var manager = Manager();

        (await manager.Invoking(m => m.CreateAsync(new IndexDefinition { Name = "UX_Code", Fields = new[] { IndexField.Ascending("Code") }, Unique = true }, scope))
            .Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("/id");

        (await manager.ExistsAsync("UX_Code", scope)).Should().BeFalse();
        (await Policy(scope)).IncludedPaths.Should().NotContain(p => p.Path == "/Code/?");
    }

    public static IEnumerable<object[]> Refused() => new[]
    {
        new object[] { new IndexDefinition { Name = "IX", Fields = new[] { IndexField.Ascending("Code") }, Sparse = true } },
        new object[] { new IndexDefinition { Name = "IX", Fields = new[] { IndexField.Ascending("Code") }, ExpireAfter = TimeSpan.FromHours(1) } },
        new object[] { new IndexDefinition { Name = "IX", Fields = new[] { IndexField.Text("Code") } } },
        new object[] { new IndexDefinition { Name = "IX", Fields = new[] { IndexField.Descending("Code") } } },
        new object[] { new IndexDefinition { Name = "IX", Fields = new[] { IndexField.Ascending("Code") }, Properties = new Dictionary<string, object> { ["x"] = 1 } } },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_flag_cosmos_cannot_express_is_refused_and_nothing_is_written(IndexDefinition definition)
    {
        if (_db == null) return;
        var scope = await ExistingContainer();
        var before = (await Policy(scope)).IncludedPaths.Select(p => p.Path).ToList();

        await Manager().Invoking(m => m.CreateAsync(definition, scope)).Should().ThrowAsync<NotSupportedException>();

        (await Policy(scope)).IncludedPaths.Select(p => p.Path).Should().Equal(before);
        (await Manager().ExistsAsync("IX", scope)).Should().BeFalse();
    }
}
