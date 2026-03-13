using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Sync.ElasticSearch.Models;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;

namespace Birko.Data.Sync.ElasticSearch.Stores;

/// <summary>
/// Async Elasticsearch implementation of IAsyncSyncKnowledgeItemStore.
/// </summary>
public class AsyncElasticSyncKnowledgeStore : AsyncElasticSearchStore<ElasticSyncKnowledgeItem>, IAsyncSyncKnowledgeItemStore<ElasticSyncKnowledgeItem>
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

    public ElasticSyncKnowledgeItem CreateKnowledgeItem(Guid guid, string? localItemHash, string? remoteItemHash, SyncOptions options)
    {
        return new ElasticSyncKnowledgeItem
        {
            Guid = Guid.NewGuid(),
            Id = ElasticSyncKnowledgeItem.GenerateId(guid, options.Scope),
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
