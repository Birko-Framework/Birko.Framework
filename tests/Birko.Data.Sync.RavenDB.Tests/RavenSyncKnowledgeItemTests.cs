using Birko.Data.Sync.Models;
using Birko.Data.Sync.RavenDB.Models;
using Birko.Data.Sync.RavenDB.Stores;
using Birko.Data.Tenant.Models;
using FluentAssertions;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Birko.Data.Sync.RavenDB.Tests;

/// <summary>
/// Offline tests for the RavenDB sync-knowledge model. Building this project compile-verifies the
/// CR-C19 (tenant queries filter on the canonical <see cref="ITenant.TenantGuid"/>) and CR-C20
/// (async delete uses the tracked entity) fixes in the store classes.
///
/// The store query/delete behavior itself needs a live RavenDB server and is not exercised here —
/// that is the documented infra gap for this backend.
/// </summary>
public class RavenSyncKnowledgeItemTests
{
    [Fact]
    public void ImplementsCanonicalTenantAndSyncKnowledgeAbstractions()
    {
        var item = new RavenSyncKnowledgeItem();
        item.Should().BeAssignableTo<ITenant>();
        item.Should().BeAssignableTo<ISyncKnowledgeItem>();
    }

    [Fact]
    public void Tenant_RoundTrips()
    {
        var tenant = Guid.NewGuid();
        var item = new RavenSyncKnowledgeItem
        {
            EntityGuid = Guid.NewGuid(),
            Scope = "Products",
            TenantGuid = tenant,
            TenantName = "Acme"
        };

        item.TenantGuid.Should().Be(tenant);
        item.TenantName.Should().Be("Acme");
    }

    [Fact]
    public void TenantGuid_DefaultsToEmpty_ForSingleTenantKnowledge()
    {
        // Guid.Empty is what ModelByTenant treats as "no tenant filter".
        var item = new RavenSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "Products" };

        item.TenantGuid.Should().Be(Guid.Empty);
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

    [Fact]
    public void SyncStore_MethodsDropTheMisleadingAsyncSuffix()
    {
        // CR-L217: the synchronous RavenSyncKnowledgeStore returned Dictionary/void/DateTime? from
        // methods named *Async. They were renamed to their true synchronous names (mirroring the
        // CosmosDB sync sibling). Assert the *Async names are gone and the plain names exist.
        var t = typeof(RavenSyncKnowledgeStore);
        var names = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name).ToList();

        names.Should().Contain(new[]
        {
            "GetKnowledge", "GetKnowledgeItem", "UpdateKnowledge", "UpdateKnowledgeItem",
            "DeleteKnowledge", "GetLastSyncTime", "SetLastSyncTime"
        });
        names.Should().NotContain(n => n.EndsWith("KnowledgeAsync") || n.EndsWith("SyncTimeAsync"),
            "the synchronous sync store must not carry a misleading Async suffix");
    }
}
