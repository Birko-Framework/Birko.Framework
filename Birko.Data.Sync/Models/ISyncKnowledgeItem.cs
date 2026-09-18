using System;

namespace Birko.Data.Sync.Models;

/// <summary>
/// Interface for synchronization metadata for an entity
/// Concrete implementations are provided by underlying stores (SQL, JSON, MongoDB, etc.)
/// </summary>
public interface ISyncKnowledgeItem
{
    /// <summary>
    /// Unique identifier for the sync knowledge record
    /// </summary>
    Guid? Guid { get; set; }

    /// <summary>
    /// GUID of the entity this knowledge refers to
    /// </summary>
    Guid EntityGuid { get; set; }

    /// <summary>
    /// Scope of the sync (e.g., "Products", "Orders")
    /// </summary>
    string Scope { get; set; }

    /// <summary>
    /// When this item was last synchronized
    /// </summary>
    DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Version hash/timestamp from local side
    /// </summary>
    string? LocalVersion { get; set; }

    /// <summary>
    /// Version hash/timestamp from remote side
    /// </summary>
    string? RemoteVersion { get; set; }

    /// <summary>
    /// Whether the item was deleted locally
    /// </summary>
    bool IsLocalDeleted { get; set; }

    /// <summary>
    /// Whether the item was deleted remotely
    /// </summary>
    bool IsRemoteDeleted { get; set; }

    /// <summary>
    /// Additional metadata (JSON serialized)
    /// </summary>
    string? Metadata { get; set; }
}
