using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue.Transactions
{
    /// <summary>
    /// Supports transactional message sending.
    /// Messages are only visible to consumers after the transaction is committed.
    /// </summary>
    public interface ITransactionalProducer : IMessageProducer
    {
        /// <summary>
        /// Begins a transaction. Messages sent within the transaction
        /// are not visible until CommitAsync is called.
        /// </summary>
        Task BeginTransactionAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the transaction, making all sent messages visible.
        /// </summary>
        Task CommitAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Rolls back the transaction, discarding all sent messages.
        /// </summary>
        Task RollbackAsync(CancellationToken cancellationToken = default);
    }
}
