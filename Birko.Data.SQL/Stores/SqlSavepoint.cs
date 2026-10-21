using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Stores
{
    /// <summary>
    /// The one producer of the framework's savepoints (TASK-694, TASK-697): a write run inside a savepoint of a transaction
    /// it does not own, rolled back to it when the write throws and released when it succeeds, so a failed write leaves
    /// the transaction as it was.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="DbTransaction.Save"/> / <see cref="DbTransaction.Rollback(string)"/> /
    /// <see cref="DbTransaction.Release"/>, which every provider's driver implements, so no dialect is involved.
    /// </remarks>
    public static class SqlSavepoint
    {
        /// <summary>The key on the write's exception that records a failed rollback to its savepoint.</summary>
        public const string RollbackFailureKey = "Birko.SavepointRollbackFailure";

        private static long _counter;

        // Unique per write, so nested savepoints (a decorator over a decorator) each roll back only their own.
        private static string NextName() => "birko_retry_" + Interlocked.Increment(ref _counter);

        /// <summary>Runs <paramref name="write"/> inside a savepoint of <paramref name="transaction"/>.</summary>
        public static void Run(DbTransaction transaction, Action write)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentNullException.ThrowIfNull(write);
            var name = NextName();
            transaction.Save(name);
            try
            {
                write();
            }
            catch (Exception error)
            {
                RollBack(transaction, name, error);
                throw;
            }
            transaction.Release(name);
        }

        /// <inheritdoc cref="Run"/>
        public static async Task RunAsync(DbTransaction transaction, Func<Task> write, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(transaction);
            ArgumentNullException.ThrowIfNull(write);
            var name = NextName();
            await transaction.SaveAsync(name, ct).ConfigureAwait(false);
            try
            {
                await write().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                // Not the caller's token: a cancelled write still has to leave the transaction usable.
                await RollBackAsync(transaction, name, error).ConfigureAwait(false);
                throw;
            }
            // Nor here: the write was applied, so a cancellation now must not report it as failed.
            await transaction.ReleaseAsync(name, CancellationToken.None).ConfigureAwait(false);
        }

        // A rollback that fails means the transaction is lost anyway, so the write's own error is the one to surface
        // (rule 9: its type is the contract). The rollback's failure is recorded on it rather than dropped (rule 26).
        private static void RollBack(DbTransaction transaction, string name, Exception error)
        {
            try
            {
                transaction.Rollback(name);
            }
            catch (Exception rollbackFailure)
            {
                error.Data[RollbackFailureKey] = rollbackFailure.Message;
            }
        }

        private static async Task RollBackAsync(DbTransaction transaction, string name, Exception error)
        {
            try
            {
                await transaction.RollbackAsync(name, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception rollbackFailure)
            {
                error.Data[RollbackFailureKey] = rollbackFailure.Message;
            }
        }
    }
}
