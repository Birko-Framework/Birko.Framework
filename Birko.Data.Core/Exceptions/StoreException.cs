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
            => new StoreException(
                $"Create of {entityType.Name} refused: an entity with the same key is already stored. "
                + "Create never overwrites; use Update or Save for a stored entity.", innerException);
    }
}
