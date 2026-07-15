using Birko.Data.Sync.Models;
using Birko.Data.Models;
using Birko.Data.Tenant.Models;

namespace Birko.Data.Sync.RavenDB.Models;

/// <summary>
/// RavenDB implementation of ISyncKnowledgeItem extending AbstractModel.
/// Also implements <see cref="ITenant"/> so tenant-aware sync knowledge (produced by
/// TenantSyncProvider as ITenantSyncKnowledgeItem) is persisted and queryable by tenant.
/// Optimized for RavenDB document storage
/// </summary>
public class RavenSyncKnowledgeItem : AbstractModel, ISyncKnowledgeItem, ITenant
{
    // CR-L219: the int InternalRecordId field ("for database compatibility") was removed — never set,
    // read, or used by either store; it only added a meaningless int to every RavenDB document.
    // Identity is AbstractModel.Guid (the base store keys documents off it; re-syncs derive a
    // deterministic Guid via AsyncRavenSyncKnowledgeStore.DeterministicGuid).

    /// <summary>
    /// GUID of the entity this knowledge refers to
    /// </summary>
    public Guid EntityGuid { get; set; }

    /// <summary>
    /// Tenant this knowledge item belongs to (canonical <see cref="ITenant"/> member). Used to scope
    /// tenant-aware queries; <see cref="Guid.Empty"/> for single-tenant / tenant-agnostic knowledge,
    /// which ModelByTenant treats as "no tenant filter". (CR-C19: tenant scoping previously filtered
    /// on the record's own random Guid, which could never match.)
    /// </summary>
    public Guid TenantGuid { get; set; }

    /// <summary>
    /// Optional tenant display name (canonical <see cref="ITenant"/> member).
    /// </summary>
    public string? TenantName { get; set; }

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
    // and document identity comes from AbstractModel.Guid (deterministic on re-sync via
    // AsyncRavenSyncKnowledgeStore.DeterministicGuid), not a string id built here.

    /// <summary>
    /// Returns a string representation for debugging
    /// </summary>
    public override string ToString()
    {
        return $"RavenSyncKnowledgeItem: {EntityGuid} | {Scope}";
    }
}
