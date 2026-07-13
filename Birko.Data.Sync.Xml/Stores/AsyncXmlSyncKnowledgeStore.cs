using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.XML.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Data.Sync.Xml.Models;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;

namespace Birko.Data.Sync.Xml.Stores;

/// <summary>
/// Async XML file-based implementation of IAsyncSyncKnowledgeItemStore.
/// </summary>
public class AsyncXmlSyncKnowledgeStore : AsyncXmlStore<XmlSyncKnowledgeItem>, IAsyncSyncKnowledgeItemStore<XmlSyncKnowledgeItem>
{
    public async Task<DateTime?> GetLastSyncTimeAsync(string scope, CancellationToken cancellationToken)
    {
        var items = await ReadAsync(x => x.Scope == scope, ct: cancellationToken).ConfigureAwait(false);
        return items?.Any() == true ? items.Max(x => (DateTime?)x.LastSyncedAt) : null;
    }

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

            // Match CR-M162 (Sync.Json): one bulk UpdateAsync rewrites the XML file a single time instead
            // of re-serializing the whole file once per item.
            await UpdateAsync(items, ct: cancellationToken).ConfigureAwait(false);
        }

        return lastSyncTime;
    }

    public XmlSyncKnowledgeItem CreateKnowledgeItem(Guid guid, string? localItemHash, string? remoteItemHash, SyncOptions options)
    {
        return new XmlSyncKnowledgeItem
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
