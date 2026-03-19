using Birko.Data.Stores;
using Birko.Configuration;
using Birko.Data.Sync.Internal;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;

namespace Birko.Data.Sync;

/// <summary>
/// Main synchronization provider for synchronous stores
/// </summary>
public class SyncProvider<TStore, T, TKnowledge> : SyncProviderBase<T, TKnowledge>
    where TStore : IBulkStore<T>
    where T : Data.Models.AbstractModel
    where TKnowledge : Data.Models.AbstractModel, ISyncKnowledgeItem
{
    private readonly TStore _localStore;
    private readonly TStore _remoteStore;
    private readonly ISyncKnowledgeItemStore<TKnowledge> _knowledgeStore;

    /// <summary>
    /// Create a new sync provider
    /// </summary>
    public SyncProvider(TStore localStore, TStore remoteStore, ISyncKnowledgeItemStore<TKnowledge> knowledgeStore)
    {
        _localStore = localStore ?? throw new ArgumentNullException(nameof(localStore));
        _remoteStore = remoteStore ?? throw new ArgumentNullException(nameof(remoteStore));
        _knowledgeStore = knowledgeStore ?? throw new ArgumentNullException(nameof(knowledgeStore));
    }

    /// <summary>
    /// Preview sync changes without executing
    /// </summary>
    public SyncPreview Preview(SyncOptions options, SyncFilterOptions<T>? filterOptions = null)
    {
        var preview = new SyncPreview { Scope = options.Scope };

        try
        {
            ReportProgress(options, SyncPhase.DetectingChanges, 0, 0);

            // Get existing sync knowledge
            var knowledge = _knowledgeStore.Read(k => k.Scope == options.Scope, null, null, null)
                .ToDictionary(GetGuid, k => k);
            var lastSyncTime = _knowledgeStore.GetLastSyncTime(options.Scope);
            var isInitialSync = !lastSyncTime.HasValue;

            // Get all items from both stores
            var localItems = GetAllItems(_localStore, filterOptions?.LocalFetchPredicate, options.CancellationToken);
            var remoteItems = GetAllItems(_remoteStore, filterOptions?.RemoteFetchPredicate, options.CancellationToken);

            var localDict = localItems.ToDictionary(GetGuid);
            var remoteDict = remoteItems.ToDictionary(GetGuid);
            var allGuids = localDict.Keys.Union(remoteDict.Keys).ToList();

            foreach (var guid in allGuids)
            {
                if (options.CancellationToken.IsCancellationRequested)
                    break;

                var itemPreview = AnalyzeItem(guid, localDict, remoteDict, knowledge, isInitialSync, options, filterOptions);
                preview.Items.Add(itemPreview);

                // Update counters
                switch (itemPreview.Action)
                {
                    case SyncAction.Create: preview.ToCreate++; break;
                    case SyncAction.Update: preview.ToUpdate++; break;
                    case SyncAction.Delete: preview.ToDelete++; break;
                    case SyncAction.Skip: preview.Skipped++; break;
                    case SyncAction.Conflict: preview.Conflicts++; break;
                }

                ReportProgress(options, SyncPhase.DetectingChanges, 0, preview.Items.Count);
            }

            return preview;
        }
        catch
        {
            preview.Conflicts++; // Mark as failed
            return preview;
        }
    }

    /// <summary>
    /// Execute synchronization
    /// </summary>
    public SyncResult Sync(SyncOptions options, SyncFilterOptions<T>? filterOptions = null)
    {
        var startTime = DateTime.UtcNow;
        var result = new SyncResult
        {
            StartTime = startTime,
            Scope = options.Scope,
            Direction = options.Direction
        };

        var progress = new SyncProgress();

        try
        {
            ReportProgress(options, SyncPhase.DetectingChanges, 0, 0);

            // Get existing sync knowledge
            var knowledge = _knowledgeStore.Read(k => k.Scope == options.Scope, null, null, null)
                .ToDictionary(GetGuid, k => k);
            var lastSyncTime = _knowledgeStore.GetLastSyncTime(options.Scope);
            var isInitialSync = !lastSyncTime.HasValue;
            result.IsInitialSync = isInitialSync;

            // For initial sync, always download first
            if (isInitialSync)
            {
                options.Direction = SyncDirection.Download;
            }

            // Get all items from both stores
            var localItems = GetAllItems(_localStore, filterOptions?.LocalFetchPredicate, options.CancellationToken);
            var remoteItems = GetAllItems(_remoteStore, filterOptions?.RemoteFetchPredicate, options.CancellationToken);

            var localDict = localItems.ToDictionary(GetGuid);
            var remoteDict = remoteItems.ToDictionary(GetGuid);
            progress.TotalItems = localDict.Count + remoteDict.Count;

            var allGuids = localDict.Keys.Union(remoteDict.Keys).ToList();
            var knowledgeUpdates = new List<TKnowledge>();

            // Process in batches
            for (var i = 0; i < allGuids.Count; i += options.BatchSize)
            {
                var batchGuids = allGuids.Skip(i).Take(options.BatchSize).ToList();
                var batchNumber = (i / options.BatchSize) + 1;

                options.OnBatchStarting?.Invoke(batchNumber);

                var batchResult = ProcessBatch(
                    batchGuids,
                    localDict,
                    remoteDict,
                    knowledge,
                    isInitialSync,
                    options,
                    filterOptions,
                    progress
                );

                knowledgeUpdates.AddRange(batchResult.KnowledgeUpdates);
                result.Errors.AddRange(batchResult.Errors);

                options.OnBatchCompleted?.Invoke(new SyncBatchResult
                {
                    BatchNumber = batchNumber,
                    Processed = batchResult.Processed,
                    Errors = batchResult.Errors
                });

                ReportProgress(options, SyncPhase.ApplyingChanges, allGuids.Count, progress.ProcessedItems);

                if (options.CancellationToken.IsCancellationRequested)
                    break;
            }

            // Update sync knowledge
            _knowledgeStore.Update(knowledgeUpdates, null);
            _knowledgeStore.SetLastSyncTime(options.Scope, DateTime.UtcNow);

            // Fill result
            result.TotalProcessed = progress.ProcessedItems;
            result.Created = progress.CreatedItems;
            result.Updated = progress.UpdatedItems;
            result.Deleted = progress.DeletedItems;
            result.Skipped = progress.SkippedItems;
            result.Conflicts = progress.Conflicts;
            result.Success = result.Errors.Count == 0 || options.CancellationToken.IsCancellationRequested;
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;

            ReportProgress(options, SyncPhase.Completed, allGuids.Count, progress.ProcessedItems);

            return result;
        }
        catch (Exception ex)
        {
            result.Errors.Add(new SyncError
            {
                Message = "Sync failed",
                Details = ex.Message,
                Exception = ex
            });
            result.Success = false;
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;
            ReportProgress(options, SyncPhase.Failed, 0, 0);
            return result;
        }
    }

    /// <summary>
    /// Process a batch of items
    /// </summary>
    private BatchProcessResult ProcessBatch(
        List<Guid> guids,
        Dictionary<Guid, T> localDict,
        Dictionary<Guid, T> remoteDict,
        Dictionary<Guid, TKnowledge> knowledge,
        bool isInitialSync,
        SyncOptions options,
        SyncFilterOptions<T>? filterOptions,
        SyncProgress progress)
    {
        var result = new BatchProcessResult();
        var knowledgeUpdates = new List<TKnowledge>();

        foreach (var guid in guids)
        {
            if (options.CancellationToken.IsCancellationRequested)
                break;

            var localExists = localDict.TryGetValue(guid, out var localItem);
            var remoteExists = remoteDict.TryGetValue(guid, out var remoteItem);
            var hasKnowledge = knowledge.TryGetValue(guid, out var knowledgeItem);

            // Determine sync action
            var action = DetermineSyncAction(guid, localItem, remoteItem, knowledgeItem, isInitialSync, options, filterOptions);

            try
            {
                switch (action.Action)
                {
                    case SyncAction.Create:
                        if (options.Direction == SyncDirection.Download && remoteItem != null)
                        {
                            if (CanSaveToLocal(remoteItem, filterOptions, options))
                            {
                                _localStore.Create(remoteItem, null);
                                progress.CreatedItems++;
                            }
                            else
                            {
                                progress.SkippedItems++;
                            }
                        }
                        else if (options.Direction == SyncDirection.Upload && localItem != null)
                        {
                            if (CanSaveToRemote(localItem, filterOptions, options))
                            {
                                _remoteStore.Create(localItem, null);
                                progress.CreatedItems++;
                            }
                            else
                            {
                                progress.SkippedItems++;
                            }
                        }
                        break;

                    case SyncAction.Update:
                        var winner = action.Winner;
                        if (winner == "remote" && remoteItem != null && CanSaveToLocal(remoteItem, filterOptions, options))
                        {
                            _localStore.Update(remoteItem, null);
                            progress.UpdatedItems++;
                        }
                        else if (winner == "local" && localItem != null && CanSaveToRemote(localItem, filterOptions, options))
                        {
                            _remoteStore.Update(localItem, null);
                            progress.UpdatedItems++;
                        }
                        break;

                    case SyncAction.Delete:
                        if (action.DeleteOn == "local" && localItem != null)
                        {
                            _localStore.Delete(localItem);
                            progress.DeletedItems++;
                        }
                        else if (action.DeleteOn == "remote" && remoteItem != null)
                        {
                            _remoteStore.Delete(remoteItem);
                            progress.DeletedItems++;
                        }
                        break;

                    case SyncAction.Skip:
                        progress.SkippedItems++;
                        break;

                    case SyncAction.Conflict:
                        progress.Conflicts++;
                        var resolution = ResolveConflict(action.Conflict!, options);
                        ApplyConflictResolution(resolution, guid, localItem, remoteItem, options, filterOptions, progress);
                        break;
                }

                result.Processed++;
                progress.ProcessedItems++;

                // Update knowledge
                knowledgeUpdates.Add(_knowledgeStore.CreateKnowledgeItem(guid, GetVersionHash(localItem), GetVersionHash(remoteItem), options));
            }
            catch (Exception ex)
            {
                result.Errors.Add(new SyncError
                {
                    ItemGuid = guid,
                    Operation = action.Action.ToString(),
                    Message = $"Failed to sync item {guid}",
                    Details = ex.Message,
                    Exception = ex
                });
                progress.Errors++;
            }
        }

        result.KnowledgeUpdates = knowledgeUpdates;
        return result;
    }

    /// <summary>
    /// Apply conflict resolution
    /// </summary>
    private void ApplyConflictResolution(
        ConflictResolution resolution,
        Guid guid,
        T? localItem,
        T? remoteItem,
        SyncOptions options,
        SyncFilterOptions<T>? filterOptions,
        SyncProgress progress)
    {
        try
        {
            switch (resolution)
            {
                case ConflictResolution.UseLocal when localItem != null:
                    if (remoteItem != null && CanSaveToRemote(localItem, filterOptions, options))
                    {
                        _remoteStore.Update(localItem, null);
                        progress.UpdatedItems++;
                    }
                    break;

                case ConflictResolution.UseRemote when remoteItem != null:
                    if (localItem != null && CanSaveToLocal(remoteItem, filterOptions, options))
                    {
                        _localStore.Update(remoteItem, null);
                        progress.UpdatedItems++;
                    }
                    break;

                case ConflictResolution.Skip:
                    progress.SkippedItems++;
                    break;
            }
        }
        catch (Exception ex)
        {
            options.OnError?.Invoke(new SyncError
            {
                ItemGuid = guid,
                Operation = "ConflictResolution",
                Message = "Failed to apply conflict resolution",
                Details = ex.Message,
                Exception = ex
            });
        }
    }

    /// <summary>
    /// Get all items from a store with optional filtering
    /// </summary>
    private IEnumerable<T> GetAllItems(TStore store, Expression<Func<T, bool>>? filter, CancellationToken cancellationToken)
    {
        return store.Read(filter, null, null, null);
    }
}
