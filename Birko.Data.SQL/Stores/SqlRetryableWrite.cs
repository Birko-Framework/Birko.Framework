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
    /// database (<c>SqlUnitOfWork</c>), the same two doors every operation enters by. The savepoint is
    /// <see cref="SqlSavepoint"/>'s (TASK-697).
    /// </remarks>
    internal static class SqlRetryableWrite
    {
        private static DbTransaction? Transaction(AbstractConnectorBase? connector, SqlTransactionContext? context)
        {
            // Rule 35: a provider limit routed around in one place; elsewhere a savepoint would only cost round trips.
            if (connector == null || !connector.AbortsTransactionOnError)
            {
                return null;
            }
            return context?.Transaction ?? AmbientSqlTransaction.Find(connector.Settings?.GetId())?.Transaction;
        }

        public static void Run(AbstractConnectorBase? connector, SqlTransactionContext? context, Action write)
        {
            var transaction = Transaction(connector, context);
            if (transaction == null)
            {
                write();
                return;
            }
            SqlSavepoint.Run(transaction, write);
        }

        public static async Task RunAsync(AbstractConnectorBase? connector, SqlTransactionContext? context, Func<Task> write, CancellationToken ct)
        {
            var transaction = Transaction(connector, context);
            if (transaction == null)
            {
                await write().ConfigureAwait(false);
                return;
            }
            await SqlSavepoint.RunAsync(transaction, write, ct).ConfigureAwait(false);
        }

        /// <summary>The key on the write's exception that records a failed rollback to its savepoint.</summary>
        public const string RollbackFailureKey = SqlSavepoint.RollbackFailureKey;
    }
}
