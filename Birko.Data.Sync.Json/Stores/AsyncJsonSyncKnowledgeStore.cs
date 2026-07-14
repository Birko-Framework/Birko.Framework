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
    /// <summary>
    /// Returns the scope's last sync time as the max <see cref="ISyncKnowledgeItem.LastSyncedAt"/> over
    /// the knowledge items in that scope, or null when the scope has none.
    /// </summary>
    /// <remarks>
    /// CR-L214: last-sync-time is <em>derived</em> from the items rather than stored as a standalone
    /// per-scope record, so a scope with zero knowledge items has no observable timestamp — see the
    /// matching note on <see cref="SetLastSyncTimeAsync"/>.
    /// </remarks>
    public async Task<DateTime?> GetLastSyncTimeAsync(string scope, CancellationToken cancellationToken)
    {
        var items = await ReadAsync(x => x.Scope == scope, ct: cancellationToken).ConfigureAwait(false);
        return items?.Any() == true ? items.Max(x => (DateTime?)x.LastSyncedAt) : null;
    }

    /// <summary>
    /// Stamps <paramref name="lastSyncTime"/> onto every knowledge item in the scope (one bulk write).
    /// </summary>
    /// <remarks>
    /// CR-L214: because the time is derived from the items (see <see cref="GetLastSyncTimeAsync"/>),
    /// stamping a scope that has <em>no</em> items persists nothing — the returned value is echoed back
    /// but a subsequent Get still yields null (the scope reads as never-synced / initial-sync). This is
    /// correct for the real flow: <c>AsyncSyncProvider</c> always persists the round's knowledge items
    /// (Create/Update) <em>before</em> calling this, so a stamp only ever lands on an already-populated
    /// scope. A caller that stamps an empty scope in isolation should not expect the timestamp to survive.
    /// </remarks>
    public async Task<DateTime?> SetLastSyncTimeAsync(string scope, DateTime? lastSyncTime, CancellationToken cancellationToken)
    {
        if (lastSyncTime == null) return null;

        var items = (await ReadAsync(x => x.Scope == scope, ct: cancellationToken).ConfigureAwait(false))?.ToList();
        if (items != null && items.Count > 0)
        {
            foreach (var item in items)
            {
                item.LastSyncedAt = lastSyncTime.Value;
            }

            // CR-M162: one bulk UpdateAsync rewrites the JSON file a single time, instead of the whole
            // file being re-serialized once per item (O(n) full-file rewrites).
            await UpdateAsync(items, ct: cancellationToken).ConfigureAwait(false);
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
