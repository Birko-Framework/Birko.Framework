using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Optional interface for stores that support server-side aggregation.
    /// Stores that don't implement this can still be aggregated via <see cref="AggregateHelper"/>.
    /// </summary>
    public interface IAsyncAggregatableStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Executes an aggregation query against the store.
        /// </summary>
        Task<IReadOnlyList<AggregateResult>> AggregateAsync(
            AggregateQuery<T> query,
            CancellationToken ct = default);
    }
}
