using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Reaches the capabilities of the store at the bottom of a decorator chain (TASK-631, SH-M336).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A decorator implements <see cref="IAsyncBulkStore{T}"/>, so a store composed by <c>StoreWrapperBuilder</c> lost every
    /// other interface of the raw store: <c>chain as IAsyncTransactionalStore&lt;T, TContext&gt;</c> was null, and the way
    /// out, <see cref="IStoreWrapper.GetInnerStore"/>, sent an operation past the soft-delete and tenant scope of every
    /// decorator above it.
    /// </para>
    /// <para>
    /// Two kinds of capability, two answers. <b>Unscoped</b> ones, a transaction context and settings, belong to the raw
    /// store whatever the decorators do, so <see cref="SetTransactionContext{T, TContext}"/> and
    /// <see cref="SetSettings{TSettings}"/> deliver them there. <b>Scoped</b> ones, aggregation, are forwarded by every
    /// decorator through <see cref="AggregateThrough{T}"/>, each adding its own scope; this class offers no way to reach a
    /// scoped capability of the raw store directly.
    /// </para>
    /// </remarks>
    public static class StoreChain
    {
        /// <summary>The store, then each store it wraps, down to the raw one.</summary>
        public static IEnumerable<object> Walk(object store)
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            for (object? current = store; current != null && seen.Add(current); current = (current as IStoreWrapper)?.GetInnerStore())
            {
                yield return current;
            }
        }

        /// <summary>
        /// Gives <paramref name="context"/> to the store in the chain that takes a transaction context of that type.
        /// </summary>
        /// <exception cref="NotSupportedException">No store in the chain takes one.</exception>
        public static void SetTransactionContext<T, TContext>(object store, TContext? context) where T : Models.AbstractModel
        {
            foreach (var link in Walk(store))
            {
                switch (link)
                {
                    case IAsyncTransactionalStore<T, TContext> async:
                        async.SetTransactionContext(context);
                        return;
                    case ITransactionalStore<T, TContext> sync:
                        sync.SetTransactionContext(context);
                        return;
                }
            }
            throw new NotSupportedException(
                $"No store in the chain of {store.GetType().Name} takes a {typeof(TContext).Name} transaction context.");
        }

        /// <summary>Gives <paramref name="settings"/> to the store in the chain that takes settings of that type.</summary>
        /// <exception cref="NotSupportedException">No store in the chain takes them.</exception>
        public static void SetSettings<TSettings>(object store, TSettings settings)
        {
            foreach (var link in Walk(store))
            {
                if (link is ISettingsStore<TSettings> target)
                {
                    target.SetSettings(settings);
                    return;
                }
            }
            throw new NotSupportedException(
                $"No store in the chain of {store.GetType().Name} takes {typeof(TSettings).Name} settings.");
        }

        /// <summary>
        /// Runs <paramref name="query"/> on <paramref name="inner"/>, the next store down, for a decorator that has already
        /// applied its own scope to the query.
        /// </summary>
        /// <exception cref="NotSupportedException">The inner store does not aggregate.</exception>
        public static Task<IReadOnlyList<AggregateResult>> AggregateThrough<T>(object? inner, AggregateQuery<T> query, CancellationToken ct)
            where T : Models.AbstractModel
            => inner is IAsyncAggregatableStore<T> aggregatable
                ? aggregatable.AggregateAsync(query, ct)
                : throw new NotSupportedException(
                    $"{inner?.GetType().Name ?? "The inner store"} does not support aggregation, so the chain cannot aggregate {typeof(T).Name}.");
    }
}
