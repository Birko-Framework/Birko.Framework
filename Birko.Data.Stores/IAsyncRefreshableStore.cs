using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// A store whose filtered reads see a write only after the backend makes it visible (TASK-1013). Elasticsearch is the
    /// one today: every read but the realtime GET is a search, and a search sees a write only after the next index refresh
    /// (about a second). A caller that must find a record by value right after writing it calls <see cref="RefreshAsync"/>
    /// after the write. A store that reads its own writes at once does not implement this.
    /// </summary>
    public interface IAsyncRefreshableStore
    {
        /// <summary>Makes every write made so far visible to the store's filtered reads and counts.</summary>
        /// <exception cref="System.InvalidOperationException">The backend could not make them visible.</exception>
        Task RefreshAsync(CancellationToken ct = default);
    }
}
