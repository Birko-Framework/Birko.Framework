using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Sync.MongoDb.Models;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;

namespace Birko.Data.Sync.MongoDb.Stores;

/// <summary>
/// Async MongoDB implementation of IAsyncSyncKnowledgeItemStore.
/// </summary>
public class AsyncMongoSyncKnowledgeStore : AsyncMongoDBStore<MongoSyncKnowledgeItem>, IAsyncSyncKnowledgeItemStore<MongoSyncKnowledgeItem>
{
    public async Task<DateTime?> GetLastSyncTimeAsync(string scope, CancellationToken cancellationToken)
    {
        var items = await ReadAsync(x => x.Scope == scope, ct: cancellationToken).ConfigureAwait(false);
        return items?.Any() == true ? items.Max(x => (DateTime?)x.LastSyncedAt) : null;
    }

    public async Task<DateTime?> SetLastSyncTimeAsync(string scope, DateTime? lastSyncTime, CancellationToken cancellationToken)
    {
        if (lastSyncTime == null) return null;

        var items = await ReadAsync(x => x.Scope == scope, ct: cancellationToken).ConfigureAwait(false);
        if (items != null)
        {
            foreach (var item in items)
            {
                item.LastSyncedAt = lastSyncTime.Value;
                await UpdateAsync(item, ct: cancellationToken).ConfigureAwait(false);
            }
        }

        return lastSyncTime;
    }

    public MongoSyncKnowledgeItem CreateKnowledgeItem(Guid guid, string? localItemHash, string? remoteItemHash, SyncOptions options)
    {
        return new MongoSyncKnowledgeItem
        {
            Guid = Guid.NewGuid(),
            EntityGuid = guid,
            Scope = options.Scope,
            LastSyncedAt = DateTime.UtcNow,
            LocalVersion = localItemHash,
            RemoteVersion = remoteItemHash,
            IsLocalDeleted = string.IsNullOrEmpty(localItemHash),
            IsRemoteDeleted = string.IsNullOrEmpty(remoteItemHash)
        };
    }
}
