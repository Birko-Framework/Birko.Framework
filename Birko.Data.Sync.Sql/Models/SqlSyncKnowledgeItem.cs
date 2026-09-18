using System;
using Birko.Data.Models;
using Birko.Data.Sync.Models;

namespace Birko.Data.Sync.Sql.Models;

/// <summary>
/// SQL implementation of ISyncKnowledgeItem.
/// Extends AbstractModel for Birko.Data.SQL store compatibility.
/// Maps to the "SyncKnowledge" table via Birko.Data.SQL attributes.
/// </summary>
[Birko.Data.SQL.Attributes.Table("SyncKnowledge")]
public class SqlSyncKnowledgeItem : AbstractModel, ISyncKnowledgeItem
{
    /// <summary>
    /// Sync knowledge record GUID (primary key, from AbstractModel).
    /// </summary>
    [Birko.Data.SQL.Attributes.PrimaryField]
    [Birko.Data.SQL.Attributes.NamedField("Guid")]
    public override Guid? Guid { get; set; }

    /// <summary>
    /// Auto-increment row identifier for database indexing.
    /// </summary>
    [Birko.Data.SQL.Attributes.IncrementField]
    [Birko.Data.SQL.Attributes.NamedField("Id")]
    public int Id { get; set; }

    /// <summary>
    /// GUID of the entity this knowledge refers to.
    /// </summary>
    [Birko.Data.SQL.Attributes.RequiredField]
    [Birko.Data.SQL.Attributes.NamedField("EntityGuid")]
    public Guid EntityGuid { get; set; }

    /// <summary>
    /// Scope of the sync (e.g., "Products", "Orders").
    /// </summary>
    [Birko.Data.SQL.Attributes.RequiredField]
    [Birko.Data.SQL.Attributes.MaxLengthField(100)]
    [Birko.Data.SQL.Attributes.NamedField("Scope")]
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// When this item was last synchronized.
    /// </summary>
    [Birko.Data.SQL.Attributes.RequiredField]
    [Birko.Data.SQL.Attributes.NamedField("LastSyncedAt")]
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Version hash/timestamp from local side.
    /// </summary>
    [Birko.Data.SQL.Attributes.MaxLengthField(100)]
    [Birko.Data.SQL.Attributes.NamedField("LocalVersion")]
    public string? LocalVersion { get; set; }

    /// <summary>
    /// Version hash/timestamp from remote side.
    /// </summary>
    [Birko.Data.SQL.Attributes.MaxLengthField(100)]
    [Birko.Data.SQL.Attributes.NamedField("RemoteVersion")]
    public string? RemoteVersion { get; set; }

    /// <summary>
    /// Whether the item was deleted locally.
    /// </summary>
    [Birko.Data.SQL.Attributes.RequiredField]
    [Birko.Data.SQL.Attributes.NamedField("IsLocalDeleted")]
    public bool IsLocalDeleted { get; set; }

    /// <summary>
    /// Whether the item was deleted remotely.
    /// </summary>
    [Birko.Data.SQL.Attributes.RequiredField]
    [Birko.Data.SQL.Attributes.NamedField("IsRemoteDeleted")]
    public bool IsRemoteDeleted { get; set; }

    /// <summary>
    /// Additional metadata (JSON serialized).
    /// </summary>
    [Birko.Data.SQL.Attributes.NamedField("Metadata")]
    public string? Metadata { get; set; }
}
