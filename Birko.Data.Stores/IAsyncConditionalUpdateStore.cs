using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// A store that can update one entity only while it still matches a condition, deciding at the moment of writing — a
    /// compare-and-set, the shape of a one-time claim (redeem a code, revoke a token, take a job).
    /// </summary>
    /// <remarks>
    /// TASK-1013. Implemented where the backend can make the decision atomic: the SQL stores (an <c>UPDATE … WHERE</c>
    /// on the id and the condition), MongoDB (a replace filtered the same way), RavenDB, Cosmos DB and Elasticsearch
    /// (optimistic concurrency on the document's version) and InMemory. The JSON and XML stores do not implement it: they
    /// cannot decide at once across processes, and a racy claim is worse than none.
    /// <para>
    /// The condition is an expression because a backend that decides on the server must translate it; one it cannot
    /// translate is refused, never evaluated in memory instead.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The entity type.</typeparam>
    public interface IAsyncConditionalUpdateStore<T> where T : Models.AbstractModel
    {
        /// <summary>
        /// Reads the entity stored under <paramref name="guid"/>; when <paramref name="condition"/> holds for it, applies
        /// <paramref name="update"/> and writes it — only if the stored entity still matches <paramref name="condition"/>
        /// when the write happens. Of concurrent callers whose update makes the condition false (a claim), exactly one
        /// gets the entity back and the others <c>null</c>.
        /// </summary>
        /// <param name="guid">The entity's id. <see cref="Guid.Empty"/> answers <c>null</c>.</param>
        /// <param name="condition">What the stored entity must satisfy, both when read and when written.</param>
        /// <param name="update">The change, applied to the read entity; it must not change the entity's id.</param>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The entity as written, or <c>null</c> when it is missing or does not (or no longer) match.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="condition"/> or <paramref name="update"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Called inside a unit of work or transaction whose writes the backend applies only at commit (RavenDB's session,
        /// Cosmos DB's transactional batch), where it cannot decide at once. A SQL transaction and a MongoDB session are
        /// allowed: their conditional write is decided by the statement itself.
        /// </exception>
        Task<T?> UpdateIfAsync(Guid guid, Expression<Func<T, bool>> condition, Action<T> update, CancellationToken ct = default);
    }
}
