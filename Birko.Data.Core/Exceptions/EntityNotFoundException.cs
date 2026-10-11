using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Exceptions
{
    /// <summary>
    /// An update addressed an entity that is not stored. Thrown by every store's <c>Update</c> / <c>UpdateAsync</c>
    /// instead of silently writing nothing (SH-M306, TASK-555); a bulk update throws it after applying the entities
    /// that are stored, naming the rest in <see cref="Guids"/>.
    /// </summary>
    /// <remarks>
    /// <c>Save</c> / <c>SaveAsync</c> catch it for their own entity and create it under the caller's Guid instead — the
    /// upsert (SH-M309). Each provider detects the miss from its native matched / affected count, never a pre-read.
    /// </remarks>
    public class EntityNotFoundException : StoreException
    {
        /// <summary>The entity type the update addressed.</summary>
        public Type EntityType { get; }

        /// <summary>The Guids that matched no stored entity (<see cref="Guid.Empty"/> for an entity with no Guid).</summary>
        public IReadOnlyList<Guid> Guids { get; }

        public EntityNotFoundException(Type entityType, Guid? guid)
            : this(entityType, new[] { guid ?? Guid.Empty })
        {
        }

        public EntityNotFoundException(Type entityType, IEnumerable<Guid> guids)
            : this(entityType, guids?.ToList() ?? new List<Guid>())
        {
        }

        private EntityNotFoundException(Type entityType, List<Guid> guids)
            : base(BuildMessage(entityType, guids))
        {
            EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
            Guids = guids;
        }

        /// <summary>Throws for <paramref name="missing"/> when it holds any Guid; a bulk update's one producer.</summary>
        public static void ThrowIfAny(Type entityType, IEnumerable<Guid> missing)
        {
            var list = missing?.ToList() ?? new List<Guid>();
            if (list.Count > 0)
            {
                throw new EntityNotFoundException(entityType, list);
            }
        }

        private static string BuildMessage(Type? entityType, List<Guid> guids)
        {
            var name = entityType?.Name ?? "entity";
            return guids.Count == 1
                ? $"No stored {name} has Guid {guids[0]}; Update writes only an entity that exists — use Create or Save."
                : $"{guids.Count} {name} entities are not stored and were not updated: {string.Join(", ", guids)}.";
        }
    }
}
