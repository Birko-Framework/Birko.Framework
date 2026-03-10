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
