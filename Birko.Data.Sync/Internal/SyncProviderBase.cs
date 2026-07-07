using Birko.Data.Sync.Models;
using System;
using System.Collections.Generic;
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
    /// Determine what sync action to take for an item
    /// </summary>
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
            if (!remoteExists && !localExists)
                return (SyncAction.Skip, null, null, null);
            return (SyncAction.Skip, null, null, null); // Already exists locally
        }

        // Download only
        if (options.Direction == SyncDirection.Download)
        {
            if (remoteExists && !localExists)
                return (SyncAction.Create, null, null, null);
            if (remoteExists && localExists)
                return (SyncAction.Update, "remote", null, null);
            if (!remoteExists && localExists && knowledgeItem?.IsRemoteDeleted == true)
                return (SyncAction.Delete, null, "local", null);
            return (SyncAction.Skip, null, null, null);
        }

        // Upload only
        if (options.Direction == SyncDirection.Upload)
        {
            if (localExists && !remoteExists)
                return (SyncAction.Create, null, null, null);
            if (localExists && remoteExists)
                return (SyncAction.Update, "local", null, null);
            if (!localExists && remoteExists && knowledgeItem?.IsLocalDeleted == true)
                return (SyncAction.Delete, null, "remote", null);
            return (SyncAction.Skip, null, null, null);
        }

        // Bidirectional - check for conflicts
        if (localExists && remoteExists)
        {
            // Both exist - could be conflict or just update needed
            // For now, use conflict resolution policy to determine winner
            var winner = GetWinner(localItem!, remoteItem!, options.ConflictPolicy);
            if (winner == "conflict")
            {
                var conflict = new ConflictInfo
                {
                    Guid = guid,
                    LocalItem = localItem,
                    RemoteItem = remoteItem,
                    Reason = "Both local and remote have been modified"
                };
                return (SyncAction.Conflict, null, null, conflict);
            }
            return (SyncAction.Update, winner, null, null);
        }

        if (localExists && !remoteExists)
        {
            // Exists locally, not remotely - was it deleted remotely?
            if (knowledgeItem?.IsRemoteDeleted == true)
            {
                // Conflict: local modified vs remote deleted
                if (options.ConflictPolicy == ConflictResolutionPolicy.RemoteWins)
                    return (SyncAction.Delete, null, "local", null);
                if (options.ConflictPolicy == ConflictResolutionPolicy.LocalWins)
                    return (SyncAction.Create, null, null, null); // Re-create on remote

                var conflict = new ConflictInfo
                {
                    Guid = guid,
                    LocalItem = localItem,
                    RemoteItem = null,
                    Reason = "Modified locally but deleted remotely"
                };
                return (SyncAction.Conflict, null, null, conflict);
            }
            return (SyncAction.Create, null, null, null); // New item to upload
        }

        if (!localExists && remoteExists)
        {
            // Exists remotely, not locally - was it deleted locally?
            if (knowledgeItem?.IsLocalDeleted == true)
            {
                // Conflict: remote modified vs local deleted
                if (options.ConflictPolicy == ConflictResolutionPolicy.LocalWins)
                    return (SyncAction.Delete, null, "remote", null);
                if (options.ConflictPolicy == ConflictResolutionPolicy.RemoteWins)
                    return (SyncAction.Create, null, null, null); // Re-create locally

                var conflict = new ConflictInfo
                {
                    Guid = guid,
                    LocalItem = null,
                    RemoteItem = remoteItem,
                    Reason = "Modified remotely but deleted locally"
                };
                return (SyncAction.Conflict, null, null, conflict);
            }
            return (SyncAction.Create, null, null, null); // New item to download
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
            SyncAction.Delete when knowledgeItem?.IsLocalDeleted == true => "Deleted locally",
            SyncAction.Delete when knowledgeItem?.IsRemoteDeleted == true => "Deleted remotely",
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
