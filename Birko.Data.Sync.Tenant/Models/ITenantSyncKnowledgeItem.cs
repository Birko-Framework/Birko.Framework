using Birko.Data.Sync.Models;
using Birko.Data.Tenant.Models;

namespace Birko.Data.Sync.Tenant.Models;

/// <summary>
/// Interface for tenant-aware synchronization metadata for an entity
/// Extends ISyncKnowledgeItem with ITenant for tenant identification
/// Concrete implementations are provided by underlying stores
/// </summary>
public interface ITenantSyncKnowledgeItem : ISyncKnowledgeItem, ITenant
{
}
