using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
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
    /// <summary>
    /// Returns the scope's last sync time as the max <see cref="ISyncKnowledgeItem.LastSyncedAt"/> over
    /// the knowledge rows in that scope, or null when the scope has none. See
    /// <see cref="SetLastSyncTimeAsync"/> for the empty-scope caveat.
    /// </summary>
    public async Task<DateTime?> GetLastSyncTimeAsync(string scope, CancellationToken cancellationToken)
    {
        var items = await ReadAsync(x => x.Scope == scope, ct: cancellationToken).ConfigureAwait(false);
        return items?.Any() == true ? items.Max(x => (DateTime?)x.LastSyncedAt) : null;
    }

    /// <summary>
    /// Stamps <paramref name="lastSyncTime"/> onto every knowledge row in the scope.
    /// </summary>
    /// <remarks>
    /// CR-L220: last-sync-time is <em>derived</em> from the rows (see <see cref="GetLastSyncTimeAsync"/>),
    /// only refreshing existing rows — stamping a scope with <em>no</em> rows persists nothing. This is a deliberate cross-backend
    /// design choice shared with the JSON reference store (CR-L214). Note the abstract <c>ISyncKnowledgeStore.SetLastSyncTimeAsync</c> takes a
    /// non-nullable time, whereas this per-item store's overload takes <c>DateTime?</c> and short-circuits
    /// on null.
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
