using System;
using System.Linq;
using Birko.Data.Sync.CosmosDB.Models;
using Birko.Data.Sync.CosmosDB.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Xunit;

namespace Birko.Data.Sync.CosmosDB.Tests;

/// <summary>
/// Offline tests for the Cosmos DB sync-knowledge model and stores. TASK-630 replaced the stores' own tenant API (and its
/// tenant query, SH-H013, and conversion path, SH-M436) with the sync engine's knowledge-store interfaces. Nothing here
/// performs I/O: <c>GetContainer</c> is a proxy and <c>ToQueryDefinition()</c> renders locally. There is no Cosmos
/// emulator in the live suite, so the round trip itself is not run against a server.
/// </summary>
public class CosmosSyncKnowledgeStoreTests
{
    private static Container OfflineContainer()
    {
        const string connectionString =
            "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM"
            + "+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        return new CosmosClient(connectionString).GetContainer("db", CosmosSyncKnowledgeItem.ContainerName);
    }

    [Fact]
    public void The_stores_implement_the_engines_knowledge_store_interfaces()
    {
        typeof(IAsyncSyncKnowledgeItemStore<CosmosSyncKnowledgeItem>).IsAssignableFrom(typeof(AsyncCosmosSyncKnowledgeStore)).Should().BeTrue();
        typeof(ISyncKnowledgeItemStore<CosmosSyncKnowledgeItem>).IsAssignableFrom(typeof(CosmosSyncKnowledgeStore)).Should().BeTrue();
    }

    [Fact]
    public void The_model_carries_no_tenant_of_its_own()
    {
        // TASK-630: a tenant's knowledge is kept apart by the tenant provider's tenant-qualified scope.
        typeof(CosmosSyncKnowledgeItem).GetProperty("TenantGuid").Should().BeNull();
        typeof(CosmosSyncKnowledgeItem).GetProperty("InternalRecordId").Should().BeNull();
    }

    [Fact]
    public void CreateKnowledgeItem_records_both_versions_and_a_fresh_key()
    {
        var store = new AsyncCosmosSyncKnowledgeStore(OfflineContainer());
        var entity = Guid.NewGuid();

        var item = store.CreateKnowledgeItem(entity, null, "remote", new SyncOptions { Scope = "Products" });

        item.EntityGuid.Should().Be(entity);
        item.Scope.Should().Be("Products");
        item.RemoteVersion.Should().Be("remote");
        item.IsLocalDeleted.Should().BeTrue();
        item.IsRemoteDeleted.Should().BeFalse();
        item.Guid.Should().NotBeNull().And.NotBe(Guid.Empty, "SH-M436: never Guid.Empty as a key");
    }

    [Fact]
    public void The_scope_read_renders_a_scope_term_only()
    {
        var sql = OfflineContainer().GetItemLinqQueryable<CosmosSyncKnowledgeItem>()
            .Where(x => x.Scope == "Products@tenant:abc")
            .ToQueryDefinition().QueryText;

        sql.Should().Contain("Scope").And.Contain("Products@tenant:abc").And.NotContain("TenantGuid");
    }
}
