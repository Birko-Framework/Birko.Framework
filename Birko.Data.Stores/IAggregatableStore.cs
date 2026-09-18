using System.Collections.Generic;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Optional interface for stores that support synchronous server-side aggregation.
    /// Stores that don't implement this can still be aggregated via <see cref="AggregateHelper"/>.
    /// </summary>
    public interface IAggregatableStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Executes an aggregation query against the store.
        /// </summary>
        IReadOnlyList<AggregateResult> Aggregate(AggregateQuery<T> query);
    }
}
