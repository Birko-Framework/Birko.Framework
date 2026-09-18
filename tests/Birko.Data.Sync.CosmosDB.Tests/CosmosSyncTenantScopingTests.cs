using System;
using Birko.Data.Sync.CosmosDB.Models;
using Birko.Data.Sync.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.CosmosDB.Tests;

/// <summary>
/// CR-H100: the Cosmos sync knowledge store accepted a tenantId on every read/delete/last-sync
/// method but never used it, and the model had no tenant field — so operations leaked/clobbered
/// across tenants. The model now carries TenantId, the conversion factory stamps it, and the queries
/// filter on it. The LINQ filter itself needs a live Cosmos DB, so these offline tests pin the model
/// round-trip and the tenant-stamping conversion. (Filter behavior is an integration/infra check.)
///
/// CR-L211: the per-store <c>ConvertToCosmosItem</c> copies were consolidated into a single shared
/// factory <see cref="CosmosSyncKnowledgeItem.FromInterface"/>, so the mapping can't drift between the
/// sync and async stores. These tests now exercise that one factory.
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
    public void Model_HasNoDeadInternalRecordId()
    {
        // Audit-gap extra alongside CR-L225: the dead int InternalRecordId ("for database
        // compatibility") was removed — never set/read, serialized InternalRecordId:0 into every
        // document (the Cosmos analogue of RavenDB's CR-L219). Guards against reintroduction.
        typeof(CosmosSyncKnowledgeItem).GetProperty("InternalRecordId").Should().BeNull();
    }

    [Fact]
    public void Model_CarriesTenantId()
    {
        var tenant = Guid.NewGuid();
        var item = new CosmosSyncKnowledgeItem { TenantId = tenant };
        item.TenantId.Should().Be(tenant);
    }

    [Fact]
    public void FromInterface_StampsTenant_OnPlainItem()
    {
        var tenant = Guid.NewGuid();
        var src = new PlainKnowledge { EntityGuid = Guid.NewGuid(), Scope = "Products" };

        var result = CosmosSyncKnowledgeItem.FromInterface(src, tenant);

        result.TenantId.Should().Be(tenant);
        result.Scope.Should().Be("Products");
    }

    [Fact]
    public void FromInterface_NullTenant_LeavesTenantNull()
    {
        var src = new PlainKnowledge { EntityGuid = Guid.NewGuid(), Scope = "Products" };

        var result = CosmosSyncKnowledgeItem.FromInterface(src, null);

        result.TenantId.Should().BeNull();
    }

    [Fact]
    public void FromInterface_ExplicitTenant_OverridesExistingCosmosItemTenant()
    {
        var original = Guid.NewGuid();
        var newTenant = Guid.NewGuid();
        var existing = new CosmosSyncKnowledgeItem { Guid = Guid.NewGuid(), TenantId = original };

        var result = CosmosSyncKnowledgeItem.FromInterface(existing, newTenant);

        result.TenantId.Should().Be(newTenant);
    }

    // ── CR-M158: the pass-through (already-CosmosSyncKnowledgeItem) branch must populate a null Guid
    // so the downstream `Guid!.Value` in Update/SetLastSyncTime is provably safe. CR-L211 folded the
    // former sync + async copies into this one factory, so a single test now covers both paths. ──

    [Fact]
    public void FromInterface_ExistingItemWithNullGuid_GetsGuidAssigned()
    {
        var existing = new CosmosSyncKnowledgeItem { Guid = null, EntityGuid = Guid.NewGuid(), Scope = "S" };

        var result = CosmosSyncKnowledgeItem.FromInterface(existing, null);

        result.Guid.Should().NotBeNull();
    }

    [Fact]
    public void FromInterface_ExistingItemWithGuid_PreservesIt()
    {
        var g = Guid.NewGuid();
        var existing = new CosmosSyncKnowledgeItem { Guid = g };

        CosmosSyncKnowledgeItem.FromInterface(existing, null).Guid.Should().Be(g);
    }

    [Fact]
    public void FromInterface_PlainItem_CopiesAllFields()
    {
        var entity = Guid.NewGuid();
        var when = new DateTime(2026, 7, 14, 8, 30, 0, DateTimeKind.Utc);
        var src = new PlainKnowledge
        {
            EntityGuid = entity,
            Scope = "Orders",
            LastSyncedAt = when,
            LocalVersion = "lv1",
            RemoteVersion = "rv1",
            IsLocalDeleted = true,
            IsRemoteDeleted = false,
            Metadata = "{\"k\":1}"
        };

        var result = CosmosSyncKnowledgeItem.FromInterface(src, null);

        result.EntityGuid.Should().Be(entity);
        result.Scope.Should().Be("Orders");
        result.LastSyncedAt.Should().Be(when);
        result.LocalVersion.Should().Be("lv1");
        result.RemoteVersion.Should().Be("rv1");
        result.IsLocalDeleted.Should().BeTrue();
        result.IsRemoteDeleted.Should().BeFalse();
        result.Metadata.Should().Be("{\"k\":1}");
        result.Guid.Should().NotBeNull("a plain item with no Guid is assigned a fresh one");
    }
}
