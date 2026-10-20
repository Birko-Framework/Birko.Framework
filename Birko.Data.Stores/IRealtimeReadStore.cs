using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// A store whose queries see a write only after an index refresh, but which can read one entity by its Guid at once
    /// (ElasticSearch's realtime GET).
    /// </summary>
    /// <remarks>
    /// TASK-698: after a refused create, the translation upsert re-reads the key another writer just stored. A search
    /// missed that row inside ElasticSearch's refresh window (measured), so the refusal reached the caller. The refused
    /// row's Guid is known in advance (<c>LocalizedTranslationRows.KeyGuid</c>), so the re-read asks by Guid through
    /// this capability, found beneath decorators.
    /// </remarks>
    public interface IRealtimeReadStore<T> where T : Models.AbstractModel
    {
        /// <summary>The entity stored under <paramref name="guid"/>, including a write not yet visible to queries; null when none.</summary>
        T? ReadRealtime(Guid guid);
    }

    /// <inheritdoc cref="IRealtimeReadStore{T}"/>
    public interface IAsyncRealtimeReadStore<T> where T : Models.AbstractModel
    {
        /// <inheritdoc cref="IRealtimeReadStore{T}.ReadRealtime"/>
        Task<T?> ReadRealtimeAsync(Guid guid, CancellationToken ct = default);
    }
}
