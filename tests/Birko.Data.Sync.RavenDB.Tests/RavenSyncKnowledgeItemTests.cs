using Birko.Data.Sync.Models;
using Birko.Data.Sync.RavenDB.Models;
using Birko.Data.Tenant.Models;
using FluentAssertions;
using System;
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
    public void GenerateDocumentId_UsesEntityAndScope()
    {
        var entity = Guid.NewGuid();
        RavenSyncKnowledgeItem.GenerateDocumentId(entity, "Orders")
            .Should().Be($"SyncKnowledge/{entity:N}/Orders");
    }
}
