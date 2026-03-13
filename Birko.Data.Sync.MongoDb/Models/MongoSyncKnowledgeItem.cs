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
public class MongoSyncKnowledgeItem : AbstractModel, ISyncKnowledgeItem
{
    /// <summary>
    /// MongoDB document identifier.
    /// </summary>
    [BsonId]
    [global::MongoDB.Bson.Serialization.Attributes.BsonRepresentation(global::MongoDB.Bson.BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier for the sync knowledge record (for compatibility).
    /// </summary>
    [BsonElement("recordId")]
    public int IdRecord { get; set; }

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

    /// <summary>
    /// MongoDB index hint for optimized queries.
    /// </summary>
    [BsonIgnore]
    public string CollectionName => "SyncKnowledge";
}
