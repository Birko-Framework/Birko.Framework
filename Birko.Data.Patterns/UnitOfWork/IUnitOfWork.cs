using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Patterns.UnitOfWork;

/// <summary>
/// Defines a unit of work for coordinating multiple store operations
/// within a single transaction.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Whether a transaction is currently active.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Begins a new transaction.
    /// </summary>
    Task BeginAsync(CancellationToken ct = default);

    /// <summary>
    /// Commits all changes made within the transaction.
    /// </summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Rolls back all changes made within the transaction.
    /// </summary>
    Task RollbackAsync(CancellationToken ct = default);
}

/// <summary>
/// Unit of work that exposes the underlying transaction context
/// so that stores/repositories can participate in the transaction.
/// </summary>
/// <typeparam name="TContext">The platform-specific transaction context
/// (e.g., DbTransaction for SQL, IClientSessionHandle for MongoDB).</typeparam>
public interface IUnitOfWork<out TContext> : IUnitOfWork
{
    /// <summary>
    /// The underlying transaction context. Null if no transaction is active.
    /// Pass this to stores that need to participate in the transaction.
    /// </summary>
    TContext? Context { get; }
}
