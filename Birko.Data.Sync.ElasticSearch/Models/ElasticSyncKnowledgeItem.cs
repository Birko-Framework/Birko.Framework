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
    /// Elasticsearch document identifier.
    /// Format: {EntityGuid}_{Scope}
    /// </summary>
    [Text(Name = "_id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier for the sync knowledge record.
    /// </summary>
    [Number(NumberType.Integer, Name = "recordId")]
    public int RecordId { get; set; }

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
