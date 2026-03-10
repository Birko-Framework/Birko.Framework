using Birko.Data.Stores;
using Birko.Data.Sync.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Sync.Stores
{
    public interface IAsyncSyncKnowledgeItemStore<T>
        : IAsyncBulkStore<T> where T : Data.Models.AbstractModel, ISyncKnowledgeItem
    {
        Task<DateTime?> GetLastSyncTimeAsync(string scope, CancellationToken cancellationToken);
        Task<DateTime?> SetLastSyncTimeAsync(string scope, DateTime? lastSyncTime, CancellationToken cancellationToken);
        T CreateKnowledgeItem(Guid guid, string? localItemHash, string? remoteItemHash, SyncOptions options);

        /* return new SyncKnowledgeItem
        {
            EntityGuid = guid,
            Scope = options.Scope,
            LastSyncedAt = DateTime.UtcNow,
            LocalVersion = localItemHash,
            RemoteVersion = remoteItemHash
            IsLocalDeleted = string.IsNullOrEmpty(localItemHash),
            IsRemoteDeleted = string.IsNullOrEmpty(remoteItemHash)
        };*/
    }
}