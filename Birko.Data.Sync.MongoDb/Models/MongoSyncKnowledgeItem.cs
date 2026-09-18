using System;
using Birko.Data.Models;
using Birko.Data.Sync.Models;
using MongoDB.Bson.Serialization.Attributes;

namespace Birko.Data.Sync.MongoDb.Models;

/// <summary>
/// MongoDB implementation of ISyncKnowledgeItem.
/// Extends AbstractModel for Birko.Data store compatibility.
/// Optimized for MongoDB document storage.
/// </summary>
/// <remarks>
/// CR-L216: <c>[BsonIgnoreExtraElements]</c> lets documents written before the dead <c>IdRecord</c>
/// field was dropped — which carry a legacy <c>recordId</c> element — deserialize cleanly. The MongoDB
/// driver throws on an unmapped element by default, and the Birko Mongo layer registers no
/// IgnoreExtraElements convention, so removing a previously-persisted element without this attribute
/// would break reads of existing data.
/// </remarks>
[BsonIgnoreExtraElements]
public class MongoSyncKnowledgeItem : AbstractModel, ISyncKnowledgeItem
{
    /// <summary>
    /// MongoDB document identifier.
    /// </summary>
    [BsonId]
    [global::MongoDB.Bson.Serialization.Attributes.BsonRepresentation(global::MongoDB.Bson.BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    // CR-L216: the int IdRecord field (BsonElement "recordId", "for compatibility") was removed — dead
    // surface never assigned by CreateKnowledgeItem and never queried, inflating every persisted document
    // with a recordId:0. Identity is the AbstractModel.Guid (ModelByGuid filters on x.Guid) plus the
    // Mongo-generated _id.

    /// <summary>
    /// GUID of the entity this knowledge refers to.
    /// </summary>
    [BsonElement("entityGuid")]
    [global::MongoDB.Bson.Serialization.Attributes.BsonRepresentation(global::MongoDB.Bson.BsonType.Binary)]
    public Guid EntityGuid { get; set; }

    /// <summary>
    /// Scope of the sync (e.g., "Products", "Orders").
    /// </summary>
    [BsonElement("scope")]
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// When this item was last synchronized.
    /// </summary>
    [BsonElement("lastSyncedAt")]
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Version hash/timestamp from local side.
    /// </summary>
    [BsonElement("localVersion")]
    public string? LocalVersion { get; set; }

    /// <summary>
    /// Version hash/timestamp from remote side.
    /// </summary>
    [BsonElement("remoteVersion")]
    public string? RemoteVersion { get; set; }

    /// <summary>
    /// Whether the item was deleted locally.
    /// </summary>
    [BsonElement("isLocalDeleted")]
    public bool IsLocalDeleted { get; set; }

    /// <summary>
    /// Whether the item was deleted remotely.
    /// </summary>
    [BsonElement("isRemoteDeleted")]
    public bool IsRemoteDeleted { get; set; }

    /// <summary>
    /// Additional metadata (JSON serialized).
    /// </summary>
    [BsonElement("metadata")]
    public string? Metadata { get; set; }

    // CR-L215: the decorative `CollectionName => "SyncKnowledge"` property was removed. It never
    // affected where documents live — the base store resolves the collection via
    // MongoDBClient.GetCollection<T>() → `collectionName ?? typeof(T).Name`, so these documents live in
    // a collection named "MongoSyncKnowledgeItem". The property was purely misleading (and the CLAUDE.md
    // claim that it targets the collection was wrong). Wiring it through was deliberately not done: that
    // would silently relocate existing data from "MongoSyncKnowledgeItem" to "SyncKnowledge". Contrast
    // CosmosSyncKnowledgeItem.ContainerName, which IS honored because it is passed to the base ctor.
}
