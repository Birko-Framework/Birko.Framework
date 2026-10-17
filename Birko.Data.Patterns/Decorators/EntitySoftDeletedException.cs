using System;
using Birko.Data.Exceptions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// The refusal of a <c>Save</c> whose entity is soft-deleted: restore it explicitly (<c>Restore</c> / <c>RestoreAsync</c> on
/// the soft-delete wrapper) before writing it again.
/// </summary>
/// <remarks>
/// TASK-649 (user's decision, 2026-10-17). <c>Save</c> fell through to a create of the same Guid, which every store refused
/// as a duplicate ("use Update or Save", measured on InMemory, SQLite and PostgreSQL): the wrong reason, and a store
/// that did not enforce the key would have resurrected the row from a stale copy. A <see cref="StoreException"/>, so every
/// <c>catch (StoreException)</c> still selects it (rule 9), and deliberately not an <see cref="EntityNotFoundException"/>,
/// which <c>StoreSave</c> reads as "create it".
/// </remarks>
public sealed class EntitySoftDeletedException : StoreException
{
    /// <summary>The soft-deleted entity's Guid.</summary>
    public Guid Guid { get; }

    public EntitySoftDeletedException(Type entityType, Guid guid)
        : base($"{entityType.Name} {guid} is soft-deleted, so Save cannot write it. Restore it first (Restore / RestoreAsync on the soft-delete wrapper).")
    {
        Guid = guid;
    }
}
