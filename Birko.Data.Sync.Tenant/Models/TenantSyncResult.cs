using Birko.Data.Sync.Models;
using System;

namespace Birko.Data.Sync.Tenant.Models;

/// <summary>
/// Tenant-aware result of a synchronization operation
/// Extends SyncResult with tenant identification
/// </summary>
public class TenantSyncResult : SyncResult
{
    /// <summary>
    /// Tenant ID for this sync operation
    /// </summary>
    public Guid? TenantGuid { get; set; }

    /// <summary>
    /// Optional tenant name for display/logging purposes
    /// </summary>
    public string? TenantName { get; set; }
}
