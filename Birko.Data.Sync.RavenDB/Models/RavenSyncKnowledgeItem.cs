using Birko.Data.Sync.Models;
using Birko.Data.Models;

namespace Birko.Data.Sync.RavenDB.Models;

/// <summary>
/// RavenDB implementation of ISyncKnowledgeItem extending AbstractModel.
/// Optimized for RavenDB document storage
/// </summary>
public class RavenSyncKnowledgeItem : AbstractModel, ISyncKnowledgeItem
{
    // CR-L219: the int InternalRecordId field ("for database compatibility") was removed — never set,
    // read, or used by either store; it only added a meaningless int to every RavenDB document.
    // Identity is AbstractModel.Guid (the base store keys documents off it).

    /// <summary>
    /// GUID of the entity this knowledge refers to
    /// </summary>
    public Guid EntityGuid { get; set; }

    // TASK-630: TenantGuid / TenantName (ITenant) removed; a tenant's knowledge is kept apart by the tenant provider's
    // tenant-qualified scope, and the engine upserts by Guid (no DeterministicGuid any more).

    private string _scope = string.Empty;

    /// <summary>
    /// Scope of the sync (e.g., "Products", "Orders")
    /// </summary>
    public string Scope
    {
        get => _scope;
        set => _scope = value ?? string.Empty;
    }

    /// <summary>
    /// When this item was last synchronized
    /// </summary>
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Version hash/timestamp from local side
    /// </summary>
    public string? LocalVersion { get; set; }

    /// <summary>
    /// Version hash/timestamp from remote side
    /// </summary>
    public string? RemoteVersion { get; set; }

    /// <summary>
    /// Whether the item was deleted locally
    /// </summary>
    public bool IsLocalDeleted { get; set; }

    /// <summary>
    /// Whether the item was deleted remotely
    /// </summary>
    public bool IsRemoteDeleted { get; set; }

    /// <summary>
    /// Additional metadata (JSON serialized)
    /// </summary>
    public string? Metadata { get; set; }

    // CR-L218: the CollectionName const and GenerateDocumentId helper were removed — both dead. RavenDB
    // resolves the collection from the type name (the base store registers no CollectionName convention),
    // and document identity comes from AbstractModel.Guid , not a string id built here.

    /// <summary>
    /// Returns a string representation for debugging
    /// </summary>
    public override string ToString()
    {
        return $"RavenSyncKnowledgeItem: {EntityGuid} | {Scope}";
    }
}
