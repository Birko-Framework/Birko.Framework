using Birko.Data.Sync.Models;
using Birko.Data.Sync.RavenDB.Models;
using Birko.Data.Sync.RavenDB.Stores;
using Birko.Data.Sync.Stores;
using Birko.Data.Tenant.Models;
using FluentAssertions;
using System;
using System.Reflection;
using Xunit;

namespace Birko.Data.Sync.RavenDB.Tests;

/// <summary>
/// Offline tests for the RavenDB sync-knowledge model and stores. TASK-630 replaced the stores' own tenant API with the
/// sync engine's knowledge-store interfaces; the live round trip is <see cref="RavenSyncKnowledgeLiveTests"/>.
/// </summary>
public class RavenSyncKnowledgeItemTests
{
    [Fact]
    public void The_stores_implement_the_engines_knowledge_store_interfaces()
    {
        typeof(IAsyncSyncKnowledgeItemStore<RavenSyncKnowledgeItem>).IsAssignableFrom(typeof(AsyncRavenSyncKnowledgeStore)).Should().BeTrue();
        typeof(ISyncKnowledgeItemStore<RavenSyncKnowledgeItem>).IsAssignableFrom(typeof(RavenSyncKnowledgeStore)).Should().BeTrue();
    }

    [Fact]
    public void The_model_carries_no_tenant_of_its_own()
    {
        // TASK-630: a tenant's knowledge is kept apart by the tenant provider's tenant-qualified scope.
        typeof(ITenant).IsAssignableFrom(typeof(RavenSyncKnowledgeItem)).Should().BeFalse();
        typeof(ISyncKnowledgeItem).IsAssignableFrom(typeof(RavenSyncKnowledgeItem)).Should().BeTrue();
    }

    [Fact]
    public void CreateKnowledgeItem_records_both_versions_and_absence_flags()
    {
        var store = new AsyncRavenSyncKnowledgeStore("http://localhost:1", "unused");
        var entity = Guid.NewGuid();

        var item = store.CreateKnowledgeItem(entity, "local", null, new SyncOptions { Scope = "Products" });

        item.EntityGuid.Should().Be(entity);
        item.Scope.Should().Be("Products");
        item.LocalVersion.Should().Be("local");
        item.RemoteVersion.Should().BeNull();
        item.IsLocalDeleted.Should().BeFalse();
        item.IsRemoteDeleted.Should().BeTrue();
        item.Guid.Should().NotBeNull();
    }

    [Fact]
    public void Model_HasNoDeadMembers()
    {
        // CR-L219: InternalRecordId (dead int, "for database compatibility"). CR-L218: CollectionName
        // const + GenerateDocumentId helper (both dead — RavenDB resolves the collection from the type
        // name and identity comes from AbstractModel.Guid). Assert all three are gone.
        var t = typeof(RavenSyncKnowledgeItem);
        t.GetProperty("InternalRecordId", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull("dead InternalRecordId was removed under CR-L219");
        t.GetField("CollectionName", BindingFlags.Public | BindingFlags.Static)
            .Should().BeNull("dead CollectionName const was removed under CR-L218");
        t.GetMethod("GenerateDocumentId", BindingFlags.Public | BindingFlags.Static)
            .Should().BeNull("dead GenerateDocumentId helper was removed under CR-L218");
    }
}
