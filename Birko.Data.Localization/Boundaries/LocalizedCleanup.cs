using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.Boundaries;

/// <summary>
/// The one producer of "a delete's translation cleanup failed" (TASK-683). The wrappers delete the entity first, then
/// its translation rows.
/// </summary>
/// <remarks>
/// On an atomic boundary a cleanup failure propagates as it is, and rolls the entity delete back with it. Otherwise the
/// entity is gone, so the failure is rethrown as <see cref="TranslationCleanupException"/> naming the entities whose
/// rows remain (rule 22: never report success for a write that did not happen). Translations are never deleted first:
/// a failed entity delete would then have lost translations nothing can restore.
/// </remarks>
internal static class LocalizedCleanup
{
    public static void Run(ILocalizedWriteBoundary boundary, Type entityType, IReadOnlyCollection<Guid> entityGuids, Action delete)
    {
        if (boundary.IsAtomic || entityGuids.Count == 0)
        {
            delete();
            return;
        }
        try
        {
            delete();
        }
        catch (Exception failure)
        {
            throw new TranslationCleanupException(entityType, entityGuids, failure);
        }
    }

    public static async Task RunAsync(
        ILocalizedWriteBoundary boundary, Type entityType, IReadOnlyCollection<Guid> entityGuids,
        Func<CancellationToken, Task> delete, CancellationToken ct)
    {
        if (boundary.IsAtomic || entityGuids.Count == 0)
        {
            await delete(ct);
            return;
        }
        try
        {
            // Not the caller's token: the entities are already deleted, and a cleanup cancelled half way would leave rows
            // this report could not name.
            await delete(CancellationToken.None);
        }
        catch (Exception failure)
        {
            throw new TranslationCleanupException(entityType, entityGuids, failure);
        }
    }

    /// <summary>The entities of <paramref name="items"/> the inner store reported as not stored, left out.</summary>
    public static IEnumerable<T> Stored<T>(IEnumerable<T> items, IReadOnlyList<Guid> missing) where T : Data.Models.AbstractModel
    {
        var notStored = missing.ToHashSet();
        return items.Where(item => item.Guid is not Guid guid || !notStored.Contains(guid));
    }

    /// <summary>
    /// After a collection update reported items as not stored: saves the stored items' translations. On an atomic boundary
    /// a failure of that save propagates and rolls the whole write back. Otherwise the report wins: the caller needs the
    /// list of items not stored more than this second error.
    /// </summary>
    public static void SaveStored(ILocalizedWriteBoundary boundary, Action save)
    {
        if (boundary.IsAtomic)
        {
            save();
            return;
        }
        try
        {
            save();
        }
        catch (Exception)
        {
            // The EntityNotFoundException is rethrown by the caller.
        }
    }

    /// <inheritdoc cref="SaveStored"/>
    public static async Task SaveStoredAsync(ILocalizedWriteBoundary boundary, Func<Task> save)
    {
        if (boundary.IsAtomic)
        {
            await save();
            return;
        }
        try
        {
            await save();
        }
        catch (Exception)
        {
            // The EntityNotFoundException is rethrown by the caller.
        }
    }
}
