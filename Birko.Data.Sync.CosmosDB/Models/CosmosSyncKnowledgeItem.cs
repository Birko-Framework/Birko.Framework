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
    // The int InternalRecordId field ("for database compatibility") was removed — never set, never
    // read, serialized a meaningless InternalRecordId:0 into every document (the Cosmos analogue of
    // RavenDB's CR-L219). Existing documents carrying the property still deserialize: unmapped JSON
    // members are ignored by the Cosmos serializer.

    /// <summary>
    /// GUID of the entity this knowledge refers to.
    /// </summary>
    public Guid EntityGuid { get; set; }

    // TASK-630: TenantGuid removed; a tenant's knowledge is kept apart by the tenant provider's tenant-qualified scope.

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

    // TASK-630 (SH-M436): the FromInterface conversion (which kept a foreign Guid.Empty as a key) was removed with the
    // tenant API; the engine creates and upserts this type directly.

    public override string ToString()
    {
        return $"CosmosSyncKnowledgeItem: {EntityGuid} | {Scope}";
    }
}
