using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.Exceptions
{
    public class StoreException : Exception
    {
        public StoreException(string message) : this(message, null!) { }
        public StoreException(string message, Exception? innerException) : base(message, innerException) { }

        /// <summary>
        /// The refusal of a create whose key is already stored: the same type, <see cref="StoreUniqueViolationException"/>, on
        /// every provider (TASK-567, TASK-625), with the provider's own exception as <see cref="Exception.InnerException"/> when
        /// there is one. The database-backed stores use this message; the in-memory, JSON and XML stores word their own and
        /// name the Guid (TASK-651: this used to promise one message everywhere).
        /// </summary>
        public static StoreException DuplicateCreate(Type entityType, Exception? innerException = null)
            => new StoreUniqueViolationException(
                $"Create of {entityType.Name} refused: an entity with the same key is already stored. "
                + "Create never overwrites; use Update or Save for a stored entity.", innerException);

        /// <summary>
        /// The refusal of an update that would store a value a unique index already holds for another entity (TASK-632):
        /// a <see cref="StoreUniqueViolationException"/>, with the provider's own exception as
        /// <see cref="Exception.InnerException"/>.
        /// </summary>
        public static StoreException UniqueViolation(Type entityType, Exception? innerException = null)
            => new StoreUniqueViolationException(
                $"Update of {entityType.Name} refused: it would store a value a unique index already holds for another entity.",
                innerException);

        /// <summary>
        /// What a write refused by a unique key throws when it cannot repair the collision (TASK-685): the original
        /// <paramref name="violation"/>, rethrown as it was, when nothing failed; otherwise a
        /// <see cref="StoreUniqueViolationException"/> wrapping it and naming <paramref name="readFailure"/>, the query that
        /// was to decide the retry.
        /// </summary>
        /// <remarks>
        /// Measured on PostgreSQL inside a caller's transaction: the refusal aborts the transaction, so the next query
        /// fails with <c>25P02 current transaction is aborted</c>. Surfacing that driver exception replaced the typed
        /// refusal callers select on (rule 9). The caller's transaction is lost either way; the error now says why.
        /// </remarks>
        public static Exception UnretryableUniqueViolation(StoreUniqueViolationException violation, Exception? readFailure)
        {
            if (readFailure == null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(violation).Throw();
            }
            return new StoreUniqueViolationException(
                $"{violation.Message} The retry could not query the store after the refusal (the transaction may have been "
                + "aborted by it, as PostgreSQL does): " + readFailure!.Message, violation);
        }

        /// <summary>
        /// Runs <paramref name="probe"/>, the query that decides whether a refused write may be retried. If it fails, the
        /// refusal is surfaced through <see cref="UnretryableUniqueViolation"/>, never the probe's own exception;
        /// cancellation passes through.
        /// </summary>
        public static bool ProbeAfterRefusal(Func<bool> probe, StoreUniqueViolationException violation)
        {
            try
            {
                return probe();
            }
            catch (Exception readFailure) when (readFailure is not OperationCanceledException)
            {
                throw UnretryableUniqueViolation(violation, readFailure);
            }
        }

        /// <inheritdoc cref="ProbeAfterRefusal"/>
        public static async System.Threading.Tasks.Task<bool> ProbeAfterRefusalAsync(
            Func<System.Threading.Tasks.Task<bool>> probe, StoreUniqueViolationException violation)
        {
            try
            {
                return await probe();
            }
            catch (Exception readFailure) when (readFailure is not OperationCanceledException)
            {
                throw UnretryableUniqueViolation(violation, readFailure);
            }
        }
    }

    /// <summary>
    /// A write the store refused because a unique key or index already holds the value: a duplicate create
    /// (<see cref="StoreException.DuplicateCreate"/>) or an update colliding with another entity
    /// (<see cref="StoreException.UniqueViolation"/>).
    /// </summary>
    /// <remarks>
    /// TASK-632. A subclass, so every existing <c>catch (StoreException)</c> still selects it (rule 9), while a caller that
    /// can repair the collision (the slug and default decorators retry) selects exactly this case.
    /// </remarks>
    public class StoreUniqueViolationException : StoreException
    {
        public StoreUniqueViolationException(string message, Exception? innerException) : base(message, innerException) { }
    }
}
