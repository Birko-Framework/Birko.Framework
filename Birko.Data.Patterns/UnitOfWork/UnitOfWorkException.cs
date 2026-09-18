using System;

namespace Birko.Data.Patterns.UnitOfWork;

/// <summary>
/// Exception thrown when a unit of work operation fails.
/// </summary>
public class UnitOfWorkException : Exception
{
    public UnitOfWorkException(string message) : base(message) { }
    public UnitOfWorkException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an operation is attempted on a unit of work that has no active transaction.
/// </summary>
public class NoActiveTransactionException : UnitOfWorkException
{
    public NoActiveTransactionException()
        : base("No active transaction. Call BeginAsync() first.") { }
}

/// <summary>
/// Thrown when BeginAsync is called while a transaction is already active.
/// </summary>
public class TransactionAlreadyActiveException : UnitOfWorkException
{
    public TransactionAlreadyActiveException()
        : base("A transaction is already active. Commit or rollback before starting a new one.") { }
}

/// <summary>
/// Thrown when a unit of work is asked to commit a boundary that a nested participant already rolled back.
/// </summary>
/// <remarks>
/// A nested unit of work joins the enclosing boundary rather than opening its own transaction, so its
/// rollback cannot undo anything on its own. Marking the boundary rollback-only and refusing the owner's
/// commit is what stops the participant's decision being silently discarded — the alternative is an
/// operation that reports success having thrown half of itself away.
/// </remarks>
public class TransactionRollbackOnlyException : UnitOfWorkException
{
    public TransactionRollbackOnlyException()
        : base("The transaction was marked rollback-only by a nested unit of work and cannot be committed. "
             + "Call RollbackAsync() instead.") { }
}
