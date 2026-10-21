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
        private static readonly AsyncLocal<int> _declared = new();

        /// <summary>
        /// True while a write a caller will retry on a refusal runs (TASK-697): a participant beneath, such as a joined
        /// localized write boundary, must then leave its caller's transaction as it was when the write fails rather
        /// than mark it rollback-only, or the retry that follows succeeds and the caller's commit is refused anyway.
        /// </summary>
        public static bool IsDeclared => _declared.Value > 0;

        /// <summary>The <see cref="IRetryableWriteStore"/> at or beneath <paramref name="store"/>, or null.</summary>
        public static IRetryableWriteStore? Find(object? store)
            // StoreChain.Walk is the one chain walker (rule 16), with its cycle guard.
            => store == null ? null : StoreChain.Walk(store).OfType<IRetryableWriteStore>().FirstOrDefault();

        /// <summary>
        /// Runs <paramref name="write"/> through the store beneath <paramref name="store"/>, declared retried
        /// (<see cref="IsDeclared"/>). A null <paramref name="store"/> is a caller's last attempt: the write runs as is.
        /// </summary>
        public static void Run(object? store, Action write)
        {
            ArgumentNullException.ThrowIfNull(write);
            if (store == null)
            {
                write();
                return;
            }
            var retryable = Find(store);
            var previous = _declared.Value;
            _declared.Value = previous + 1;
            try
            {
                if (retryable == null)
                {
                    write();
                    return;
                }
                retryable.RunRetryable(write);
            }
            finally
            {
                _declared.Value = previous;
            }
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
        public static async Task RunAsync(object? store, Func<Task> write, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(write);
            if (store == null)
            {
                await write();
                return;
            }
            var retryable = Find(store);
            // Set in this async method, so it flows into the write and never back to the caller.
            _declared.Value++;
            if (retryable == null)
            {
                await write();
                return;
            }
            await retryable.RunRetryableAsync(write, ct);
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
