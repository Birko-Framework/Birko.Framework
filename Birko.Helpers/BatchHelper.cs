using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Helpers
{
    public static class BatchHelper
    {
        public const int DefaultBatchSize = 50;

        /// <summary>
        /// Splits items into batches and invokes an async function for each batch,
        /// aggregating all results into a single list.
        /// </summary>
        public static async Task<List<TResult>> ProcessInBatchesAsync<TItem, TResult>(
            IReadOnlyCollection<TItem> items,
            Func<IReadOnlyCollection<TItem>, CancellationToken, Task<IEnumerable<TResult>>> batchFunc,
            int batchSize = DefaultBatchSize,
            CancellationToken ct = default)
        {
            if (items.Count == 0)
                return new List<TResult>();

            if (items.Count <= batchSize)
            {
                var result = await batchFunc(items, ct);
                return result.ToList();
            }

            var results = new List<TResult>();
            foreach (var batch in items.Chunk(batchSize))
            {
                ct.ThrowIfCancellationRequested();
                var batchResults = await batchFunc(batch, ct);
                results.AddRange(batchResults);
            }
            return results;
        }

        /// <summary>
        /// Splits items into batches, processes each with an async function,
        /// and builds a dictionary from all results.
        /// </summary>
        public static async Task<Dictionary<TKey, TResult>> ProcessInBatchesToDictionaryAsync<TItem, TKey, TResult>(
            IReadOnlyCollection<TItem> items,
            Func<IReadOnlyCollection<TItem>, CancellationToken, Task<IEnumerable<TResult>>> batchFunc,
            Func<TResult, TKey> keySelector,
            int batchSize = DefaultBatchSize,
            CancellationToken ct = default) where TKey : notnull
        {
            var list = await ProcessInBatchesAsync(items, batchFunc, batchSize, ct);
            return list.ToDictionary(keySelector);
        }

        /// <summary>
        /// Splits items into batches and invokes a sync function for each batch,
        /// aggregating all results into a single list.
        /// </summary>
        public static List<TResult> ProcessInBatches<TItem, TResult>(
            IReadOnlyCollection<TItem> items,
            Func<IReadOnlyCollection<TItem>, IEnumerable<TResult>> batchFunc,
            int batchSize = DefaultBatchSize)
        {
            if (items.Count == 0)
                return new List<TResult>();

            if (items.Count <= batchSize)
                return batchFunc(items).ToList();

            var results = new List<TResult>();
            foreach (var batch in items.Chunk(batchSize))
                results.AddRange(batchFunc(batch));
            return results;
        }

        /// <summary>
        /// Splits items into batches and invokes an async action for each batch (no return value).
        /// Useful for bulk insert/update/delete operations.
        /// </summary>
        public static async Task ForEachBatchAsync<TItem>(
            IReadOnlyCollection<TItem> items,
            Func<IReadOnlyCollection<TItem>, CancellationToken, Task> batchAction,
            int batchSize = DefaultBatchSize,
            CancellationToken ct = default)
        {
            if (items.Count == 0) return;

            if (items.Count <= batchSize)
            {
                await batchAction(items, ct);
                return;
            }

            foreach (var batch in items.Chunk(batchSize))
            {
                ct.ThrowIfCancellationRequested();
                await batchAction(batch, ct);
            }
        }

        /// <summary>
        /// Splits items into batches and invokes a sync action for each batch (no return value).
        /// </summary>
        public static void ForEachBatch<TItem>(
            IReadOnlyCollection<TItem> items,
            Action<IReadOnlyCollection<TItem>> batchAction,
            int batchSize = DefaultBatchSize)
        {
            if (items.Count == 0) return;

            if (items.Count <= batchSize)
            {
                batchAction(items);
                return;
            }

            foreach (var batch in items.Chunk(batchSize))
                batchAction(batch);
        }
    }
}
