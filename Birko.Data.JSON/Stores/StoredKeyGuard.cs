using System;
using System.Collections.Generic;
using Birko.Data.Exceptions;
using Birko.Data.Models;

namespace Birko.Data.JSON.Stores
{
    /// <summary>
    /// Adds created entities only if none of them is stored already (TASK-567): create means "a new entity", never an
    /// overwrite. The whole batch is refused before anything is written, so a duplicate never leaves half a batch in the
    /// file.
    /// </summary>
    internal static class StoredKeyGuard
    {
        /// <exception cref="StoreException">A Guid in <paramref name="pending"/> is already stored, or appears twice in it.</exception>
        public static void AddNew<T>(IDictionary<Guid, T> items, IReadOnlyList<T> pending) where T : AbstractModel
        {
            var seen = new HashSet<Guid>();
            foreach (var entity in pending)
            {
                var guid = entity.Guid!.Value;
                if (items.ContainsKey(guid) || !seen.Add(guid))
                {
                    throw new StoreException(
                        $"Create of {typeof(T).Name} refused: an entity with Guid {guid} is already stored. "
                        + "Create never overwrites; use Update or Save for a stored entity.");
                }
            }
            foreach (var entity in pending)
            {
                items[entity.Guid!.Value] = entity;
            }
        }
    }
}
