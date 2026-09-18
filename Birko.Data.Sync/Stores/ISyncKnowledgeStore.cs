using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Sync.Models;

namespace Birko.Data.Sync.Stores
{
    /// <summary>
    /// Non-generic interface for sync knowledge store operations.
    /// Used by tenant-aware sync providers that don't need generic store constraints.
    /// </summary>
    public interface ISyncKnowledgeStore
    {
        /// <summary>
        /// Gets sync knowledge for a specific scope and optional tenant.
        /// </summary>
        Task<Dictionary<Guid, ISyncKnowledgeItem>> GetKnowledgeAsync(
            string scope,
            Guid? tenantId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the last sync time for a scope and optional tenant.
        /// </summary>
        Task<DateTime?> GetLastSyncTimeAsync(
            string scope,
            Guid? tenantId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates or creates sync knowledge items.
        /// </summary>
        Task UpdateKnowledgeAsync(
            IEnumerable<ISyncKnowledgeItem> items,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets the last sync time for a scope and optional tenant.
        /// </summary>
        Task SetLastSyncTimeAsync(
            string scope,
            Guid? tenantId,
            DateTime syncTime,
            CancellationToken cancellationToken = default);
    }
}
