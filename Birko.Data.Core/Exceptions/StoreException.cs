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
        /// The refusal of a create whose key is already stored: the same type and message on every provider (TASK-567,
        /// TASK-625), with the provider's own exception as <see cref="Exception.InnerException"/> when there is one.
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
