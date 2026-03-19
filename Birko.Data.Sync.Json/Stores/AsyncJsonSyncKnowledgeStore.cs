using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.JSON.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Data.Sync.Json.Models;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;

namespace Birko.Data.Sync.Json.Stores;

/// <summary>
/// Async JSON file-based implementation of IAsyncSyncKnowledgeItemStore.
/// </summary>
public class AsyncJsonSyncKnowledgeStore : AsyncJsonStore<JsonSyncKnowledgeItem>, IAsyncSyncKnowledgeItemStore<JsonSyncKnowledgeItem>
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

    public JsonSyncKnowledgeItem CreateKnowledgeItem(Guid guid, string? localItemHash, string? remoteItemHash, SyncOptions options)
    {
        return new JsonSyncKnowledgeItem
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
