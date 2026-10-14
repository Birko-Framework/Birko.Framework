using System;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

public class TenantDoc : AbstractModel
{
    public string? TenantId { get; set; }
    public string? Name { get; set; }
}

/// <summary>
/// TASK-614. The stores address every document with <c>new PartitionKey(guid)</c>, which is only the document's
/// partition key value when the container is partitioned on <c>/id</c>. <c>Settings.PartitionKeyPath</c> nevertheless
/// accepted any path, and the docs showed <c>/tenantId</c>. The container was then created on it, and every write
/// failed. A path the store cannot address is refused when it is configured.
/// </summary>
public class PartitionKeyPathTests
{
    private static Settings With(string path) => new() { Location = "AccountEndpoint=http://localhost:1/;AccountKey=a2V5", Name = "db", PartitionKeyPath = path };

    [Fact]
    public void The_async_store_refuses_a_partition_key_path_it_cannot_address()
        => ((Action)(() => new AsyncCosmosDBStore<TenantDoc>().SetSettings(With("/tenantId"))))
            .Should().Throw<NotSupportedException>().WithMessage("*PartitionKeyPath*/tenantId*");

    [Fact]
    public void The_sync_store_refuses_a_partition_key_path_it_cannot_address()
        => ((Action)(() => new CosmosDBStore<TenantDoc>().SetSettings(With("/tenantId"))))
            .Should().Throw<NotSupportedException>().WithMessage("*PartitionKeyPath*/tenantId*");

    [Fact]
    public void Both_constructors_taking_settings_refuse_it_too()
    {
        ((Action)(() => new AsyncCosmosDBStore<TenantDoc>("AccountEndpoint=http://localhost:1/;AccountKey=a2V5", "db", null, With("/tenantId"))))
            .Should().Throw<NotSupportedException>();
        ((Action)(() => new CosmosDBStore<TenantDoc>("AccountEndpoint=http://localhost:1/;AccountKey=a2V5", "db", null, With("/tenantId"))))
            .Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void The_default_path_is_accepted()
        => ((Action)(() => new AsyncCosmosDBStore<TenantDoc>().SetSettings(With("/id")))).Should().NotThrow();
}

/// <summary>
/// TASK-614, live: a container that already exists with another partition key (created elsewhere) is refused at
/// initialisation, before any write. Measured: the SDK's <c>CreateContainerIfNotExistsAsync</c> already throws
/// <c>ArgumentException</c> naming both paths, so no framework check is needed. This pins that it stays that way.
/// Gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class PartitionKeyPathLiveTests
{
    private readonly ITestOutputHelper _output;
    public PartitionKeyPathLiveTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task An_existing_container_partitioned_on_another_path_is_refused_at_init()
    {
        var conn = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
        {
            _output.WriteLine("SKIPPED: no live Cosmos. Set BIRKO_COSMOS_CONNECTION.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_COSMOS_CONNECTION is not.");
            return;
        }

        var dbName = "birko_pk614_" + Guid.NewGuid().ToString("N");
        var options = new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway };
        using var raw = new CosmosClient(conn, options);
        var db = (await raw.CreateDatabaseIfNotExistsAsync(dbName)).Database;
        try
        {
            await db.CreateContainerIfNotExistsAsync(nameof(TenantDoc), "/tenantId");
            var store = new AsyncCosmosDBStore<TenantDoc>(conn, dbName, null, new Settings { ConnectionMode = ConnectionMode.Gateway });

            var create = () => store.CreateAsync(new TenantDoc { TenantId = "t1", Name = "a" });

            (await create.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("'/tenantId'").And.Contain("'/id'");
        }
        finally
        {
            try { await db.DeleteAsync(); } catch { /* best-effort */ }
        }
    }
}
