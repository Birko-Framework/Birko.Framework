using System;
using Birko.Data.Sync.ElasticSearch.Models;
using Birko.Data.Sync.ElasticSearch.Stores;
using Birko.Data.Sync.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.ElasticSearch.Tests;

/// <summary>
/// CR-M161: pure-logic coverage for the ES sync store — GenerateId formatting and CreateKnowledgeItem
/// (id derivation + IsLocalDeleted/IsRemoteDeleted from empty hashes). No live Elasticsearch needed;
/// the store has a parameterless ctor and CreateKnowledgeItem touches no client.
/// </summary>
public class ElasticSyncGenerateIdAndCreateItemTests
{
    [Fact]
    public void GenerateId_FormatsEntityGuidAndScope()
    {
        var guid = Guid.NewGuid();

        ElasticSyncKnowledgeItem.GenerateId(guid, "Products")
            .Should().Be($"{guid:N}_Products");
    }

    [Fact]
    public void CreateKnowledgeItem_BothHashesPresent_NotDeleted()
    {
        var store = new AsyncElasticSyncKnowledgeStore();
        var guid = Guid.NewGuid();

        var item = store.CreateKnowledgeItem(guid, "lhash", "rhash", new SyncOptions { Scope = "Products" });

        item.EntityGuid.Should().Be(guid);
        item.Scope.Should().Be("Products");
        item.Id.Should().Be(ElasticSyncKnowledgeItem.GenerateId(guid, "Products"));
        item.LocalVersion.Should().Be("lhash");
        item.RemoteVersion.Should().Be("rhash");
        item.IsLocalDeleted.Should().BeFalse();
        item.IsRemoteDeleted.Should().BeFalse();
        item.LastSyncedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(null, "rhash", true, false)]
    [InlineData("", "rhash", true, false)]
    [InlineData("lhash", null, false, true)]
    [InlineData("lhash", "", false, true)]
    [InlineData(null, null, true, true)]
    public void CreateKnowledgeItem_DerivesDeletionFlagsFromEmptyHashes(
        string? local, string? remote, bool expectLocalDeleted, bool expectRemoteDeleted)
    {
        var store = new AsyncElasticSyncKnowledgeStore();

        var item = store.CreateKnowledgeItem(Guid.NewGuid(), local, remote, new SyncOptions { Scope = "S" });

        item.IsLocalDeleted.Should().Be(expectLocalDeleted);
        item.IsRemoteDeleted.Should().Be(expectRemoteDeleted);
    }
}
