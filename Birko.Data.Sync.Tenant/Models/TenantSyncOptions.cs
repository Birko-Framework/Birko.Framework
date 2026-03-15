using Birko.Data.Sync.Models;
using System;

namespace Birko.Data.Sync.Tenant.Models;

/// <summary>
/// Tenant-aware options for synchronization operations
/// Extends SyncOptions with tenant identification
/// </summary>
public class TenantSyncOptions : SyncOptions
{
    /// <summary>
    /// Tenant ID for tenant-scoped synchronization
    /// </summary>
    public Guid? TenantGuid { get; set; }

    /// <summary>
    /// Optional tenant name for display/logging purposes
    /// </summary>
    public string? TenantName { get; set; }
}
