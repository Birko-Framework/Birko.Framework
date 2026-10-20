using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// A store that can keep its caller's transaction usable when it refuses a write the caller means to retry
    /// (TASK-694).
    /// </summary>
    /// <remarks>
    /// Measured on PostgreSQL: a statement the server refuses (a unique violation) aborts the enclosing transaction, so the
    /// re-check and the retry that follow fail with <c>25P02</c>. A store implementing this runs the write inside a
    /// savepoint where that is the case, rolls back to it when the write throws, and releases it when it succeeds. Where a
    /// refusal does not abort the transaction, or no transaction is active, the write runs as is.
    /// <para>
    /// Only for a write the caller retries: a savepoint costs two round trips, which doubled the cost of a write inside a
    /// transaction on PostgreSQL and SQL Server, and nearly tripled it on MySQL (measured, TASK-694).
    /// </para>
    /// </remarks>
    public interface IRetryableWriteStore
    {
        /// <summary>Runs <paramref name="write"/> so that a refusal leaves the caller's transaction usable.</summary>
        void RunRetryable(Action write);

        /// <inheritdoc cref="RunRetryable"/>
        Task RunRetryableAsync(Func<Task> write, CancellationToken ct = default);
    }

    /// <summary>
    /// Runs a write a caller may retry through the <see cref="IRetryableWriteStore"/> beneath any decorators, found
    /// through <see cref="IStoreWrapper"/>; with no such store, the write runs as is (TASK-694).
    /// </summary>
    public static class RetryableWrite
    {
        /// <summary>The <see cref="IRetryableWriteStore"/> at or beneath <paramref name="store"/>, or null.</summary>
        public static IRetryableWriteStore? Find(object? store)
            // StoreChain.Walk is the one chain walker (rule 16), with its cycle guard.
            => store == null ? null : StoreChain.Walk(store).OfType<IRetryableWriteStore>().FirstOrDefault();

        /// <summary>Runs <paramref name="write"/> through the store beneath <paramref name="store"/>.</summary>
        public static void Run(object? store, Action write)
        {
            ArgumentNullException.ThrowIfNull(write);
            var retryable = Find(store);
            if (retryable == null)
            {
                write();
                return;
            }
            retryable.RunRetryable(write);
        }

        /// <inheritdoc cref="Run(object?, Action)"/>
        public static TResult Run<TResult>(object? store, Func<TResult> write)
        {
            ArgumentNullException.ThrowIfNull(write);
            TResult result = default!;
            Run(store, () => { result = write(); });
            return result;
        }

        /// <inheritdoc cref="Run(object?, Action)"/>
        public static Task RunAsync(object? store, Func<Task> write, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(write);
            var retryable = Find(store);
            return retryable == null ? write() : retryable.RunRetryableAsync(write, ct);
        }

        /// <inheritdoc cref="Run(object?, Action)"/>
        public static async Task<TResult> RunAsync<TResult>(object? store, Func<Task<TResult>> write, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(write);
            TResult result = default!;
            await RunAsync(store, async () => { result = await write(); }, ct);
            return result;
        }
    }
}
