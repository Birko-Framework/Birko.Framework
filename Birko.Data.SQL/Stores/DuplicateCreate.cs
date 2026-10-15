using System;
using Birko.Data.Exceptions;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.SQL.Stores
{
    /// <summary>
    /// Turns a create that collided with a stored key into the <see cref="StoreException"/> every provider throws for it
    /// (TASK-567), the in-memory store's type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applied at the stores, never at the connector. <c>Connector.Insert</c> keeps throwing the driver's exception,
    /// because callers already select on it through <see cref="AbstractConnectorBase.IsUniqueViolationException"/>
    /// (<c>SqlEventStore</c>'s optimistic append), and rewrapping in flight would silently break them (rule 9).
    /// </para>
    /// <para>
    /// Only a unique violation is translated: every other failure keeps its type. The driver's exception is kept as
    /// <see cref="Exception.InnerException"/>.
    /// </para>
    /// </remarks>
    internal static class DuplicateCreate
    {
        /// <remarks>
        /// Every connector's classifier walks the inner-exception chain, which is also why a caller that classifies the
        /// rethrown <see cref="StoreException"/> still recognises it (<c>SqlEventStore</c>'s conflict filter, measured live).
        /// </remarks>
        public static bool IsDuplicate(AbstractConnectorBase? connector, Exception ex)
            => connector?.IsUniqueViolationException(ex) == true;

        public static StoreException Refusal(Type type, Exception ex)
            => new StoreException(
                $"Create of {type.Name} refused: an entity with the same key is already stored. "
                + "Create never overwrites; use Update or Save for a stored entity.", ex);
    }
}
