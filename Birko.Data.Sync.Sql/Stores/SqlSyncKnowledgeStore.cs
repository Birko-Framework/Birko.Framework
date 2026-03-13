using System;
using System.Linq;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
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
    public DateTime? GetLastSyncTime(string scope)
    {
        var items = Read(x => x.Scope == scope);
        return items?.Any() == true ? items.Max(x => (DateTime?)x.LastSyncedAt) : null;
    }

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
