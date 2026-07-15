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
    /// refreshing existing rows — stamping a scope with <em>no</em> rows persists nothing (the value is
    /// echoed back but a later Get still yields null / initial-sync). This is a deliberate cross-backend
    /// design choice shared with the JSON reference store (CR-L214); it is safe because the sync provider
    /// always persists the round's knowledge rows before stamping, so a stamp only ever lands on a
    /// populated scope.
    /// </remarks>
    public DateTime? SetLastSyncTime(string scope, DateTime? lastSyncTime)
    {
        if (lastSyncTime == null) return null;

        var items = Read(x => x.Scope == scope);
        if (items != null)
        {
            foreach (var item in items)
            {
                item.LastSyncedAt = lastSyncTime.Value;
                Update(item);
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
