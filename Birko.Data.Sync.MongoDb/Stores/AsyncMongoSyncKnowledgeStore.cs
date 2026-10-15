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

    /// <remarks>
    /// <para>TASK-157 (SH-M067): returns the time only when a row was stamped, and <c>null</c> when the scope has none, so a
    /// caller can tell persisted from discarded. The sync providers write a scope marker row each run, so a stamp after
    /// a run always lands.</para>
    /// </remarks>
    public async Task<DateTime?> SetLastSyncTimeAsync(string scope, DateTime? lastSyncTime, CancellationToken cancellationToken)
    {
        if (lastSyncTime == null) return null;

        // SH-M068 (TASK-157): materialised before any write, and one bulk write.
        var items = (await ReadAsync(x => x.Scope == scope, ct: cancellationToken).ConfigureAwait(false)).ToList();
        if (items.Count == 0)
        {
            return null;
        }

        foreach (var item in items)
        {
            item.LastSyncedAt = lastSyncTime.Value;
        }
        await UpdateAsync(items, ct: cancellationToken).ConfigureAwait(false);
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
