using System;
using Birko.Data.Sync.CosmosDB.Models;
using Birko.Data.Sync.CosmosDB.Stores;
using Birko.Data.Sync.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.CosmosDB.Tests;

/// <summary>
/// CR-H100: the Cosmos sync knowledge store accepted a tenantId on every read/delete/last-sync
/// method but never used it, and the model had no tenant field — so operations leaked/clobbered
/// across tenants. The model now carries TenantId, ConvertToCosmosItem stamps it, and the queries
/// filter on it. The LINQ filter itself needs a live Cosmos DB, so these offline tests pin the model
/// round-trip and the tenant-stamping conversion. (Filter behavior is an integration/infra check.)
/// </summary>
public class CosmosSyncTenantScopingTests
{
    private sealed class PlainKnowledge : ISyncKnowledgeItem
    {
        public Guid? Guid { get; set; }
        public Guid EntityGuid { get; set; }
        public string Scope { get; set; } = string.Empty;
        public DateTime LastSyncedAt { get; set; }
        public string? LocalVersion { get; set; }
        public string? RemoteVersion { get; set; }
        public bool IsLocalDeleted { get; set; }
        public bool IsRemoteDeleted { get; set; }
        public string? Metadata { get; set; }
    }

    [Fact]
    public void Model_CarriesTenantId()
    {
        var tenant = Guid.NewGuid();
        var item = new CosmosSyncKnowledgeItem { TenantId = tenant };
        item.TenantId.Should().Be(tenant);
    }

    [Fact]
    public void ConvertToCosmosItem_StampsTenant_OnPlainItem()
    {
        var tenant = Guid.NewGuid();
        var src = new PlainKnowledge { EntityGuid = Guid.NewGuid(), Scope = "Products" };

        var result = CosmosSyncKnowledgeStore.ConvertToCosmosItem(src, tenant);

        result.TenantId.Should().Be(tenant);
        result.Scope.Should().Be("Products");
    }

    [Fact]
    public void ConvertToCosmosItem_NullTenant_LeavesTenantNull()
    {
        var src = new PlainKnowledge { EntityGuid = Guid.NewGuid(), Scope = "Products" };

        var result = CosmosSyncKnowledgeStore.ConvertToCosmosItem(src, null);

        result.TenantId.Should().BeNull();
    }

    [Fact]
    public void ConvertToCosmosItem_ExplicitTenant_OverridesExistingCosmosItemTenant()
    {
        var original = Guid.NewGuid();
        var newTenant = Guid.NewGuid();
        var existing = new CosmosSyncKnowledgeItem { Guid = Guid.NewGuid(), TenantId = original };

        var result = CosmosSyncKnowledgeStore.ConvertToCosmosItem(existing, newTenant);

        result.TenantId.Should().Be(newTenant);
    }

    // ── CR-M158: the pass-through (already-CosmosSyncKnowledgeItem) branch must populate a null Guid
    // so the downstream `Guid!.Value` in Update/SetLastSyncTime is provably safe. ──

    [Fact]
    public void ConvertToCosmosItem_Sync_ExistingItemWithNullGuid_GetsGuidAssigned()
    {
        var existing = new CosmosSyncKnowledgeItem { Guid = null, EntityGuid = Guid.NewGuid(), Scope = "S" };

        var result = CosmosSyncKnowledgeStore.ConvertToCosmosItem(existing, null);

        result.Guid.Should().NotBeNull();
    }

    [Fact]
    public void ConvertToCosmosItem_Async_ExistingItemWithNullGuid_GetsGuidAssigned()
    {
        var existing = new CosmosSyncKnowledgeItem { Guid = null, EntityGuid = Guid.NewGuid(), Scope = "S" };

        var result = AsyncCosmosSyncKnowledgeStore.ConvertToCosmosItem(existing, null);

        result.Guid.Should().NotBeNull();
    }

    [Fact]
    public void ConvertToCosmosItem_ExistingItemWithGuid_PreservesIt()
    {
        var g = Guid.NewGuid();
        var existing = new CosmosSyncKnowledgeItem { Guid = g };

        CosmosSyncKnowledgeStore.ConvertToCosmosItem(existing, null).Guid.Should().Be(g);
    }
}
