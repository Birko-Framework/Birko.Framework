using System;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.RavenDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.RavenDB.Tests;

/// <summary>
/// CR-H103: ConvertToRavenItem assigned a brand-new random Guid when the incoming item had none, so
/// re-syncing the same (EntityGuid, Scope) stored a NEW document each time (duplicates) instead of
/// upserting. The Guid is now derived deterministically from the natural key, so the base store —
/// which keys document identity off Guid — overwrites the same document on re-sync.
/// </summary>
public class RavenSyncKnowledgeUpsertTests
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
    public void NullGuidItem_GetsDeterministicId_ForSameNaturalKey()
    {
        var entity = Guid.NewGuid();
        var a = AsyncRavenSyncKnowledgeStore.ConvertToRavenItem(
            new PlainKnowledge { EntityGuid = entity, Scope = "Products" });
        var b = AsyncRavenSyncKnowledgeStore.ConvertToRavenItem(
            new PlainKnowledge { EntityGuid = entity, Scope = "Products" });

        a.Guid.Should().NotBeNull().And.NotBe(Guid.Empty);
        a.Guid.Should().Be(b.Guid, "the same natural key must map to the same document id (upsert)");
    }

    [Fact]
    public void DifferentScope_ProducesDifferentId()
    {
        var entity = Guid.NewGuid();
        var products = AsyncRavenSyncKnowledgeStore.ConvertToRavenItem(
            new PlainKnowledge { EntityGuid = entity, Scope = "Products" });
        var orders = AsyncRavenSyncKnowledgeStore.ConvertToRavenItem(
            new PlainKnowledge { EntityGuid = entity, Scope = "Orders" });

        products.Guid!.Value.Should().NotBe(orders.Guid!.Value);
    }

    [Fact]
    public void ExplicitGuidIsPreserved()
    {
        var explicitGuid = Guid.NewGuid();
        var item = AsyncRavenSyncKnowledgeStore.ConvertToRavenItem(
            new PlainKnowledge { Guid = explicitGuid, EntityGuid = Guid.NewGuid(), Scope = "Products" });

        item.Guid.Should().Be(explicitGuid);
    }

    [Fact]
    public void DeterministicGuid_IsStable()
    {
        var entity = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        AsyncRavenSyncKnowledgeStore.DeterministicGuid(entity, "S", tenant)
            .Should().Be(AsyncRavenSyncKnowledgeStore.DeterministicGuid(entity, "S", tenant));
    }
}
