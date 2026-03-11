using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using Birko.Data.Tenant.Models;
using Birko.Data.Stores;
using Birko.Data.Sync.Tenant.Models;

namespace Birko.Data.Sync.Tenant.Providers;

/// <summary>
/// Tenant-aware synchronization provider
/// Automatically uses current tenant from ITenantContext for all sync operations
/// </summary>
public class TenantSyncProvider<TStore, T> : ISyncProvider
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel
{
    private readonly TStore _localStore;
    private readonly TStore _remoteStore;
    private readonly ISyncKnowledgeStore _knowledgeStore;
    private readonly ITenantContext _tenantContext;
    private readonly PropertyInfo _guidProperty;
    private readonly PropertyInfo? _tenantIdProperty;

    /// <summary>
    /// Create a new tenant-aware sync provider
    /// </summary>
    public TenantSyncProvider(
        TStore localStore,
        TStore remoteStore,
        ISyncKnowledgeStore knowledgeStore,
        ITenantContext? tenantContext = null)
    {
        _localStore = localStore ?? throw new ArgumentNullException(nameof(localStore));
        _remoteStore = remoteStore ?? throw new ArgumentNullException(nameof(remoteStore));
        _knowledgeStore = knowledgeStore ?? throw new ArgumentNullException(nameof(knowledgeStore));
        _tenantContext = tenantContext ?? Data.Tenant.Models.Tenant.Current;

        // Get Guid property using reflection
        _guidProperty = typeof(T).GetProperty("Guid")
            ?? throw new InvalidOperationException($"Type {typeof(T).Name} must have a Guid property");

        // Get TenantId property if exists
        _tenantIdProperty = typeof(T).GetProperty("TenantId");
    }

    /// <summary>
    /// Preview sync with automatic tenant scoping
    /// </summary>
    public async Task<SyncPreview> PreviewAsync(
        SyncOptions? baseOptions = null,
        SyncFilterOptions<T>? filterOptions = null)
    {
        var options = baseOptions ?? new SyncOptions();

        // Ensure tenant context is applied
        options = ApplyTenantContext(options);

        // Add tenant filtering to filter options
        filterOptions = ApplyTenantFiltering(filterOptions);

        // Execute preview
        return await ExecutePreviewAsync(options, filterOptions);
    }

    /// <summary>
    /// Execute sync with automatic tenant scoping
    /// </summary>
    public async Task<SyncResult> SyncAsync(
        SyncOptions? baseOptions = null,
        SyncFilterOptions<T>? filterOptions = null)
    {
        var options = baseOptions ?? new SyncOptions();

        // Ensure tenant context is applied
        options = ApplyTenantContext(options);

        // Add tenant filtering to filter options
        filterOptions = ApplyTenantFiltering(filterOptions);

        // Execute sync
        return await ExecuteSyncAsync(options, filterOptions);
    }

    /// <summary>
    /// Apply tenant context to sync options
    /// </summary>
    private SyncOptions ApplyTenantContext(SyncOptions options)
    {
        // If options is already TenantSyncOptions, use it
        if (options is TenantSyncOptions tenantOptions)
        {
            // Only set if not explicitly provided
            if (!tenantOptions.TenantId.HasValue && _tenantContext.HasTenant)
            {
                tenantOptions.TenantId = _tenantContext.CurrentTenantId;
            }
            return tenantOptions;
        }

        // Otherwise, wrap in TenantSyncOptions
        var wrapped = new TenantSyncOptions
        {
            Direction = options.Direction,
            ConflictPolicy = options.ConflictPolicy,
            CustomConflictResolver = options.CustomConflictResolver,
            BatchSize = options.BatchSize,
            MaxItems = options.MaxItems,
            Scope = options.Scope,
            CancellationToken = options.CancellationToken,
            OnProgress = options.OnProgress,
            OnConflict = options.OnConflict,
            OnError = options.OnError,
            OnBatchStarting = options.OnBatchStarting,
            OnBatchCompleted = options.OnBatchCompleted,
            SkipPreview = options.SkipPreview,
            TenantId = _tenantContext.HasTenant ? _tenantContext.CurrentTenantId : null
        };
        return wrapped;
    }

    /// <summary>
    /// Get TenantId from options (returns null if not TenantSyncOptions)
    /// </summary>
    private Guid? GetTenantId(SyncOptions options)
    {
        return (options as TenantSyncOptions)?.TenantId;
    }

    /// <summary>
    /// Apply tenant filtering to filter options (only modifies save filters, not fetch predicates)
    /// </summary>
    private SyncFilterOptions<T> ApplyTenantFiltering(SyncFilterOptions<T>? filterOptions)
    {
        if (filterOptions == null)
        {
            filterOptions = new SyncFilterOptions<T>();
        }

        // Add tenant filtering if tenant context exists and entity has TenantId
        if (_tenantContext.HasTenant && _tenantIdProperty != null)
        {
            var currentTenantId = _tenantContext.CurrentTenantId!.Value;

            // Wrap existing save predicates with tenant check
            var canSaveLocal = filterOptions.CanSaveToLocal;
            var canSaveRemote = filterOptions.CanSaveToRemote;

            filterOptions.CanSaveToLocal = canSaveLocal == null
                ? (T item) => BelongsToTenant(item, currentTenantId)
                : (T item) => BelongsToTenant(item, currentTenantId) && canSaveLocal(item);

            filterOptions.CanSaveToRemote = canSaveRemote == null
                ? (T item) => BelongsToTenant(item, currentTenantId)
                : (T item) => BelongsToTenant(item, currentTenantId) && canSaveRemote(item);
        }

        return filterOptions;
    }

    /// <summary>
    /// Check if an item belongs to the specified tenant
    /// </summary>
    private bool BelongsToTenant(T item, Guid tenantId)
    {
        if (_tenantIdProperty == null)
        {
            return true;
        }

        var value = _tenantIdProperty.GetValue(item);
        if (value is Guid itemTenantId)
        {
            return itemTenantId == tenantId;
        }

        return true;
    }

    /// <summary>
    /// Get Guid from entity
    /// </summary>
    private Guid GetGuid(T entity)
    {
        var value = _guidProperty.GetValue(entity);
        return value is Guid guid ? guid : Guid.Empty;
    }

    /// <summary>
    /// Analyze a single item for preview
    /// </summary>
    private SyncItemPreview AnalyzeItem(
        Guid guid,
        Dictionary<Guid, T> localDict,
        Dictionary<Guid, T> remoteDict,
        Dictionary<Guid, ISyncKnowledgeItem> knowledge,
        bool isInitialSync)
    {
        var localExists = localDict.TryGetValue(guid, out var localItem);
        var remoteExists = remoteDict.TryGetValue(guid, out var remoteItem);
        knowledge.TryGetValue(guid, out var knowledgeItem);

        var preview = new SyncItemPreview
        {
            Guid = guid,
            LocalVersion = localItem != null ? GetVersionHash(localItem) : null,
            RemoteVersion = remoteItem != null ? GetVersionHash(remoteItem) : null
        };

        if (isInitialSync)
        {
            if (remoteExists && !localExists)
            {
                preview.Action = SyncAction.Create;
                preview.Reason = "Initial sync: download from remote";
            }
            else
            {
                preview.Action = SyncAction.Skip;
                preview.Reason = "Initial sync: already exists locally";
            }
            return preview;
        }

        if (!localExists && remoteExists)
        {
            preview.Action = SyncAction.Create;
            preview.Reason = "New item on remote";
        }
        else if (localExists && !remoteExists)
        {
            if (knowledgeItem?.IsRemoteDeleted == true)
            {
                preview.Action = SyncAction.Delete;
                preview.Reason = "Deleted remotely";
            }
            else
            {
                preview.Action = SyncAction.Create;
                preview.Reason = "New item on local";
            }
        }
        else if (localExists && remoteExists)
        {
            preview.Action = SyncAction.Update;
            preview.Reason = "Exists on both sides";
        }
        else
        {
            preview.Action = SyncAction.Skip;
            preview.Reason = "No changes";
        }

        return preview;
    }

    /// <summary>
    /// Execute preview operation
    /// </summary>
    private async Task<SyncPreview> ExecutePreviewAsync(
        SyncOptions options,
        SyncFilterOptions<T> filterOptions)
    {
        var preview = new SyncPreview { Scope = options.Scope };

        try
        {
            ReportProgress(options, SyncPhase.DetectingChanges, 0);

            var tenantId = GetTenantId(options);

            // Get existing sync knowledge
            var knowledge = await _knowledgeStore.GetKnowledgeAsync(
                options.Scope,
                tenantId,
                options.CancellationToken
            );

            var lastSyncTime = await _knowledgeStore.GetLastSyncTimeAsync(
                options.Scope,
                tenantId,
                options.CancellationToken
            );

            var isInitialSync = !lastSyncTime.HasValue;

            // Get items from both stores with filtering
            var localItems = await GetAllItemsAsync(_localStore, filterOptions.LocalFetchPredicate, options);
            var remoteItems = await GetAllItemsAsync(_remoteStore, filterOptions.RemoteFetchPredicate, options);

            var localDict = localItems.ToDictionary(GetGuid);
            var remoteDict = remoteItems.ToDictionary(GetGuid);

            // All unique GUIDs from both sides
            var allGuids = localDict.Keys.Union(remoteDict.Keys).ToList();

            foreach (var guid in allGuids)
            {
                if (options.CancellationToken.IsCancellationRequested)
                    break;

                var itemPreview = AnalyzeItem(guid, localDict, remoteDict, knowledge, isInitialSync);
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

                var totalCount = allGuids.Count > 0 ? allGuids.Count : 1;
                ReportProgress(options, SyncPhase.DetectingChanges,
                    (int)((preview.Items.Count / (double)totalCount) * 100));
            }

            return preview;
        }
        catch (Exception)
        {
            preview.Conflicts++;
            return preview;
        }
    }

    /// <summary>
    /// Execute sync operation
    /// </summary>
    private async Task<SyncResult> ExecuteSyncAsync(
        SyncOptions options,
        SyncFilterOptions<T> filterOptions)
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
            ReportProgress(options, SyncPhase.DetectingChanges, 0);

            var tenantId = GetTenantId(options);

            // Get existing sync knowledge
            var knowledge = await _knowledgeStore.GetKnowledgeAsync(
                options.Scope,
                tenantId,
                options.CancellationToken
            );

            var lastSyncTime = await _knowledgeStore.GetLastSyncTimeAsync(
                options.Scope,
                tenantId,
                options.CancellationToken
            );

            var isInitialSync = !lastSyncTime.HasValue;
            result.IsInitialSync = isInitialSync;

            // For initial sync, always download first
            if (isInitialSync)
            {
                options.Direction = SyncDirection.Download;
            }

            // Get items from both stores
            var localItems = await GetAllItemsAsync(_localStore, filterOptions.LocalFetchPredicate, options);
            var remoteItems = await GetAllItemsAsync(_remoteStore, filterOptions.RemoteFetchPredicate, options);

            var localDict = localItems.ToDictionary(GetGuid);
            var remoteDict = remoteItems.ToDictionary(GetGuid);
            progress.TotalItems = localDict.Count + remoteDict.Count;

            // All unique GUIDs from both sides
            var allGuids = localDict.Keys.Union(remoteDict.Keys).ToList();
            var knowledgeUpdates = new List<ISyncKnowledgeItem>();

            // Process in batches
            var totalProcessed = 0;
            for (var i = 0; i < allGuids.Count; i += options.BatchSize)
            {
                var batchGuids = allGuids.Skip(i).Take(options.BatchSize).ToList();

                foreach (var guid in batchGuids)
                {
                    if (options.CancellationToken.IsCancellationRequested)
                        break;

                    var localExists = localDict.TryGetValue(guid, out var localItem);
                    var remoteExists = remoteDict.TryGetValue(guid, out var remoteItem);
                    var hasKnowledge = knowledge.TryGetValue(guid, out var knowledgeItem);

                    var action = DetermineSyncAction(guid, localItem, remoteItem, knowledgeItem, isInitialSync, options);

                    try
                    {
                        switch (action.Action)
                        {
                            case SyncAction.Create:
                                if (options.Direction is SyncDirection.Download && remoteItem != null)
                                {
                                    if (filterOptions.CanSaveToLocal?.Invoke(remoteItem) != false)
                                    {
                                        await _localStore.CreateAsync(remoteItem, ct: options.CancellationToken);
                                        progress.CreatedItems++;
                                    }
                                    else
                                    {
                                        progress.SkippedItems++;
                                    }
                                }
                                else if (options.Direction is SyncDirection.Upload && localItem != null)
                                {
                                    if (filterOptions.CanSaveToRemote?.Invoke(localItem) != false)
                                    {
                                        await _remoteStore.CreateAsync(localItem, ct: options.CancellationToken);
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
                                if (winner == "remote" && remoteItem != null)
                                {
                                    if (filterOptions.CanSaveToLocal?.Invoke(remoteItem) != false)
                                    {
                                        await _localStore.UpdateAsync(remoteItem, ct: options.CancellationToken);
                                        progress.UpdatedItems++;
                                    }
                                }
                                else if (winner == "local" && localItem != null)
                                {
                                    if (filterOptions.CanSaveToRemote?.Invoke(localItem) != false)
                                    {
                                        await _remoteStore.UpdateAsync(localItem, ct: options.CancellationToken);
                                        progress.UpdatedItems++;
                                    }
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
                                // Apply conflict resolution
                                var resolution = ResolveConflict(action.Conflict!, options);
                                await ApplyConflictResolutionAsync(resolution, guid, localItem, remoteItem, filterOptions, progress);
                                break;
                        }

                        totalProcessed++;
                        progress.ProcessedItems++;

                        // Update knowledge
                        knowledgeUpdates.Add(CreateKnowledgeItem(guid, localItem, remoteItem, options));
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

                    var totalCount = allGuids.Count > 0 ? allGuids.Count : 1;
                    ReportProgress(options, SyncPhase.ApplyingChanges,
                        (int)((totalProcessed / (double)totalCount) * 100));
                }

                options.OnBatchCompleted?.Invoke(new SyncBatchResult
                {
                    BatchNumber = (i / options.BatchSize) + 1,
                    Processed = batchGuids.Count,
                    Errors = result.Errors.Skip(result.Errors.Count - progress.Errors).ToList()
                });
            }

            // Update sync knowledge
            await _knowledgeStore.UpdateKnowledgeAsync(knowledgeUpdates, options.CancellationToken);
            await _knowledgeStore.SetLastSyncTimeAsync(
                options.Scope,
                tenantId,
                DateTime.UtcNow,
                options.CancellationToken
            );

            // Fill result
            result.TotalProcessed = totalProcessed;
            result.Created = progress.CreatedItems;
            result.Updated = progress.UpdatedItems;
            result.Deleted = progress.DeletedItems;
            result.Skipped = progress.SkippedItems;
            result.Conflicts = progress.Conflicts;
            result.Success = result.Errors.Count == 0;
            result.EndTime = DateTime.UtcNow;
            result.Duration = result.EndTime - result.StartTime;

            ReportProgress(options, SyncPhase.Completed, 100);

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
            ReportProgress(options, SyncPhase.Failed, 0);
            return result;
        }
    }

    /// <summary>
    /// Get all items from a store with optional filtering
    /// </summary>
    private async Task<List<T>> GetAllItemsAsync(
        TStore store,
        Expression<Func<T, bool>>? predicate,
        SyncOptions options)
    {
        IEnumerable<T> items;
        if (predicate != null)
        {
            items = await store.ReadAsync(predicate, ct: options.CancellationToken);
        }
        else
        {
            items = await store.ReadAsync(options.CancellationToken);
        }
        return items.ToList();
    }

    /// <summary>
    /// Determine sync action for an item
    /// </summary>
    private (SyncAction Action, string? Winner, string? DeleteOn, ConflictInfo? Conflict) DetermineSyncAction(
        Guid guid,
        T? localItem,
        T? remoteItem,
        ISyncKnowledgeItem? knowledgeItem,
        bool isInitialSync,
        SyncOptions options)
    {
        var localExists = localItem != null;
        var remoteExists = remoteItem != null;

        // Initial sync: download everything
        if (isInitialSync)
        {
            if (remoteExists && !localExists)
                return (SyncAction.Create, null, null, null);
            return (SyncAction.Skip, null, null, null);
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

        // Bidirectional
        if (localExists && remoteExists)
        {
            var winner = GetWinner(localItem!, remoteItem!, options.ConflictPolicy);
            if (winner == "conflict")
            {
                return (SyncAction.Conflict, null, null, new ConflictInfo
                {
                    Guid = guid,
                    LocalItem = localItem,
                    RemoteItem = remoteItem,
                    Reason = "Both local and remote have been modified"
                });
            }
            return (SyncAction.Update, winner, null, null);
        }

        if (localExists && !remoteExists)
        {
            if (knowledgeItem?.IsRemoteDeleted == true)
            {
                return (SyncAction.Conflict, null, null, new ConflictInfo
                {
                    Guid = guid,
                    LocalItem = localItem,
                    RemoteItem = null,
                    Reason = "Modified locally but deleted remotely"
                });
            }
            return (SyncAction.Create, null, null, null);
        }

        if (!localExists && remoteExists)
        {
            if (knowledgeItem?.IsLocalDeleted == true)
            {
                return (SyncAction.Conflict, null, null, new ConflictInfo
                {
                    Guid = guid,
                    LocalItem = null,
                    RemoteItem = remoteItem,
                    Reason = "Modified remotely but deleted locally"
                });
            }
            return (SyncAction.Create, null, null, null);
        }

        return (SyncAction.Skip, null, null, null);
    }

    /// <summary>
    /// Get the winner of a conflict
    /// </summary>
    private string GetWinner(T local, T remote, ConflictResolutionPolicy policy)
    {
        return policy switch
        {
            ConflictResolutionPolicy.LocalWins => "local",
            ConflictResolutionPolicy.RemoteWins => "remote",
            ConflictResolutionPolicy.NewestWins => GetNewest(local, remote),
            _ => "local"
        };
    }

    /// <summary>
    /// Get the newer item based on UpdatedAt
    /// </summary>
    private string GetNewest(T local, T remote)
    {
        var localUpdatedAt = GetUpdatedAt(local);
        var remoteUpdatedAt = GetUpdatedAt(remote);

        if (localUpdatedAt.HasValue && remoteUpdatedAt.HasValue)
        {
            return localUpdatedAt.Value > remoteUpdatedAt.Value ? "local" : "remote";
        }

        return "local";
    }

    /// <summary>
    /// Resolve a conflict
    /// </summary>
    private ConflictResolution ResolveConflict(ConflictInfo conflict, SyncOptions options)
    {
        options.OnConflict?.Invoke(conflict);

        if (options.ConflictPolicy == ConflictResolutionPolicy.Custom &&
            options.CustomConflictResolver != null)
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
    private ConflictResolution GetNewestConflictResolution(ConflictInfo conflict)
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
    /// Apply conflict resolution
    /// </summary>
    private async Task ApplyConflictResolutionAsync(
        ConflictResolution resolution,
        Guid guid,
        T? localItem,
        T? remoteItem,
        SyncFilterOptions<T> filterOptions,
        SyncProgress progress)
    {
        try
        {
            switch (resolution)
            {
                case ConflictResolution.UseLocal when localItem != null:
                    if (filterOptions.CanSaveToRemote?.Invoke(localItem) != false)
                    {
                        await _remoteStore.UpdateAsync(localItem);
                        progress.UpdatedItems++;
                    }
                    break;

                case ConflictResolution.UseRemote when remoteItem != null:
                    if (filterOptions.CanSaveToLocal?.Invoke(remoteItem) != false)
                    {
                        await _localStore.UpdateAsync(remoteItem);
                        progress.UpdatedItems++;
                    }
                    break;

                case ConflictResolution.Skip:
                    progress.SkippedItems++;
                    break;
            }
        }
        catch (Exception)
        {
            // Error already handled in calling method
        }
    }

    /// <summary>
    /// Create sync knowledge item
    /// </summary>
    private ISyncKnowledgeItem CreateKnowledgeItem(
        Guid guid,
        T? localItem,
        T? remoteItem,
        SyncOptions options)
    {
        var tenantId = GetTenantId(options) ?? (_tenantContext.HasTenant ? _tenantContext.CurrentTenantId : Guid.Empty);

        return new TenantSyncKnowledgeItem
        {
            EntityGuid = guid,
            Scope = options.Scope,
            TenantId = tenantId ?? Guid.Empty,
            LastSyncedAt = DateTime.UtcNow,
            LocalVersion = GetVersionHash(localItem),
            RemoteVersion = GetVersionHash(remoteItem),
            IsLocalDeleted = localItem == null,
            IsRemoteDeleted = remoteItem == null
        };
    }

    /// <summary>
    /// Get UpdatedAt from entity
    /// </summary>
    private DateTime? GetUpdatedAt(T entity)
    {
        var prop = typeof(T).GetProperty("UpdatedAt");
        if (prop != null && prop.PropertyType == typeof(DateTime))
        {
            var value = prop.GetValue(entity);
            return value as DateTime?;
        }
        return null;
    }

    /// <summary>
    /// Get version hash for entity
    /// </summary>
    private string? GetVersionHash(T? entity)
    {
        if (entity == null) return null;

        var updatedAt = GetUpdatedAt(entity);
        return updatedAt?.ToString("O") ?? Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Report progress
    /// </summary>
    private void ReportProgress(SyncOptions options, SyncPhase phase, int percent)
    {
        options.OnProgress?.Invoke(new SyncProgress
        {
            Phase = phase,
            TotalItems = 100,
            ProcessedItems = percent
        });
    }

    /// <summary>
    /// Explicit ISyncProvider implementation with object? parameters.
    /// </summary>
    Task<SyncPreview> ISyncProvider.PreviewAsync(SyncOptions? options, object? filterOptions)
    {
        return PreviewAsync(options, filterOptions as SyncFilterOptions<T>);
    }

    /// <summary>
    /// Explicit ISyncProvider implementation with object? parameters.
    /// </summary>
    Task<SyncResult> ISyncProvider.SyncAsync(SyncOptions? options, object? filterOptions)
    {
        return SyncAsync(options, filterOptions as SyncFilterOptions<T>);
    }
}

/// <summary>
/// Minimal sync provider interface
/// </summary>
public interface ISyncProvider
{
    Task<SyncPreview> PreviewAsync(SyncOptions? options, object? filterOptions);
    Task<SyncResult> SyncAsync(SyncOptions? options, object? filterOptions);
}

/// <summary>
/// Extension methods for creating tenant-aware sync providers
/// </summary>
public static class TenantSyncProviderExtensions
{
    /// <summary>
    /// Create a complete tenant-aware sync setup
    /// </summary>
    public static TenantSyncSetup<TStore, T> CreateTenantSync<TStore, T>(
        this (TStore Local, TStore Remote) stores,
        ISyncKnowledgeStore knowledgeStore,
        ITenantContext? tenantContext = null)
        where TStore : IAsyncBulkStore<T>
        where T : Data.Models.AbstractModel
    {
        var syncProvider = new TenantSyncProvider<TStore, T>(
            stores.Local,
            stores.Remote,
            knowledgeStore,
            tenantContext
        );

        return new TenantSyncSetup<TStore, T>
        {
            SyncProvider = syncProvider,
            KnowledgeStore = knowledgeStore
        };
    }

    /// <summary>
    /// Wrap stores with tenant-aware sync
    /// </summary>
    public static TenantSyncProvider<TStore, T> WithTenantSync<TStore, T>(
        this TStore localStore,
        TStore remoteStore,
        ISyncKnowledgeStore knowledgeStore,
        ITenantContext? tenantContext = null)
        where TStore : IAsyncBulkStore<T>
        where T : Data.Models.AbstractModel
    {
        return new TenantSyncProvider<TStore, T>(
            localStore,
            remoteStore,
            knowledgeStore,
            tenantContext
        );
    }
}

/// <summary>
/// Complete tenant sync setup
/// </summary>
public class TenantSyncSetup<TStore, T>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel
{
    public required TenantSyncProvider<TStore, T> SyncProvider { get; init; }
    public required ISyncKnowledgeStore KnowledgeStore { get; init; }
}
