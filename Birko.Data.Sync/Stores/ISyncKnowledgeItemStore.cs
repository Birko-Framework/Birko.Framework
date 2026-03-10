using Birko.Data.Stores;
using Birko.Data.Sync.Models;
using System;

namespace Birko.Data.Sync.Stores
{
    public interface ISyncKnowledgeItemStore<T>
        : IBulkStore<T> where T : Data.Models.AbstractModel, ISyncKnowledgeItem
    {
        DateTime? GetLastSyncTime(string scope);
        DateTime? SetLastSyncTime(string scope, DateTime? lastSyncTime);
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

