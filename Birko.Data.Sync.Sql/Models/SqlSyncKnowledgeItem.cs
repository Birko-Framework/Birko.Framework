using System;
using Birko.Data.Sync.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Birko.Data.Sync.Sql.Models;

/// <summary>
/// SQL implementation of ISyncKnowledgeItem
/// Optimized for relational database storage with Entity Framework or ADO.NET
/// </summary>
[Table("SyncKnowledge")]
public class SqlSyncKnowledgeItem : ISyncKnowledgeItem
{
    /// <summary>
    /// Sync knowledge record GUID
    /// </summary>
    public Guid? Guid { get; set; }

    /// <summary>
    /// Unique identifier for the sync knowledge record (primary key)
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// GUID of the entity this knowledge refers to
    /// </summary>
    [Required]
    [MaxLength(36)]
    public Guid EntityGuid { get; set; }

    /// <summary>
    /// Scope of the sync (e.g., "Products", "Orders")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// When this item was last synchronized
    /// </summary>
    [Required]
    public DateTime LastSyncedAt { get; set; }

    /// <summary>
    /// Version hash/timestamp from local side
    /// </summary>
    [MaxLength(100)]
    public string? LocalVersion { get; set; }

    /// <summary>
    /// Version hash/timestamp from remote side
    /// </summary>
    [MaxLength(100)]
    public string? RemoteVersion { get; set; }

    /// <summary>
    /// Whether the item was deleted locally
    /// </summary>
    [Required]
    public bool IsLocalDeleted { get; set; }

    /// <summary>
    /// Whether the item was deleted remotely
    /// </summary>
    [Required]
    public bool IsRemoteDeleted { get; set; }

    /// <summary>
    /// Additional metadata (JSON serialized)
    /// </summary>
    [MaxLength(int.MaxValue)]
    public string? Metadata { get; set; }
}
