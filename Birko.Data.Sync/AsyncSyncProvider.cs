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

            var localDict = BuildEntityDictionary(localItems, "local");
            var remoteDict = BuildEntityDictionary(remoteItems, "remote");
            var allGuids = localDict.Keys.Union(remoteDict.Keys).ToList();
            // CR-L207: honor MaxItems by capping the set of Guids processed this run.
            if (options.MaxItems is int max && max >= 0 && allGuids.Count > max)
            {
                allGuids = allGuids.Take(max).ToList();
            }

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

            var localDict = BuildEntityDictionary(localItems, "local");
            var remoteDict = BuildEntityDictionary(remoteItems, "remote");
            progress.TotalItems = localDict.Count + remoteDict.Count;

            var allGuids = localDict.Keys.Union(remoteDict.Keys).ToList();
            // CR-L207: honor MaxItems by capping the set of Guids processed this run.
            if (options.MaxItems is int max && max >= 0 && allGuids.Count > max)
            {
                allGuids = allGuids.Take(max).ToList();
            }
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
            // SH-H012: these three used to carry options.CancellationToken. The batch loop above BREAKS
            // on cancellation rather than throwing, so on a cancelled run they were reached with an
            // already-cancelled token and threw immediately - the outer catch then recorded a generic
            // "Sync failed" and the knowledge for every item ALREADY WRITTEN to the stores by completed
            // batches was lost, leaving the next run to re-decide them blind. The writes have happened;
            // recording that they happened is not optional, and the synchronous SyncProvider twin has
            // always persisted unconditionally. Cancellation is still reported through the result.
            if (knowledgeCreates.Count > 0)
                await _knowledgeStore.CreateAsync(knowledgeCreates, null, CancellationToken.None);
            if (knowledgeUpdatesToApply.Count > 0)
                await _knowledgeStore.UpdateAsync(knowledgeUpdatesToApply, null, CancellationToken.None);
            await _knowledgeStore.SetLastSyncTimeAsync(options.Scope, DateTime.UtcNow, CancellationToken.None);

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

            localDict.TryGetValue(guid, out var localItem);
            remoteDict.TryGetValue(guid, out var remoteItem);
            knowledge.TryGetValue(guid, out var knowledgeItem);

            // Determine sync action
            var action = DetermineSyncAction(guid, localItem, remoteItem, knowledgeItem, isInitialSync, options, filterOptions);

            try
            {
                // SH-H011: the knowledge row written below must describe the state AFTER the action
                // was applied, not the pre-action dictionaries. Each arm updates the effective pair.
                var effectiveLocal = localItem;
                var effectiveRemote = remoteItem;

                switch (action.Action)
                {
                    case SyncAction.Create:
                        // SH-H008: the destination of a create is decided by which side is MISSING the
                        // item, never by options.Direction. The arms here used to test
                        // Direction == Download then == Upload with no else, so under the DEFAULT
                        // Bidirectional direction (SyncOptions.Direction) neither matched: the Create
                        // that DetermineSyncAction returns for its two one-sided Bidirectional branches
                        // was applied to nothing, while the item was still counted Processed and a
                        // knowledge row still written - not even SkippedItems moved, so the drop was
                        // invisible in every counter. Direction is already applied upstream
                        // (DetermineSyncAction only returns Create for remoteExists && !localExists
                        // under Download, and for the mirror under Upload), so dispatching on presence
                        // is behaviour-preserving for those two directions and is the only thing that
                        // works for Bidirectional and for the initial-sync branch.
                        if (remoteItem != null && localItem == null)
                        {
                            if (CanSaveToLocal(remoteItem, filterOptions, options))
                            {
                                await _localStore.CreateAsync(remoteItem, null, options.CancellationToken);
                                effectiveLocal = remoteItem;
                                progress.CreatedItems++;
                            }
                            else
                            {
                                progress.SkippedItems++;
                            }
                        }
                        else if (localItem != null && remoteItem == null)
                        {
                            if (CanSaveToRemote(localItem, filterOptions, options))
                            {
                                await _remoteStore.CreateAsync(localItem, null, options.CancellationToken);
                                effectiveRemote = localItem;
                                progress.CreatedItems++;
                            }
                            else
                            {
                                progress.SkippedItems++;
                            }
                        }
                        else
                        {
                            // Unreachable today: DetermineSyncAction never returns Create with both
                            // sides present or both absent. Counted rather than dropped so that a
                            // future shape reaching here is visible in the result instead of vanishing
                            // silently, which is the whole of SH-H008.
                            progress.SkippedItems++;
                        }
                        break;

                    case SyncAction.Update:
                        var winner = action.Winner;
                        if (winner == "remote" && remoteItem != null && CanSaveToLocal(remoteItem, filterOptions, options))
                        {
                            await _localStore.UpdateAsync(remoteItem, null, options.CancellationToken);
                            effectiveLocal = remoteItem;
                            progress.UpdatedItems++;
                        }
                        else if (winner == "local" && localItem != null && CanSaveToRemote(localItem, filterOptions, options))
                        {
                            await _remoteStore.UpdateAsync(localItem, null, options.CancellationToken);
                            effectiveRemote = localItem;
                            progress.UpdatedItems++;
                        }
                        break;

                    case SyncAction.Delete:
                        if (action.DeleteOn == "local" && localItem != null)
                        {
                            await _localStore.DeleteAsync(localItem, options.CancellationToken);
                            effectiveLocal = null;
                            progress.DeletedItems++;
                        }
                        else if (action.DeleteOn == "remote" && remoteItem != null)
                        {
                            await _remoteStore.DeleteAsync(remoteItem, options.CancellationToken);
                            effectiveRemote = null;
                            progress.DeletedItems++;
                        }
                        break;

                    case SyncAction.Skip:
                        progress.SkippedItems++;
                        break;

                    case SyncAction.Conflict:
                        progress.Conflicts++;
                        var resolution = ResolveConflict(action.Conflict!, options);
                        (effectiveLocal, effectiveRemote) = await ApplyConflictResolutionAsync(
                            resolution, guid, localItem, remoteItem, options, filterOptions, progress);
                        break;
                }

                result.Processed++;
                progress.ProcessedItems++;

                // Update knowledge. SH-H011: these were GetVersionHash(localItem)/(remoteItem) - the
                // PRE-action values - so after a create the destination's hash was still null and every
                // backend set IsLocal/IsRemoteDeleted = string.IsNullOrEmpty(hash) = true. Those flags
                // mean "absent when decided", but DetermineSyncAction's two delete branches read them as
                // "deleted", so the next run deleted a row that had just been created (SH-H009).
                knowledgeUpdates.Add(_knowledgeStore.CreateKnowledgeItem(guid, GetVersionHash(effectiveLocal), GetVersionHash(effectiveRemote), options));
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
    /// Apply conflict resolution, returning the (local, remote) pair as it stands after the write so the
    /// caller can record knowledge describing the post-action state (SH-H011).
    /// </summary>
    /// <remarks>
    /// SH-H010: every arm used to require BOTH items to be non-null - an outer
    /// <c>when localItem != null</c> and then an inner <c>if (remoteItem != null ...)</c>, and the mirror
    /// for UseRemote. But a conflict is only ever raised by the two one-sided-presence branches of
    /// <see cref="Internal.SyncProviderBase{T, TKnowledge}.DetermineSyncAction"/>, where the opposite item
    /// is null by construction, so no resolution could ever write anything: every conflict fell through
    /// with no store write and no counter change.
    /// <para>The one-sided arms below do exactly what the LocalWins / RemoteWins shortcuts in that same
    /// method already do for the identical state - re-create on the side that lost the row, or honour the
    /// deletion by dropping the surviving side - so a policy and its conflict path cannot disagree about
    /// what "local wins" means.</para>
    /// <para><c>ConflictResolution.Merge</c> is deliberately not handled here: nothing in the framework
    /// can merge two entities, so there is no mechanism to call. It falls through to the unchanged pair.</para>
    /// </remarks>
    private async Task<(T? Local, T? Remote)> ApplyConflictResolutionAsync(
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
                case ConflictResolution.UseLocal when localItem != null && remoteItem != null:
                    if (CanSaveToRemote(localItem, filterOptions, options))
                    {
                        await _remoteStore.UpdateAsync(localItem, null, options.CancellationToken);
                        progress.UpdatedItems++;
                        return (localItem, localItem);
                    }
                    break;

                case ConflictResolution.UseLocal when localItem != null:
                    // Modified locally, deleted remotely, local wins: re-create on the remote. Mirrors
                    // DetermineSyncAction's LocalWins shortcut for this state, which returns Create.
                    if (CanSaveToRemote(localItem, filterOptions, options))
                    {
                        await _remoteStore.CreateAsync(localItem, null, options.CancellationToken);
                        progress.CreatedItems++;
                        return (localItem, localItem);
                    }
                    break;

                case ConflictResolution.UseLocal when remoteItem != null:
                    // Deleted locally, modified remotely, local wins: the local deletion wins, so the
                    // remote row goes. Mirrors the LocalWins shortcut, which returns Delete on "remote".
                    await _remoteStore.DeleteAsync(remoteItem, options.CancellationToken);
                    progress.DeletedItems++;
                    return (null, null);

                case ConflictResolution.UseRemote when remoteItem != null && localItem != null:
                    if (CanSaveToLocal(remoteItem, filterOptions, options))
                    {
                        await _localStore.UpdateAsync(remoteItem, null, options.CancellationToken);
                        progress.UpdatedItems++;
                        return (remoteItem, remoteItem);
                    }
                    break;

                case ConflictResolution.UseRemote when remoteItem != null:
                    // Deleted locally, modified remotely, remote wins: re-create locally. Mirrors
                    // DetermineSyncAction's RemoteWins shortcut for this state, which returns Create.
                    if (CanSaveToLocal(remoteItem, filterOptions, options))
                    {
                        await _localStore.CreateAsync(remoteItem, null, options.CancellationToken);
                        progress.CreatedItems++;
                        return (remoteItem, remoteItem);
                    }
                    break;

                case ConflictResolution.UseRemote when localItem != null:
                    // Modified locally, deleted remotely, remote wins: the remote deletion wins, so the
                    // local row goes. Mirrors the RemoteWins shortcut, which returns Delete on "local".
                    await _localStore.DeleteAsync(localItem, options.CancellationToken);
                    progress.DeletedItems++;
                    return (null, null);

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

        // Nothing was written (filter blocked, Merge, Skip, or the catch above), so the pair is unchanged.
        return (localItem, remoteItem);
    }

    /// <summary>
    /// Get all items from a store with optional filtering
    /// </summary>
    private async Task<IEnumerable<T>> GetAllItemsAsync(TStore store, Expression<Func<T, bool>>? filter, CancellationToken cancellationToken)
    {
        return await store.ReadAsync(filter, null, null, null, cancellationToken);
    }
}
