using System;
using Birko.Data.Sync.Models;

namespace Birko.Data.Sync.Tenant.Models;

/// <summary>
/// Concrete implementation of ITenantSyncKnowledgeItem for tenant-aware sync metadata
/// </summary>
public class TenantSyncKnowledgeItem : ITenantSyncKnowledgeItem
{
    /// <inheritdoc />
    public Guid? Guid { get; set; }

    /// <inheritdoc />
    public Guid EntityGuid { get; set; }

    /// <inheritdoc />
    public string Scope { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTime LastSyncedAt { get; set; }

    /// <inheritdoc />
    public string? LocalVersion { get; set; }

    /// <inheritdoc />
    public string? RemoteVersion { get; set; }

    /// <inheritdoc />
    public bool IsLocalDeleted { get; set; }

    /// <inheritdoc />
    public bool IsRemoteDeleted { get; set; }

    /// <inheritdoc />
    public string? Metadata { get; set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public string? TenantName { get; set; }
}
