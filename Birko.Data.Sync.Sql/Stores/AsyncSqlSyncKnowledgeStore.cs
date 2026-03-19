using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Sql.Models;
using Birko.Data.Sync.Stores;

namespace Birko.Data.Sync.Sql.Stores;

/// <summary>
/// Async SQL implementation of IAsyncSyncKnowledgeItemStore.
/// Works with any SQL connector (PostgreSQL, MSSql, MySQL, SQLite).
/// </summary>
public class AsyncSqlSyncKnowledgeStore<DB> : AsyncDataBaseBulkStore<DB, SqlSyncKnowledgeItem>, IAsyncSyncKnowledgeItemStore<SqlSyncKnowledgeItem>
    where DB : AbstractConnector
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

    public SqlSyncKnowledgeItem CreateKnowledgeItem(Guid guid, string? localItemHash, string? remoteItemHash, SyncOptions options)
    {
        return new SqlSyncKnowledgeItem
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
