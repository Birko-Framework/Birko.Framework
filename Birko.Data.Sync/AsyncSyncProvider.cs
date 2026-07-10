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
using System.Threading.Tasks;

namespace Birko.Data.Sync;

/// <summary>
/// Main synchronization provider for asynchronous stores
/// </summary>
public class AsyncSyncProvider<TStore, T, TKnowledge> : SyncProviderBase<T, TKnowledge>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel
    where TKnowledge : Data.Models.AbstractModel, ISyncKnowledgeItem
{
    private readonly TStore _localStore;
    private readonly TStore _remoteStore;
    private readonly IAsyncSyncKnowledgeItemStore<TKnowledge> _knowledgeStore;

    /// <summary>
    /// Create a new sync provider
    /// </summary>
    public AsyncSyncProvider(TStore localStore, TStore remoteStore, IAsyncSyncKnowledgeItemStore<TKnowledge> knowledgeStore)
    {
        _localStore = localStore ?? throw new ArgumentNullException(nameof(localStore));
        _remoteStore = remoteStore ?? throw new ArgumentNullException(nameof(remoteStore));
        _knowledgeStore = knowledgeStore ?? throw new ArgumentNullException(nameof(knowledgeStore));
    }

    /// <summary>
    /// Preview sync changes without executing
    /// </summary>
    public async Task<SyncPreview> PreviewAsync(SyncOptions options, SyncFilterOptions<T>? filterOptions = null)
    {
        var preview = new SyncPreview { Scope = options.Scope };

        try
        {
            ReportProgress(options, SyncPhase.DetectingChanges, 0, 0);

            // Get existing sync knowledge
            var knowledge = (await _knowledgeStore.ReadAsync(k => k.Scope == options.Scope, null, null, null, options.CancellationToken))
                .ToDictionary(GetGuid, k => k);
            var lastSyncTime = await _knowledgeStore.GetLastSyncTimeAsync(options.Scope, options.CancellationToken);
            var isInitialSync = !lastSyncTime.HasValue;

            // Get all items from both stores
            var localItems = await GetAllItemsAsync(_localStore, filterOptions?.LocalFetchPredicate, options.CancellationToken);
            var remoteItems = await GetAllItemsAsync(_remoteStore, filterOptions?.RemoteFetchPredicate, options.CancellationToken);

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
        catch (OperationCanceledException)
        {
            // CR-M155: don't mask cancellation as a "conflict" — let it propagate.
            throw;
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
    public async Task<SyncResult> SyncAsync(SyncOptions options, SyncFilterOptions<T>? filterOptions = null)
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
            var knowledge = (await _knowledgeStore.ReadAsync(k => k.Scope == options.Scope, null, null, null, options.CancellationToken))
                .ToDictionary(GetGuid, k => k);
            var lastSyncTime = await _knowledgeStore.GetLastSyncTimeAsync(options.Scope, options.CancellationToken);
            var isInitialSync = !lastSyncTime.HasValue;
            result.IsInitialSync = isInitialSync;

            // For initial sync, always download first
            if (isInitialSync)
            {
                options.Direction = SyncDirection.Download;
            }

            // Get all items from both stores
            var localItems = await GetAllItemsAsync(_localStore, filterOptions?.LocalFetchPredicate, options.CancellationToken);
            var remoteItems = await GetAllItemsAsync(_remoteStore, filterOptions?.RemoteFetchPredicate, options.CancellationToken);

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

                var batchResult = await ProcessBatchAsync(
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

            // Persist sync knowledge. CreateKnowledgeItem returns items with a null store PK, so a
            // plain Update never inserts them and first-run knowledge was silently lost (CR-C18).
            // Split into inserts (no existing row → let the store assign a PK) and updates (reuse the
            // existing row's PK), so knowledge is durably upserted without creating duplicate rows.
            var knowledgeCreates = new List<TKnowledge>();
            var knowledgeUpdatesToApply = new List<TKnowledge>();
            foreach (var item in knowledgeUpdates)
            {
                if (knowledge.TryGetValue(GetGuid((ISyncKnowledgeItem)item), out var existing) && existing.Guid.HasValue)
                {
                    item.Guid = existing.Guid;
                    knowledgeUpdatesToApply.Add(item);
                }
                else
                {
                    item.Guid = null;
                    knowledgeCreates.Add(item);
                }
            }
            if (knowledgeCreates.Count > 0)
                await _knowledgeStore.CreateAsync(knowledgeCreates, null, options.CancellationToken);
            if (knowledgeUpdatesToApply.Count > 0)
                await _knowledgeStore.UpdateAsync(knowledgeUpdatesToApply, null, options.CancellationToken);
            await _knowledgeStore.SetLastSyncTimeAsync(options.Scope, DateTime.UtcNow, options.CancellationToken);

            // Fill result
            result.TotalProcessed = progress.ProcessedItems;
            result.Created = progress.CreatedItems;
            result.Updated = progress.UpdatedItems;
            result.Deleted = progress.DeletedItems;
            result.Skipped = progress.SkippedItems;
            result.Conflicts = progress.Conflicts;
            // Success is driven by errors only. The previous `|| IsCancellationRequested` flipped a
            // run that recorded per-item errors to Success=true whenever it was cancelled, hiding the
            // failures (CR-H099). Cancellation is reported separately via the result's own fields.
            result.Success = result.Errors.Count == 0;
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
    private async Task<BatchProcessResult> ProcessBatchAsync(
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
                                await _localStore.CreateAsync(remoteItem, null, options.CancellationToken);
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
                                await _remoteStore.CreateAsync(localItem, null, options.CancellationToken);
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
                            await _localStore.UpdateAsync(remoteItem, null, options.CancellationToken);
                            progress.UpdatedItems++;
                        }
                        else if (winner == "local" && localItem != null && CanSaveToRemote(localItem, filterOptions, options))
                        {
                            await _remoteStore.UpdateAsync(localItem, null, options.CancellationToken);
                            progress.UpdatedItems++;
                        }
                        break;

                    case SyncAction.Delete:
                        if (action.DeleteOn == "local" && localItem != null)
                        {
                            await _localStore.DeleteAsync(localItem, options.CancellationToken);
                            progress.DeletedItems++;
                        }
                        else if (action.DeleteOn == "remote" && remoteItem != null)
                        {
                            await _remoteStore.DeleteAsync(remoteItem, options.CancellationToken);
                            progress.DeletedItems++;
                        }
                        break;

                    case SyncAction.Skip:
                        progress.SkippedItems++;
                        break;

                    case SyncAction.Conflict:
                        progress.Conflicts++;
                        var resolution = ResolveConflict(action.Conflict!, options);
                        await ApplyConflictResolutionAsync(resolution, guid, localItem, remoteItem, options, filterOptions, progress);
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
    /// Apply conflict resolution asynchronously
    /// </summary>
    private async Task ApplyConflictResolutionAsync(
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
                        await _remoteStore.UpdateAsync(localItem, null, options.CancellationToken);
                        progress.UpdatedItems++;
                    }
                    break;

                case ConflictResolution.UseRemote when remoteItem != null:
                    if (localItem != null && CanSaveToLocal(remoteItem, filterOptions, options))
                    {
                        await _localStore.UpdateAsync(remoteItem, null, options.CancellationToken);
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
    private async Task<IEnumerable<T>> GetAllItemsAsync(TStore store, Expression<Func<T, bool>>? filter, CancellationToken cancellationToken)
    {
        return await store.ReadAsync(filter, null, null, null, cancellationToken);
    }
}
