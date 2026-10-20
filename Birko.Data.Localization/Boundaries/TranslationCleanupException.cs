using Birko.Data.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Localization.Boundaries;

/// <summary>
/// The entities were deleted, but their translation rows could not be (TASK-683). Thrown only where the write is not
/// atomic: on an atomic boundary the entity delete is rolled back instead.
/// </summary>
/// <remarks>
/// The rows of <see cref="RemainingEntityGuids"/> are still there. Remove them with
/// <c>IEntityTranslations.RemoveEntitiesAsync</c>, which can be repeated safely. The error that stopped the cleanup is
/// the <see cref="Exception.InnerException"/>.
/// </remarks>
public class TranslationCleanupException : StoreException
{
    public TranslationCleanupException(Type entityType, IEnumerable<Guid> remainingEntityGuids, Exception innerException)
        : this(entityType, remainingEntityGuids.ToList(), innerException)
    {
    }

    private TranslationCleanupException(Type entityType, IReadOnlyList<Guid> remaining, Exception innerException)
        : base(
            $"{remaining.Count} {entityType.Name} entit{(remaining.Count == 1 ? "y was" : "ies were")} deleted, but "
            + "their translations were not; remove them with IEntityTranslations.RemoveEntitiesAsync. "
            + innerException.Message,
            innerException)
    {
        EntityType = entityType;
        RemainingEntityGuids = remaining;
    }

    public Type EntityType { get; }

    /// <summary>The deleted entities whose translation rows remain.</summary>
    public IReadOnlyList<Guid> RemainingEntityGuids { get; }
}
