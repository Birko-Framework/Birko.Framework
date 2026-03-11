using Birko.Data.Sync.Models;
using Birko.Data.Models;

namespace Birko.Data.Sync.RavenDB.Models;

/// <summary>
/// RavenDB implementation of ISyncKnowledgeItem extending AbstractModel
/// Optimized for RavenDB document storage
/// </summary>
public class RavenSyncKnowledgeItem : AbstractModel, ISyncKnowledgeItem
{
    /// <summary>
    /// Internal record ID for database compatibility
    /// </summary>
    public int InternalRecordId { get; set; }

    /// <summary>
    /// GUID of the entity this knowledge refers to
    /// </summary>
    public Guid EntityGuid { get; set; }

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

    /// <summary>
    /// RavenDB collection name for this document type
    /// </summary>
    public const string CollectionName = "RavenSyncKnowledgeItems";

    /// <summary>
    /// Generates the document ID for RavenDB
    /// Format: SyncKnowledge/{EntityGuid}/{Scope}
    /// </summary>
    public static string GenerateDocumentId(Guid entityGuid, string scope)
    {
        return $"SyncKnowledge/{entityGuid:N}/{scope}";
    }

    /// <summary>
    /// Returns a string representation for debugging
    /// </summary>
    public override string ToString()
    {
        return $"RavenSyncKnowledgeItem: {EntityGuid} | {Scope}";
    }
}
