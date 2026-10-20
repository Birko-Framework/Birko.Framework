using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.Stores
{
    /// <summary>
    /// The one producer of the SQL stores' <see cref="Birko.Data.Stores.IRetryableWriteStore"/> (TASK-694): a savepoint
    /// around a retryable write, taken only where a refused statement would abort the active transaction.
    /// </summary>
    /// <remarks>
    /// The transaction is the store's own context (<c>SetTransactionContext</c>) or the ambient boundary covering its
    /// database (<c>SqlUnitOfWork</c>), the same two doors every operation enters by. A savepoint uses
    /// <see cref="DbTransaction.Save"/> / <see cref="DbTransaction.Rollback(string)"/> /
    /// <see cref="DbTransaction.Release"/>, which every provider's driver implements, so no dialect is involved.
    /// </remarks>
    internal static class SqlRetryableWrite
    {
        private static long _counter;

        private static DbTransaction? Transaction(AbstractConnectorBase? connector, SqlTransactionContext? context)
        {
            // Rule 35: a provider limit routed around in one place; elsewhere a savepoint would only cost round trips.
            if (connector == null || !connector.AbortsTransactionOnError)
            {
                return null;
            }
            return context?.Transaction ?? AmbientSqlTransaction.Find(connector.Settings?.GetId())?.Transaction;
        }

        // Unique per write, so nested retryable writes (a decorator over a decorator) each roll back only their own.
        private static string NextName() => "birko_retry_" + Interlocked.Increment(ref _counter);

        public static void Run(AbstractConnectorBase? connector, SqlTransactionContext? context, Action write)
        {
            var transaction = Transaction(connector, context);
            if (transaction == null)
            {
                write();
                return;
            }
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

        public static async Task RunAsync(AbstractConnectorBase? connector, SqlTransactionContext? context, Func<Task> write, CancellationToken ct)
        {
            var transaction = Transaction(connector, context);
            if (transaction == null)
            {
                await write().ConfigureAwait(false);
                return;
            }
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

        /// <summary>The key on the write's exception that records a failed rollback to its savepoint.</summary>
        public const string RollbackFailureKey = "Birko.SavepointRollbackFailure";

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
