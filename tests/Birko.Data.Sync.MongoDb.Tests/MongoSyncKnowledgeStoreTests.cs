using System;
using System.Reflection;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.MongoDb.Models;
using Birko.Data.Sync.MongoDb.Stores;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Xunit;

namespace Birko.Data.Sync.MongoDb.Tests;

/// <summary>
/// CR-H102: the MongoDB sync store had no test project. CreateKnowledgeItem is pure (no I/O) and is
/// covered here; GetLastSyncTimeAsync/SetLastSyncTimeAsync route through the concrete Mongo store and
/// require a live MongoDB (documented integration gap).
/// </summary>
public class MongoSyncKnowledgeStoreTests
{
    private static AsyncMongoSyncKnowledgeStore NewStore() => new();

    [Fact]
    public void CreateKnowledgeItem_MapsScopeAndEntityAndVersions()
    {
        var store = NewStore();
        var entity = Guid.NewGuid();
        var options = new SyncOptions { Scope = "Products" };

        var item = store.CreateKnowledgeItem(entity, "localhash", "remotehash", options);

        item.EntityGuid.Should().Be(entity);
        item.Scope.Should().Be("Products");
        item.LocalVersion.Should().Be("localhash");
        item.RemoteVersion.Should().Be("remotehash");
        item.Guid.Should().NotBeNull().And.NotBe(Guid.Empty);
        item.IsLocalDeleted.Should().BeFalse();
        item.IsRemoteDeleted.Should().BeFalse();
    }

    [Fact]
    public void CreateKnowledgeItem_EmptyLocalHash_MarksLocalDeleted()
    {
        var store = NewStore();
        var item = store.CreateKnowledgeItem(Guid.NewGuid(), null, "remotehash", new SyncOptions { Scope = "S" });

        item.IsLocalDeleted.Should().BeTrue();
        item.IsRemoteDeleted.Should().BeFalse();
    }

    [Fact]
    public void CreateKnowledgeItem_EmptyRemoteHash_MarksRemoteDeleted()
    {
        var store = NewStore();
        var item = store.CreateKnowledgeItem(Guid.NewGuid(), "localhash", "", new SyncOptions { Scope = "S" });

        item.IsLocalDeleted.Should().BeFalse();
        item.IsRemoteDeleted.Should().BeTrue();
    }

    [Fact]
    public void Model_HasNoDeadFields()
    {
        // CR-L216: the int IdRecord field (BsonElement "recordId") was dead — never assigned/queried,
        // inflating every document with recordId:0. CR-L215: the decorative CollectionName property
        // (=> "SyncKnowledge") never affected the collection name (base store uses typeof(T).Name).
        // Assert both are gone so a reintroduction is caught.
        var t = typeof(MongoSyncKnowledgeItem);
        t.GetProperty("IdRecord", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull("the dead IdRecord field was removed under CR-L216");
        t.GetProperty("CollectionName", BindingFlags.Public | BindingFlags.Instance)
            .Should().BeNull("the decorative CollectionName property was removed under CR-L215");
    }

    [Fact]
    public void Deserialize_LegacyDocumentWithRecordId_DoesNotThrow()
    {
        // CR-L216: dropping the persisted IdRecord field would break reads of documents already written
        // with a "recordId" element — the driver throws on an unmapped element and no IgnoreExtraElements
        // convention is registered. [BsonIgnoreExtraElements] on the model makes such reads safe. Built by
        // hand (not via ToBsonDocument) to avoid the driver's global GuidRepresentation config.
        var doc = new BsonDocument
        {
            { "_id", ObjectId.GenerateNewId() },
            { "scope", "Products" },
            { "recordId", 0 } // legacy element that is no longer a mapped property
        };

        var act = () => BsonSerializer.Deserialize<MongoSyncKnowledgeItem>(doc);

        act.Should().NotThrow();
        BsonSerializer.Deserialize<MongoSyncKnowledgeItem>(doc).Scope.Should().Be("Products");
    }
}
