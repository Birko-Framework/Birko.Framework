using Birko.Data.Sync.Models;
using Birko.Data.Models;
using System;

namespace Birko.Data.Sync.CosmosDB.Models;

/// <summary>
/// Cosmos DB implementation of ISyncKnowledgeItem extending AbstractModel.
/// Optimized for Cosmos DB document storage with partition key support.
/// </summary>
public class CosmosSyncKnowledgeItem : AbstractModel, ISyncKnowledgeItem
{
    /// <summary>
    /// Internal record ID for database compatibility.
    /// </summary>
    public int InternalRecordId { get; set; }

    /// <summary>
    /// GUID of the entity this knowledge refers to.
    /// </summary>
    public Guid EntityGuid { get; set; }

    private string _scope = string.Empty;

    /// <summary>
    /// Scope of the sync (e.g., "Products", "Orders").
    /// </summary>
    public string Scope
    {
        get => _scope;
        set => _scope = value ?? string.Empty;
    }

    /// <summary>
    /// When this item was last synchronized.
    /// </summary>
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Version hash/timestamp from local side.
    /// </summary>
    public string? LocalVersion { get; set; }

    /// <summary>
    /// Version hash/timestamp from remote side.
    /// </summary>
    public string? RemoteVersion { get; set; }

    /// <summary>
    /// Whether the item was deleted locally.
    /// </summary>
    public bool IsLocalDeleted { get; set; }

    /// <summary>
    /// Whether the item was deleted remotely.
    /// </summary>
    public bool IsRemoteDeleted { get; set; }

    /// <summary>
    /// Additional metadata (JSON serialized).
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Default container name for sync knowledge items.
    /// </summary>
    public const string ContainerName = "SyncKnowledge";

    /// <summary>
    /// Returns a string representation for debugging.
    /// </summary>
    public override string ToString()
    {
        return $"CosmosSyncKnowledgeItem: {EntityGuid} | {Scope}";
    }
}
