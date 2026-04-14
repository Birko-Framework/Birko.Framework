using System;
using Birko.Data.Models;
using Birko.Data.Sync.Models;
using System.Xml.Serialization;

namespace Birko.Data.Sync.Xml.Models;

/// <summary>
/// XML implementation of ISyncKnowledgeItem.
/// Extends AbstractModel for Birko.Data store compatibility.
/// Optimized for XML serialization with System.Xml.Serialization.
/// </summary>
[XmlRoot("SyncKnowledgeItem")]
public class XmlSyncKnowledgeItem : AbstractModel, ISyncKnowledgeItem
{
    /// <summary>
    /// Unique identifier for the sync knowledge record.
    /// </summary>
    [XmlElement("Id")]
    public int Id { get; set; }

    /// <summary>
    /// GUID of the entity this knowledge refers to.
    /// </summary>
    [XmlElement("EntityGuid")]
    public Guid EntityGuid { get; set; }

    /// <summary>
    /// Scope of the sync (e.g., "Products", "Orders").
    /// </summary>
    [XmlElement("Scope")]
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// When this item was last synchronized.
    /// </summary>
    [XmlElement("LastSyncedAt")]
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Version hash/timestamp from local side.
    /// </summary>
    [XmlElement("LocalVersion")]
    public string? LocalVersion { get; set; }

    /// <summary>
    /// Version hash/timestamp from remote side.
    /// </summary>
    [XmlElement("RemoteVersion")]
    public string? RemoteVersion { get; set; }

    /// <summary>
    /// Whether the item was deleted locally.
    /// </summary>
    [XmlElement("IsLocalDeleted")]
    public bool IsLocalDeleted { get; set; }

    /// <summary>
    /// Whether the item was deleted remotely.
    /// </summary>
    [XmlElement("IsRemoteDeleted")]
    public bool IsRemoteDeleted { get; set; }

    /// <summary>
    /// Additional metadata (XML serialized).
    /// </summary>
    [XmlElement("Metadata")]
    public string? Metadata { get; set; }
}
