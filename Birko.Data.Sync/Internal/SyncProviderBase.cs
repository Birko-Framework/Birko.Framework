using Birko.Data.Sync.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Birko.Data.Sync.Internal;

/// <summary>
/// Base class for sync providers containing shared logic
/// </summary>
public abstract class SyncProviderBase<T, TKnowledge>
    where T : Data.Models.AbstractModel
    where TKnowledge : Data.Models.AbstractModel, ISyncKnowledgeItem
{
    protected readonly PropertyInfo GuidProperty;

    protected SyncProviderBase()
    {
        // Get Guid property using reflection
        GuidProperty = typeof(T).GetProperty("Guid")
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} must have a Guid property");
    }

    /// <summary>
    /// Result of batch processing
    /// </summary>
    protected class BatchProcessResult
    {
        public int Processed { get; set; }
        public List<SyncError> Errors { get; set; } = new();
        public List<TKnowledge> KnowledgeUpdates { get; set; } = new();
    }

    /// <summary>
    /// The knowledge row that records "this scope has been synced", so a run that reconciled nothing still ends the
    /// initial sync (SH-M067, TASK-157). No entity can carry it: <see cref="BuildEntityDictionary"/> refuses an empty
    /// Guid, so the marker never matches an item.
    /// </summary>
    protected static readonly Guid ScopeMarkerGuid = Guid.Empty;

    /// <summary>
    /// Refuses options no run can honour (SH-M054, TASK-157). A zero <see cref="SyncOptions.BatchSize"/> divided by
    /// zero, reported as a generic "Sync failed"; a negative one never advanced the batch loop, so the run never ended.
    /// </summary>
    protected static void ValidateOptions(SyncOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (options.BatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.BatchSize,
                "SyncOptions.BatchSize must be positive.");
    }

    /// <summary>
    /// The scope's knowledge by entity Guid. Two rows for one entity (left by an earlier identity defect, SH-M064)
    /// made <c>ToDictionary</c> throw on every run, with no repair path; the most recently synced row wins, and the
    /// run's upsert then updates that one.
    /// </summary>
    protected Dictionary<Guid, TKnowledge> BuildKnowledgeDictionary(IEnumerable<TKnowledge> rows)
    {
        return rows
            .GroupBy(GetGuid)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(k => k.LastSyncedAt).First());
    }

    /// <summary>
    /// The entity Guids this run processes. SH-M055 (TASK-157): a <see cref="SyncOptions.MaxItems"/> cap sliced an
    /// unordered union, so which items ran depended on dictionary order. A capped run now takes the items that need
    /// work first, then Guid order, so repeated capped runs converge instead of re-taking the same settled items.
    /// </summary>
    protected List<Guid> SelectGuids(
        Dictionary<Guid, T> localDict,
        Dictionary<Guid, T> remoteDict,
        Dictionary<Guid, TKnowledge> knowledge,
        bool isInitialSync,
        SyncOptions options,
        SyncFilterOptions<T>? filterOptions)
    {
        var all = localDict.Keys.Union(remoteDict.Keys).ToList();
        if (options.MaxItems is not int max || max < 0 || all.Count <= max)
        {
            return all;
        }

        return all
            .Select(guid =>
            {
                localDict.TryGetValue(guid, out var local);
                remoteDict.TryGetValue(guid, out var remote);
                knowledge.TryGetValue(guid, out var known);
                var settled = DetermineSyncAction(guid, local, remote, known, isInitialSync, options, filterOptions).Action == SyncAction.Skip;
                return (Guid: guid, Settled: settled);
            })
            .OrderBy(x => x.Settled)
            .ThenBy(x => x.Guid)
            .Take(max)
            .Select(x => x.Guid)
            .ToList();
    }

    /// <summary>
    /// Whether the state after an action may be recorded as knowledge: both sides hold the item, or neither does.
    /// </summary>
    /// <remarks>
    /// TASK-157. A row records the hash of each side, and a side with no hash reads as DELETED on the next run. A row
    /// written for a one-sided state (an initial sync that skipped a local-only item, a create a save filter blocked)
    /// therefore told the next run the missing side had been deleted: measured, a local-only item present at the first
    /// sync was deleted by the second under RemoteWins. Only a reconciled pair is knowledge.
    /// </remarks>
    protected static bool IsReconciled(T? local, T? remote) => (local == null) == (remote == null);

    /// <summary>
    /// Whether <paramref name="item"/> differs from the version recorded for its side. An entity with no
    /// <c>UpdatedAt</c> has no stable version, so it always counts as changed (the behaviour before change detection).
    /// </summary>
    protected bool HasChanged(T item, string? recordedVersion)
    {
        return GetUpdatedAt(item) == null || GetVersionHash(item) != recordedVersion;
    }

    /// <summary>
    /// Determine what sync action to take for an item
    /// </summary>
    /// <remarks>
    /// TASK-157 rebuilt the decision on a <b>known pair</b>: a knowledge row recording that both sides held the item at
    /// the last reconcile. Only a known pair can say a later absence is a deletion, and only it can say which side changed.
    /// <list type="bullet">
    /// <item>Deletion used to be read from the stored <c>IsLocalDeleted</c> / <c>IsRemoteDeleted</c>, which every backend
    /// sets to "the side's hash was empty WHEN THE ROW WAS WRITTEN". After a successful sync both are false, so a later
    /// deletion was never seen: measured, an item deleted remotely after a synced round was re-created there by the next
    /// bidirectional run. It is now <c>known pair &amp;&amp; side absent</c>.</item>
    /// <item>SH-M059: the recorded versions were never compared, so every both-present item was rewritten every run and
    /// a both-sides edit was never a conflict. Now: neither side changed is a Skip, one side changed wins, both changed
    /// is the policy (LocalWins / RemoteWins directly, NewestWins / Custom through a conflict carrying both items).</item>
    /// </list>
    /// </remarks>
    protected (SyncAction Action, string? Winner, string? DeleteOn, ConflictInfo? Conflict) DetermineSyncAction(
        Guid guid,
        T? localItem,
        T? remoteItem,
        TKnowledge? knowledgeItem,
        bool isInitialSync,
        SyncOptions options,
        SyncFilterOptions<T>? filterOptions)
    {
        var localExists = localItem != null;
        var remoteExists = remoteItem != null;

        // Initial sync: download everything
        if (isInitialSync)
        {
            if (remoteExists && !localExists)
                return (SyncAction.Create, null, null, null);
            return (SyncAction.Skip, null, null, null); // Absent on both sides, or already present locally
        }

        var knownPair = knowledgeItem != null
            && !knowledgeItem.IsLocalDeleted && !knowledgeItem.IsRemoteDeleted
            && !string.IsNullOrEmpty(knowledgeItem.LocalVersion) && !string.IsNullOrEmpty(knowledgeItem.RemoteVersion);
        var localDeleted = knownPair && !localExists;
        var remoteDeleted = knownPair && !remoteExists;
        var localChanged = localExists && (!knownPair || HasChanged(localItem!, knowledgeItem!.LocalVersion));
        var remoteChanged = remoteExists && (!knownPair || HasChanged(remoteItem!, knowledgeItem!.RemoteVersion));
        var unchangedPair = knownPair && localExists && remoteExists && !localChanged && !remoteChanged;

        // Download only
        if (options.Direction == SyncDirection.Download)
        {
            if (remoteExists && !localExists)
                return (SyncAction.Create, null, null, null);
            if (remoteExists && localExists)
                return unchangedPair ? (SyncAction.Skip, null, null, null) : (SyncAction.Update, "remote", null, null);
            if (localExists && remoteDeleted)
                return (SyncAction.Delete, null, "local", null);
            return (SyncAction.Skip, null, null, null);
        }

        // Upload only
        if (options.Direction == SyncDirection.Upload)
        {
            if (localExists && !remoteExists)
                return (SyncAction.Create, null, null, null);
            if (localExists && remoteExists)
                return unchangedPair ? (SyncAction.Skip, null, null, null) : (SyncAction.Update, "local", null, null);
            if (remoteExists && localDeleted)
                return (SyncAction.Delete, null, "remote", null);
            return (SyncAction.Skip, null, null, null);
        }

        // Bidirectional, both exist
        if (localExists && remoteExists)
        {
            if (unchangedPair)
                return (SyncAction.Skip, null, null, null);
            if (knownPair && localChanged != remoteChanged)
                return (SyncAction.Update, localChanged ? "local" : "remote", null, null);
            if (options.ConflictPolicy == ConflictResolutionPolicy.LocalWins)
                return (SyncAction.Update, "local", null, null);
            if (options.ConflictPolicy == ConflictResolutionPolicy.RemoteWins)
                return (SyncAction.Update, "remote", null, null);
            // Both sides changed since a known pair, and both carry a version: a genuine conflict.
            if (knownPair && GetUpdatedAt(localItem!) != null && GetUpdatedAt(remoteItem!) != null)
            {
                return (SyncAction.Conflict, null, null, new ConflictInfo
                {
                    Guid = guid,
                    LocalItem = localItem,
                    RemoteItem = remoteItem,
                    Reason = "Modified on both sides"
                });
            }
            return (SyncAction.Update, GetWinner(localItem!, remoteItem!, options.ConflictPolicy), null, null);
        }

        if (localExists && !remoteExists)
        {
            if (!remoteDeleted)
                return (SyncAction.Create, null, null, null); // New item to upload
            // Deleted remotely. Unchanged locally since the pair was recorded: the deletion simply propagates.
            if (!localChanged)
                return (SyncAction.Delete, null, "local", null);
            if (options.ConflictPolicy == ConflictResolutionPolicy.RemoteWins)
                return (SyncAction.Delete, null, "local", null);
            if (options.ConflictPolicy == ConflictResolutionPolicy.LocalWins)
                return (SyncAction.Create, null, null, null); // Re-create on remote
            return (SyncAction.Conflict, null, null, new ConflictInfo
            {
                Guid = guid,
                LocalItem = localItem,
                RemoteItem = null,
                Reason = "Modified locally but deleted remotely"
            });
        }

        if (!localExists && remoteExists)
        {
            if (!localDeleted)
                return (SyncAction.Create, null, null, null); // New item to download
            if (!remoteChanged)
                return (SyncAction.Delete, null, "remote", null);
            if (options.ConflictPolicy == ConflictResolutionPolicy.LocalWins)
                return (SyncAction.Delete, null, "remote", null);
            if (options.ConflictPolicy == ConflictResolutionPolicy.RemoteWins)
                return (SyncAction.Create, null, null, null); // Re-create locally
            return (SyncAction.Conflict, null, null, new ConflictInfo
            {
                Guid = guid,
                LocalItem = null,
                RemoteItem = remoteItem,
                Reason = "Modified remotely but deleted locally"
            });
        }

        return (SyncAction.Skip, null, null, null);
    }

    /// <summary>
    /// Get the winner of a conflict based on policy
    /// </summary>
    protected string GetWinner(T local, T remote, ConflictResolutionPolicy policy)
    {
        return policy switch
        {
            ConflictResolutionPolicy.LocalWins => "local",
            ConflictResolutionPolicy.RemoteWins => "remote",
            ConflictResolutionPolicy.NewestWins => GetNewest(local, remote),
            _ => "local" // Default for custom (handled elsewhere)
        };
    }

    /// <summary>
    /// Determine which item is newer based on UpdatedAt property
    /// </summary>
    protected string GetNewest(T local, T remote)
    {
        var localUpdatedAt = GetUpdatedAt(local);
        var remoteUpdatedAt = GetUpdatedAt(remote);

        if (localUpdatedAt.HasValue && remoteUpdatedAt.HasValue)
        {
            return localUpdatedAt.Value > remoteUpdatedAt.Value ? "local" : "remote";
        }

        return "local"; // Default if timestamps not available
    }

    /// <summary>
    /// Resolve a conflict based on policy
    /// </summary>
    protected ConflictResolution ResolveConflict(ConflictInfo conflict, SyncOptions options)
    {
        options.OnConflict?.Invoke(conflict);

        if (options.ConflictPolicy == ConflictResolutionPolicy.Custom && options.CustomConflictResolver != null)
        {
            return options.CustomConflictResolver(conflict);
        }

        return options.ConflictPolicy switch
        {
            ConflictResolutionPolicy.LocalWins => ConflictResolution.UseLocal,
            ConflictResolutionPolicy.RemoteWins => ConflictResolution.UseRemote,
            ConflictResolutionPolicy.NewestWins => GetNewestConflictResolution(conflict),
            _ => ConflictResolution.UseLocal
        };
    }

    /// <summary>
    /// Get conflict resolution based on newest timestamp
    /// </summary>
    protected ConflictResolution GetNewestConflictResolution(ConflictInfo conflict)
    {
        if (conflict.LocalItem == null) return ConflictResolution.UseRemote;
        if (conflict.RemoteItem == null) return ConflictResolution.UseLocal;

        var localUpdatedAt = GetUpdatedAt((T)conflict.LocalItem);
        var remoteUpdatedAt = GetUpdatedAt((T)conflict.RemoteItem);

        if (localUpdatedAt.HasValue && remoteUpdatedAt.HasValue)
        {
            return localUpdatedAt.Value > remoteUpdatedAt.Value
                ? ConflictResolution.UseLocal
                : ConflictResolution.UseRemote;
        }

        return ConflictResolution.UseLocal;
    }

    /// <summary>
    /// Analyze an item for preview
    /// </summary>
    protected SyncItemPreview AnalyzeItem(
        Guid guid,
        Dictionary<Guid, T> localDict,
        Dictionary<Guid, T> remoteDict,
        Dictionary<Guid, TKnowledge> knowledge,
        bool isInitialSync,
        SyncOptions options,
        SyncFilterOptions<T>? filterOptions)
    {
        var localExists = localDict.TryGetValue(guid, out var localItem);
        var remoteExists = remoteDict.TryGetValue(guid, out var remoteItem);
        var hasKnowledge = knowledge.TryGetValue(guid, out var knowledgeItem);

        var (action, winner, deleteOn, conflict) = DetermineSyncAction(
            guid, localItem, remoteItem, knowledgeItem, isInitialSync, options, filterOptions);

        return new SyncItemPreview
        {
            Guid = guid,
            Action = action,
            Reason = GetReason(action, localItem, remoteItem, knowledgeItem),
            Conflict = conflict,
            LocalVersion = GetVersionHash(localItem),
            RemoteVersion = GetVersionHash(remoteItem)
        };
    }

    /// <summary>
    /// Get reason for sync action
    /// </summary>
    protected string GetReason(SyncAction action, T? localItem, T? remoteItem, ISyncKnowledgeItem? knowledgeItem)
    {
        return action switch
        {
            SyncAction.Create when localItem == null => "New item from remote",
            SyncAction.Create when remoteItem == null => "New item from local",
            SyncAction.Update => "Item modified",
            // TASK-157: the side being deleted is the one that still holds the item; the other side deleted it.
            SyncAction.Delete when localItem == null => "Deleted locally",
            SyncAction.Delete when remoteItem == null => "Deleted remotely",
            SyncAction.Skip => "No changes",
            SyncAction.Conflict => "Conflict detected",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Check if item can be saved to local store
    /// </summary>
    protected bool CanSaveToLocal(T item, SyncFilterOptions<T>? filterOptions, SyncOptions options)
    {
        if (filterOptions?.CanSaveToLocal == null)
            return true;

        var canSave = filterOptions.CanSaveToLocal(item);
        if (!canSave)
        {
            HandleSaveFilterBlock(item, "local", filterOptions, options);
        }
        return canSave;
    }

    /// <summary>
    /// Check if item can be saved to remote store
    /// </summary>
    protected bool CanSaveToRemote(T item, SyncFilterOptions<T>? filterOptions, SyncOptions options)
    {
        if (filterOptions?.CanSaveToRemote == null)
            return true;

        var canSave = filterOptions.CanSaveToRemote(item);
        if (!canSave)
        {
            HandleSaveFilterBlock(item, "remote", filterOptions, options);
        }
        return canSave;
    }

    /// <summary>
    /// Handle save filter block
    /// </summary>
    protected void HandleSaveFilterBlock(T item, string target, SyncFilterOptions<T> filterOptions, SyncOptions options)
    {
        var guid = GetGuid(item);
        switch (filterOptions.OnSaveFilterBlock)
        {
            case SaveFilterBlockAction.ThrowException:
                throw new InvalidOperationException($"Item {guid} is blocked by save filter for {target}");

            case SaveFilterBlockAction.LogAsError:
                options.OnError?.Invoke(new SyncError
                {
                    ItemGuid = guid,
                    Operation = "SaveFilter",
                    Message = $"Item {guid} blocked by save filter for {target}"
                });
                break;

            case SaveFilterBlockAction.MarkConflict:
                // CR-L207: MarkConflict used to fall through the switch and behave like Skip. Surface it
                // via the OnConflict callback so a blocked save is distinguishable from a plain skip.
                options.OnConflict?.Invoke(new ConflictInfo
                {
                    Guid = guid,
                    Reason = $"Item {guid} blocked by save filter for {target}"
                });
                break;
        }
    }

    /// <summary>
    /// Get Guid from entity
    /// </summary>
    protected Guid GetGuid(T entity)
    {
        var value = GuidProperty.GetValue(entity);
        return value is Guid guid ? guid : Guid.Empty;
    }

    /// <summary>
    /// Keys a set of entities by Guid for change detection, surfacing a clear error when an entity has
    /// no Guid or two entities share one — rather than the opaque ArgumentException a plain
    /// ToDictionary(GetGuid) throws when several unsaved entities all map to Guid.Empty (CR-L209).
    /// </summary>
    /// <param name="items">The entities to key.</param>
    /// <param name="side">"local" or "remote", for the error message.</param>
    protected Dictionary<Guid, T> BuildEntityDictionary(IEnumerable<T> items, string side)
    {
        var dict = new Dictionary<Guid, T>();
        foreach (var item in items)
        {
            var guid = GetGuid(item);
            if (guid == Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"Cannot sync a {side} entity with an empty Guid; all syncable entities must have a Guid assigned.");
            }
            if (!dict.TryAdd(guid, item))
            {
                throw new InvalidOperationException(
                    $"Duplicate {side} entity Guid '{guid}' encountered during sync.");
            }
        }
        return dict;
    }

    /// <summary>
    /// Get Guid from sync knowledge item
    /// </summary>
    protected Guid GetGuid(ISyncKnowledgeItem knowledgeItem)
    {
        return knowledgeItem.EntityGuid;
    }

    /// <summary>
    /// Get UpdatedAt from entity
    /// </summary>
    protected DateTime? GetUpdatedAt(T entity)
    {
        var prop = typeof(T).GetProperty("UpdatedAt");
        // Match both DateTime and DateTime? (CR-H098): the guard previously only matched non-nullable
        // DateTime, so any model with a nullable UpdatedAt returned null here — silently degrading
        // NewestWins to LocalWins. The `value as DateTime?` cast already handles both underlying types.
        if (prop != null && (prop.PropertyType == typeof(DateTime) || Nullable.GetUnderlyingType(prop.PropertyType) == typeof(DateTime)))
        {
            var value = prop.GetValue(entity);
            return value as DateTime?;
        }
        return null;
    }

    /// <summary>
    /// Get version hash for entity
    /// </summary>
    protected string? GetVersionHash(T? entity)
    {
        if (entity == null) return null;

        var updatedAt = GetUpdatedAt(entity);
        return updatedAt?.ToString("O") ?? Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Report progress
    /// </summary>
    protected void ReportProgress(SyncOptions options, SyncPhase phase, int totalItems, int processedItems)
    {
        options.OnProgress?.Invoke(new SyncProgress
        {
            Phase = phase,
            TotalItems = totalItems,
            ProcessedItems = processedItems
        });
    }
}
