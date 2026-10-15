using System;
using System.Linq;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Sql.Models;
using Birko.Data.Sync.Stores;

namespace Birko.Data.Sync.Sql.Stores;

/// <summary>
/// SQL implementation of ISyncKnowledgeItemStore.
/// Works with any SQL connector (PostgreSQL, MSSql, MySQL, SQLite).
/// </summary>
public class SqlSyncKnowledgeStore<DB> : DataBaseBulkStore<DB, SqlSyncKnowledgeItem>, ISyncKnowledgeItemStore<SqlSyncKnowledgeItem>
    where DB : AbstractConnector
{
    /// <summary>
    /// Returns the scope's last sync time as the max <see cref="ISyncKnowledgeItem.LastSyncedAt"/> over
    /// the knowledge rows in that scope, or null when the scope has none. See
    /// <see cref="SetLastSyncTime"/> for the empty-scope caveat.
    /// </summary>
    public DateTime? GetLastSyncTime(string scope)
    {
        var items = Read(x => x.Scope == scope);
        return items?.Any() == true ? items.Max(x => (DateTime?)x.LastSyncedAt) : null;
    }

    /// <summary>
    /// Stamps <paramref name="lastSyncTime"/> onto every knowledge row in the scope.
    /// </summary>
    /// <remarks>
    /// CR-L220: last-sync-time is <em>derived</em> from the rows (see <see cref="GetLastSyncTime"/>), only
    /// refreshing existing rows — stamping a scope with <em>no</em> rows persists nothing. This is a deliberate cross-backend
    /// design choice shared with the JSON reference store (CR-L214).
    /// <para>TASK-157 (SH-M067): returns the time only when a row was stamped, and <c>null</c> when the scope has none, so a
    /// caller can tell persisted from discarded. The sync providers write a scope marker row each run, so a stamp after
    /// a run always lands.</para>
    /// </remarks>
    public DateTime? SetLastSyncTime(string scope, DateTime? lastSyncTime)
    {
        if (lastSyncTime == null) return null;

        // SH-M068 (TASK-157): the read was the connector's lazy reader, still open while each row was updated on a
        // second connection. Materialised before any write, and one bulk write.
        var items = Read(x => x.Scope == scope).ToList();
        if (items.Count == 0)
        {
            return null;
        }

        foreach (var item in items)
        {
            item.LastSyncedAt = lastSyncTime.Value;
        }
        Update(items);
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
