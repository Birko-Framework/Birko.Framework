using System;
using Birko.Data.Models;
using Birko.Data.Sync.Models;
using System.Text.Json.Serialization;

namespace Birko.Data.Sync.Json.Models;

/// <summary>
/// JSON implementation of ISyncKnowledgeItem.
/// Extends AbstractModel for Birko.Data store compatibility.
/// Optimized for JSON serialization with System.Text.Json.
/// </summary>
public class JsonSyncKnowledgeItem : AbstractModel, ISyncKnowledgeItem
{
    // CR-L213: the int Id field (serialized "id") was removed — it was dead state. The store keys
    // entities exclusively by AbstractModel.Guid (AsyncJsonStore loads/saves by item.Guid;
    // CreateKnowledgeItem sets Guid = Guid.NewGuid()), and neither ISyncKnowledgeItem nor the sync
    // provider ever read or wrote it, so it defaulted to 0 in every persisted record. Identity is
    // already covered by Guid / EntityGuid.

    /// <summary>
    /// GUID of the entity this knowledge refers to.
    /// </summary>
    [JsonPropertyName("entityGuid")]
    public Guid EntityGuid { get; set; }

    /// <summary>
    /// Scope of the sync (e.g., "Products", "Orders").
    /// </summary>
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// When this item was last synchronized.
    /// </summary>
    [JsonPropertyName("lastSyncedAt")]
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Version hash/timestamp from local side.
    /// </summary>
    [JsonPropertyName("localVersion")]
    public string? LocalVersion { get; set; }

    /// <summary>
    /// Version hash/timestamp from remote side.
    /// </summary>
    [JsonPropertyName("remoteVersion")]
    public string? RemoteVersion { get; set; }

    /// <summary>
    /// Whether the item was deleted locally.
    /// </summary>
    [JsonPropertyName("isLocalDeleted")]
    public bool IsLocalDeleted { get; set; }

    /// <summary>
    /// Whether the item was deleted remotely.
    /// </summary>
    [JsonPropertyName("isRemoteDeleted")]
    public bool IsRemoteDeleted { get; set; }

    /// <summary>
    /// Additional metadata (JSON serialized).
    /// </summary>
    [JsonPropertyName("metadata")]
    public string? Metadata { get; set; }
}
