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

    /// <summary>
    /// Optional tenant this knowledge belongs to. Null means tenant-agnostic. Used to scope
    /// read/delete/last-sync operations so one tenant's sync knowledge is never returned, deleted, or
    /// overwritten by another (CR-H100).
    /// </summary>
    public Guid? TenantId { get; set; }

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
    /// Builds a <see cref="CosmosSyncKnowledgeItem"/> from any <see cref="ISyncKnowledgeItem"/>, stamping
    /// the tenant. Shared by the sync and async stores so the 14-line mapping can't drift between them
    /// (CR-L211) — this is the mapper that changes whenever the model gains a field (e.g. TenantId).
    /// </summary>
    /// <remarks>
    /// If <paramref name="item"/> is already a <see cref="CosmosSyncKnowledgeItem"/> it is returned as-is
    /// (an explicit <paramref name="tenantId"/> wins over whatever it already carried); otherwise a fresh
    /// row is materialized. The Guid is populated when null so the downstream <c>Guid!.Value</c>
    /// dereferences in Update/SetLastSyncTime are provably safe (mirrors the base store's <c>??=</c>) —
    /// see CR-H100 (tenant scoping) and CR-M158 (null-Guid pass-through).
    /// </remarks>
    internal static CosmosSyncKnowledgeItem FromInterface(ISyncKnowledgeItem item, Guid? tenantId)
    {
        if (item is CosmosSyncKnowledgeItem cosmosItem)
        {
            if (tenantId.HasValue)
            {
                cosmosItem.TenantId = tenantId;
            }
            // System.Guid is fully qualified: the inherited instance property `Guid` shadows the type
            // name in this static method's expression context.
            cosmosItem.Guid ??= System.Guid.NewGuid();
            return cosmosItem;
        }

        return new CosmosSyncKnowledgeItem
        {
            Guid = item.Guid ?? System.Guid.NewGuid(),
            EntityGuid = item.EntityGuid,
            TenantId = tenantId,
            Scope = item.Scope,
            LastSyncedAt = item.LastSyncedAt,
            LocalVersion = item.LocalVersion,
            RemoteVersion = item.RemoteVersion,
            IsLocalDeleted = item.IsLocalDeleted,
            IsRemoteDeleted = item.IsRemoteDeleted,
            Metadata = item.Metadata
        };
    }

    /// <summary>
    /// Returns a string representation for debugging.
    /// </summary>
    public override string ToString()
    {
        return $"CosmosSyncKnowledgeItem: {EntityGuid} | {Scope}";
    }
}
