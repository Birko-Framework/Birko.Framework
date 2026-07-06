using Birko.Data.Sync.RavenDB.Models;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.Sync.RavenDB.Tests;

/// <summary>
/// Offline tests for the RavenDB sync-knowledge model. Building this project compile-verifies the
/// CR-C19 (tenant filter now on <see cref="RavenSyncKnowledgeItem.TenantGuid"/>) and CR-C20
/// (async delete uses the tracked entity) fixes in the store classes.
///
/// The store query/delete behavior itself needs a live RavenDB server and is not exercised here —
/// that is the documented infra gap for this backend.
/// </summary>
public class RavenSyncKnowledgeItemTests
{
    [Fact]
    public void TenantGuid_RoundTrips()
    {
        var tenant = Guid.NewGuid();
        var item = new RavenSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "Products", TenantGuid = tenant };

        item.TenantGuid.Should().Be(tenant);
    }

    [Fact]
    public void TenantGuid_DefaultsToNull_ForSingleTenantKnowledge()
    {
        var item = new RavenSyncKnowledgeItem { EntityGuid = Guid.NewGuid(), Scope = "Products" };

        item.TenantGuid.Should().BeNull();
    }

    [Fact]
    public void GenerateDocumentId_UsesEntityAndScope()
    {
        var entity = Guid.NewGuid();
        RavenSyncKnowledgeItem.GenerateDocumentId(entity, "Orders")
            .Should().Be($"SyncKnowledge/{entity:N}/Orders");
    }
}
