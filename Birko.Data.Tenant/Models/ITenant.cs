using System;

namespace Birko.Data.Tenant.Models;

/// <summary>
/// Interface for tenant-aware entities
/// </summary>
public interface ITenant
{
    /// <summary>
    /// Unique identifier for the tenant
    /// </summary>
    Guid TenantId { get; set; }

    /// <summary>
    /// Optional tenant name for display purposes
    /// </summary>
    string? TenantName { get; set; }
}
