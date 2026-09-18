using System;
using Birko.Data.Models;
using Birko.Data.Sync.Models;
using Nest;

namespace Birko.Data.Sync.ElasticSearch.Models;

/// <summary>
/// Elasticsearch implementation of ISyncKnowledgeItem.
/// Extends AbstractModel for Birko.Data store compatibility.
/// Optimized for Elasticsearch document storage with field mappings.
/// </summary>
public class ElasticSyncKnowledgeItem : AbstractModel, ISyncKnowledgeItem
{
    /// <summary>
    /// Deterministic application-level key (format: {EntityGuid}_{Scope}), assigned by
    /// <see cref="Stores.AsyncElasticSyncKnowledgeStore.CreateKnowledgeItem"/> via <see cref="GenerateId"/>.
    /// NOT the Elasticsearch document id — the base store keys documents off
    /// <see cref="AbstractModel.Guid"/>. Mapped to a non-reserved "docKey" field: the previous
    /// [Text(Name = "_id")] made AutoMap emit a mapping for the reserved `_id` metadata field, which
    /// Elasticsearch rejects, so index creation threw (CR-H101). Retained (unlike the dead RecordId
    /// removed under CR-L212) because it is a deliberately-persisted, populated document field.
    /// </summary>
    [Keyword(Name = "docKey")]
    public string Id { get; set; } = string.Empty;

    // CR-L212: the int RecordId field (mapped "recordId") was removed — it was never assigned by
    // CreateKnowledgeItem, never read, always persisted as 0, and is not part of ISyncKnowledgeItem.
    // (The MongoDB sibling carries an equally-dead IdRecord "for compatibility".)

    /// <summary>
    /// GUID of the entity this knowledge refers to.
    /// Mapped as "keyword" for exact matching and aggregations.
    /// </summary>
    [Keyword(Name = "entityGuid")]
    public Guid EntityGuid { get; set; }

    /// <summary>
    /// Scope of the sync (e.g., "Products", "Orders").
    /// Mapped as "keyword" for filtering and aggregations.
    /// </summary>
    [Keyword(Name = "scope")]
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// When this item was last synchronized.
    /// Mapped as "date" for range queries.
    /// </summary>
    [Date(Name = "lastSyncedAt")]
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Version hash/timestamp from local side.
    /// </summary>
    [Keyword(Name = "localVersion")]
    public string? LocalVersion { get; set; }

    /// <summary>
    /// Version hash/timestamp from remote side.
    /// </summary>
    [Keyword(Name = "remoteVersion")]
    public string? RemoteVersion { get; set; }

    /// <summary>
    /// Whether the item was deleted locally.
    /// </summary>
    [Nest.Boolean(Name = "isLocalDeleted")]
    public bool IsLocalDeleted { get; set; }

    /// <summary>
    /// Whether the item was deleted remotely.
    /// </summary>
    [Nest.Boolean(Name = "isRemoteDeleted")]
    public bool IsRemoteDeleted { get; set; }

    /// <summary>
    /// Additional metadata (JSON serialized).
    /// Stored but not indexed for search.
    /// </summary>
    [Text(Name = "metadata", Index = false)]
    public string? Metadata { get; set; }

    /// <summary>
    /// Index name for sync knowledge documents.
    /// </summary>
    public const string IndexName = "sync-knowledge";

    /// <summary>
    /// Generates the document ID for Elasticsearch.
    /// Format: {EntityGuid}_{Scope}
    /// </summary>
    public static string GenerateId(Guid entityGuid, string scope)
    {
        return $"{entityGuid:N}_{scope}";
    }
}
