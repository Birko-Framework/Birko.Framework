using Birko.Data.Models;
using Birko.Data.Patterns.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.Tenant.Models;
using System;

namespace Birko.Data.SQL.Tests.TestResources.Models;

/// <summary>
/// Test model that implements both ITenant and ISoftDeletable,
/// mirroring Symbio's TenantEntity → BaseEntity hierarchy.
/// </summary>
[Table("TenantSoftDeleteModels")]
public class TenantSoftDeleteModel : AbstractLogModel, ITenant, ISoftDeletable
{
    public Guid TenantGuid { get; set; }
    public string? TenantName { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? OptionalCount { get; set; }
}
