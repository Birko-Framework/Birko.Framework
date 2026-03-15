using System;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Marks a store that can participate in an external transaction.
    /// When a transaction context is set, CRUD operations use it instead of
    /// creating their own per-operation transactions.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TContext">The platform-specific transaction context
    /// (e.g., SqlTransactionContext for SQL, IClientSessionHandle for MongoDB,
    /// IDocumentSession for RavenDB).</typeparam>
    public interface ITransactionalStore<T, TContext> : IStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Gets the current transaction context, or null if none is active.
        /// </summary>
        TContext? TransactionContext { get; }

        /// <summary>
        /// Sets the external transaction context. When set, all CRUD operations
        /// participate in this transaction. Pass null to clear and return to
        /// per-operation transaction mode.
        /// </summary>
        /// <param name="context">The transaction context, or null to clear.</param>
        void SetTransactionContext(TContext? context);
    }

    /// <summary>
    /// Async version of <see cref="ITransactionalStore{T, TContext}"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TContext">The platform-specific transaction context.</typeparam>
    public interface IAsyncTransactionalStore<T, TContext> : IAsyncStore<T>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// Gets the current transaction context, or null if none is active.
        /// </summary>
        TContext? TransactionContext { get; }

        /// <summary>
        /// Sets the external transaction context. When set, all CRUD operations
        /// participate in this transaction. Pass null to clear and return to
        /// per-operation transaction mode.
        /// </summary>
        /// <param name="context">The transaction context, or null to clear.</param>
        void SetTransactionContext(TContext? context);
    }

    /// <summary>
    /// Bulk transactional store (sync).
    /// </summary>
    public interface ITransactionalBulkStore<T, TContext> : IBulkStore<T>, ITransactionalStore<T, TContext>
        where T : Models.AbstractModel
    {
    }

    /// <summary>
    /// Bulk transactional store (async).
    /// </summary>
    public interface IAsyncTransactionalBulkStore<T, TContext> : IAsyncBulkStore<T>, IAsyncTransactionalStore<T, TContext>
        where T : Models.AbstractModel
    {
    }
}
