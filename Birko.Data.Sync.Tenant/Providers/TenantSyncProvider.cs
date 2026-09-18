using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.Expressions;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using Birko.Data.Stores;
using Birko.Configuration;
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
    private readonly PropertyInfo? _tenantGuidProperty;

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

        // Get TenantGuid property if exists
        _tenantGuidProperty = typeof(T).GetProperty("TenantGuid");
    }

    /// <summary>
    /// Preview sync with automatic tenant scoping
    /// </summary>
    public async Task<SyncPreview> PreviewAsync(
        SyncOptions? baseOptions = null,
        SyncFilterOptions<T>? filterOptions = null)
    {
        var options = baseOptions ?? new SyncOptions();

        // Resolve the run's tenant ONCE (SH-H050) and scope everything to that one answer.
        var tenantGuid = ResolveTenantScope(options);

        // Add tenant filtering to filter options — fetch predicates included (SH-H052)
        filterOptions = ApplyTenantFiltering(filterOptions, tenantGuid);

        // Execute preview
        return await ExecutePreviewAsync(options, tenantGuid, filterOptions);
    }

    /// <summary>
    /// Execute sync with automatic tenant scoping
    /// </summary>
    public async Task<SyncResult> SyncAsync(
        SyncOptions? baseOptions = null,
        SyncFilterOptions<T>? filterOptions = null)
    {
        var options = baseOptions ?? new SyncOptions();

        // Resolve the run's tenant ONCE (SH-H050) and scope everything to that one answer.
        var tenantGuid = ResolveTenantScope(options);

        // Add tenant filtering to filter options — fetch predicates included (SH-H052)
        filterOptions = ApplyTenantFiltering(filterOptions, tenantGuid);

        // Execute sync
        return await ExecuteSyncAsync(options, tenantGuid, filterOptions);
    }

    /// <summary>
    /// Resolve the single tenant this run is scoped to. Called <b>once</b> per
    /// <see cref="PreviewAsync"/> / <see cref="SyncAsync"/>; every downstream decision — fetch
    /// predicates, save predicates, knowledge keys, last-sync keys — takes its tenant from the
    /// returned value and from nowhere else.
    /// </summary>
    /// <remarks>
    /// <para><b>SH-H050 — two live answers were the bug.</b> The knowledge/last-sync calls read
    /// <c>options.TenantGuid</c> while the save filters read <c>_tenantContext.CurrentTenantGuid</c>, so
    /// <c>SyncAsync(new TenantSyncOptions { TenantGuid = u })</c> with no ambient tenant — the documented
    /// background-job shape — keyed knowledge to <i>u</i> while installing <b>no save predicate at all</b>,
    /// writing every tenant's items into both stores. Whichever source wins, the mismatch is the defect.</para>
    /// <para><b>Precedence.</b> Explicit <c>options.TenantGuid</c> is the answer when it is the only one
    /// present, and the ambient tenant is the answer when it is the only one present. When <b>both</b> are
    /// present and <b>disagree</b> the run is <b>refused</b> rather than silently resolved: code running
    /// inside tenant <i>t</i>'s scope asking to sync tenant <i>u</i> is a cross-tenant escalation, and the
    /// family has already paid for guessing here once (the <c>X-Tenant-Id</c>-vs-<c>tenant_id</c> guard,
    /// SH-H048). The deliberate cross-tenant caller says so by wrapping the call in
    /// <see cref="ITenantContext.WithAllTenantsAsync(Func{Task})"/>, under which the explicit option wins —
    /// that is the per-tenant admin loop.</para>
    /// <para><b>No tenant from either source throws</b> for a tenant-scoped entity type, instead of syncing
    /// every tenant. An entity type with no <c>TenantGuid</c> property is not tenant-scoped and is
    /// unaffected (there is no tenant to scope it by), matching <see cref="BelongsToTenant"/>'s allow-all
    /// path; and an explicit all-tenants scope is the sanctioned way to ask for every tenant on purpose.</para>
    /// </remarks>
    private Guid? ResolveTenantScope(SyncOptions options)
    {
        var fromOptions = (options as TenantSyncOptions)?.TenantGuid;
        var fromContext = _tenantContext.HasTenant ? _tenantContext.CurrentTenantGuid : null;
        var allTenants = _tenantContext.IsAllTenantsScope;

        if (fromOptions.HasValue && fromContext.HasValue && fromOptions.Value != fromContext.Value && !allTenants)
        {
            throw new TenantMismatchException("sync", typeof(T).Name, fromContext, fromOptions);
        }

        var resolved = fromOptions ?? fromContext;

        if (!resolved.HasValue && _tenantGuidProperty != null && !allTenants)
        {
            throw new TenantScopeRequiredException(
                "sync",
                typeof(T).Name,
                $"Cannot sync {typeof(T).Name}: no tenant is in scope. Set TenantSyncOptions.TenantGuid, "
                    + "establish an ambient tenant, or run inside an explicit all-tenants scope to sync every tenant on purpose.");
        }

        return resolved;
    }

    /// <summary>
    /// Apply tenant filtering to filter options — to the <b>fetch</b> predicates as well as the save
    /// predicates.
    /// </summary>
    /// <remarks>
    /// <para><b>SH-H051 / SH-H052 — the tenant term belongs on the fetch.</b> This method used to wrap only
    /// <c>CanSaveToLocal</c>/<c>CanSaveToRemote</c>, and said so in this doc comment. Everything else
    /// followed: every tenant's rows entered <c>localDict</c>/<c>remoteDict</c>, so preview enumerated and
    /// version-hashed other tenants' entities, the knowledge store recorded their guids, and the
    /// <c>SyncAction.Delete</c> arm — which consulted no predicate at all — deleted them. Scoping the fetch
    /// makes the read, compare, preview, delete and knowledge paths correct <i>by construction</i> rather
    /// than needing a guard each, which is what stops a sixth path being found later.</para>
    /// <para>The save predicates stay wrapped as defence in depth: a fetch predicate is only as good as the
    /// backend's translation of it, and this family has shipped a filter that a backend silently degraded to
    /// match-all more than once.</para>
    /// <para><b>Returns a copy; never mutates the caller's instance.</b> The scoping terms are per-run, and
    /// the per-tenant admin loop reuses one <see cref="SyncFilterOptions{T}"/> across every iteration:
    /// <c>foreach (t) SyncAsync(new TenantSyncOptions { TenantGuid = t }, filterOptions)</c>. Writing the
    /// terms back onto that shared object would conjoin each tenant onto the last — <c>t1 &amp;&amp; t2</c>
    /// matches nothing — so the loop would silently sync one tenant and then nothing at all. Same species as
    /// CR-M168 (mutating the caller's <c>SyncOptions.Direction</c>), which is why the resolved tenant is
    /// never written back onto the options either.</para>
    /// </remarks>
    private SyncFilterOptions<T> ApplyTenantFiltering(SyncFilterOptions<T>? filterOptions, Guid? tenantGuid)
    {
        var scoped = new SyncFilterOptions<T>
        {
            LocalFetchPredicate = filterOptions?.LocalFetchPredicate,
            RemoteFetchPredicate = filterOptions?.RemoteFetchPredicate,
            CanSaveToLocal = filterOptions?.CanSaveToLocal,
            CanSaveToRemote = filterOptions?.CanSaveToRemote,
            OnSaveFilterBlock = filterOptions?.OnSaveFilterBlock ?? new SyncFilterOptions().OnSaveFilterBlock
        };

        // No tenant in scope (an explicit all-tenants run), or an entity type that is not tenant-scoped.
        if (!tenantGuid.HasValue || _tenantGuidProperty == null)
        {
            return scoped;
        }

        var scopedTenantGuid = tenantGuid.Value;

        // Narrow what is FETCHED, so no foreign entity ever reaches the compare/delete/knowledge paths.
        var tenantPredicate = BuildTenantPredicate(scopedTenantGuid);
        if (tenantPredicate != null)
        {
            scoped.LocalFetchPredicate =
                ExpressionParameterReplacer.AndAlso(scoped.LocalFetchPredicate, tenantPredicate);
            scoped.RemoteFetchPredicate =
                ExpressionParameterReplacer.AndAlso(scoped.RemoteFetchPredicate, tenantPredicate);
        }

        // Wrap existing save predicates with tenant check
        var canSaveLocal = scoped.CanSaveToLocal;
        var canSaveRemote = scoped.CanSaveToRemote;

        scoped.CanSaveToLocal = canSaveLocal == null
            ? (T item) => BelongsToTenant(item, scopedTenantGuid)
            : (T item) => BelongsToTenant(item, scopedTenantGuid) && canSaveLocal(item);

        scoped.CanSaveToRemote = canSaveRemote == null
            ? (T item) => BelongsToTenant(item, scopedTenantGuid)
            : (T item) => BelongsToTenant(item, scopedTenantGuid) && canSaveRemote(item);

        return scoped;
    }

    /// <summary>
    /// Build <c>x =&gt; x.TenantGuid == tenantGuid</c> over the reflected <c>TenantGuid</c> property, in the
    /// same shape <c>ModelByTenant</c> emits so every backend's filter translator already handles it.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> when the property is not a <see cref="Guid"/> or <see cref="Nullable{Guid}"/> —
    /// nothing can be expressed about it, and <see cref="BelongsToTenant"/> already excludes every such row
    /// from the save paths, so the post-fetch guard in <see cref="GetAllItemsAsync"/> keeps the run
    /// fail-closed rather than widening it.
    /// </remarks>
    private Expression<Func<T, bool>>? BuildTenantPredicate(Guid tenantGuid)
    {
        if (_tenantGuidProperty == null)
        {
            return null;
        }

        var parameter = Expression.Parameter(typeof(T), "x");
        Expression access = Expression.Property(parameter, _tenantGuidProperty);
        var propertyType = _tenantGuidProperty.PropertyType;

        Expression body;
        if (propertyType == typeof(Guid))
        {
            body = Expression.Equal(access, Expression.Constant(tenantGuid, typeof(Guid)));
        }
        else if (Nullable.GetUnderlyingType(propertyType) == typeof(Guid))
        {
            // liftToNull: false keeps the node's type bool (C#'s own `==` on two Guid? does the same), so an
            // unset TenantGuid is excluded rather than yielding a null result — matching BelongsToTenant.
            body = Expression.Equal(
                access, Expression.Constant((Guid?)tenantGuid, typeof(Guid?)), liftToNull: false, method: null);
        }
        else
        {
            return null;
        }

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    /// <summary>
    /// Check if an item belongs to the specified tenant
    /// </summary>
    private bool BelongsToTenant(T item, Guid tenantGuid)
    {
        if (_tenantGuidProperty == null)
        {
            return true;
        }

        var value = _tenantGuidProperty.GetValue(item);
        if (value is Guid itemTenantGuid)
        {
            return itemTenantGuid == tenantGuid;
        }

        // CR-M169: the entity HAS a TenantGuid property but no matching value (null / not a Guid).
        // Excluding it preserves tenant isolation — defaulting to allow let an unset-tenant entity sync
        // into/out of every tenant's scope. The allow-all path above is reserved for entity TYPES that
        // have no TenantGuid property at all.
        return false;
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
        Guid? tenantGuid,
        SyncFilterOptions<T> filterOptions)
    {
        var preview = new SyncPreview { Scope = options.Scope };

        try
        {
            ReportProgress(options, SyncPhase.DetectingChanges, 0);

            // Get existing sync knowledge
            var knowledge = await _knowledgeStore.GetKnowledgeAsync(
                options.Scope,
                tenantGuid,
                options.CancellationToken
            );

            var lastSyncTime = await _knowledgeStore.GetLastSyncTimeAsync(
                options.Scope,
                tenantGuid,
                options.CancellationToken
            );

            var isInitialSync = !lastSyncTime.HasValue;

            // Get items from both stores with filtering
            var localItems = await GetAllItemsAsync(_localStore, filterOptions.LocalFetchPredicate, tenantGuid, options);
            var remoteItems = await GetAllItemsAsync(_remoteStore, filterOptions.RemoteFetchPredicate, tenantGuid, options);

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
            // CR-M167: previously swallowed every failure (store read error, reflection, cancellation)
            // as a spurious Conflicts++ on a partially-populated preview — so a thrown error was
            // indistinguishable from a genuine conflict and the failure was otherwise invisible.
            // Let it propagate so the caller sees the real error.
            throw;
        }
    }

    /// <summary>
    /// Execute sync operation
    /// </summary>
    private async Task<SyncResult> ExecuteSyncAsync(
        SyncOptions options,
        Guid? tenantGuid,
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

            // Get existing sync knowledge
            var knowledge = await _knowledgeStore.GetKnowledgeAsync(
                options.Scope,
                tenantGuid,
                options.CancellationToken
            );

            var lastSyncTime = await _knowledgeStore.GetLastSyncTimeAsync(
                options.Scope,
                tenantGuid,
                options.CancellationToken
            );

            var isInitialSync = !lastSyncTime.HasValue;
            result.IsInitialSync = isInitialSync;

            // CR-M168: compute an effective direction locally instead of mutating the caller-supplied
            // options object (ApplyTenantContext may return the same instance, so the mutation leaked
            // out and persisted after the call). Initial sync always downloads first, and result.Direction
            // must report what actually ran — not the pre-override value.
            var effectiveDirection = isInitialSync ? SyncDirection.Download : options.Direction;
            result.Direction = effectiveDirection;

            // Get items from both stores
            var localItems = await GetAllItemsAsync(_localStore, filterOptions.LocalFetchPredicate, tenantGuid, options);
            var remoteItems = await GetAllItemsAsync(_remoteStore, filterOptions.RemoteFetchPredicate, tenantGuid, options);

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
                // CR-L222: stop the OUTER batch loop on cancellation, not only the inner item loop.
                // Previously the inner `break` left this `for` running, so cancellation only paused the
                // current batch's items and the sync kept iterating (and firing OnBatchCompleted for)
                // every remaining batch instead of stopping promptly.
                if (options.CancellationToken.IsCancellationRequested)
                    break;

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
                                if (effectiveDirection is SyncDirection.Download && remoteItem != null)
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
                                else if (effectiveDirection is SyncDirection.Upload && localItem != null)
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

                            // SH-H051: consult the save predicate before deleting, exactly as the Create and
                            // Update arms above do. This arm used to consult nothing at all, so with the
                            // fetches unscoped an item belonging to tenant u that resolved to Delete under a
                            // run scoped to t was deleted outright. The fetches are scoped now, which is the
                            // real fix; this stays as defence in depth on the one irreversible path.
                            case SyncAction.Delete:
                                if (action.DeleteOn == "local" && localItem != null)
                                {
                                    if (filterOptions.CanSaveToLocal?.Invoke(localItem) != false)
                                    {
                                        await _localStore.DeleteAsync(localItem, options.CancellationToken);
                                        progress.DeletedItems++;
                                    }
                                    else
                                    {
                                        progress.SkippedItems++;
                                    }
                                }
                                else if (action.DeleteOn == "remote" && remoteItem != null)
                                {
                                    if (filterOptions.CanSaveToRemote?.Invoke(remoteItem) != false)
                                    {
                                        await _remoteStore.DeleteAsync(remoteItem, options.CancellationToken);
                                        progress.DeletedItems++;
                                    }
                                    else
                                    {
                                        progress.SkippedItems++;
                                    }
                                }
                                break;

                            case SyncAction.Skip:
                                progress.SkippedItems++;
                                break;

                            case SyncAction.Conflict:
                                progress.Conflicts++;
                                // Apply conflict resolution
                                var resolution = ResolveConflict(action.Conflict!, options);
                                await ApplyConflictResolutionAsync(resolution, guid, localItem, remoteItem, filterOptions, progress, options.CancellationToken);
                                break;
                        }

                        totalProcessed++;
                        progress.ProcessedItems++;

                        // Update knowledge
                        knowledgeUpdates.Add(CreateKnowledgeItem(guid, localItem, remoteItem, hasKnowledge, options, tenantGuid));
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
                tenantGuid,
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
    /// Get all items from a store with optional filtering, then drop anything that does not belong to the
    /// run's tenant.
    /// </summary>
    /// <remarks>
    /// The post-fetch pass is <b>not</b> redundant with the tenant term
    /// <see cref="ApplyTenantFiltering"/> puts on the predicate. A fetch predicate is only as strong as the
    /// backend's translation of it, and this family has shipped filters that a backend silently widened to
    /// match-all (a NEST request with a null <c>Query</c>, an empty <c>IN</c> rendered as always-true). Here
    /// that class of bug would put another tenant's rows into <c>localDict</c>/<c>remoteDict</c> and back onto
    /// the delete path, so the guarantee is enforced in-process where nothing can degrade it.
    /// </remarks>
    private async Task<List<T>> GetAllItemsAsync(
        TStore store,
        Expression<Func<T, bool>>? predicate,
        Guid? tenantGuid,
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

        if (tenantGuid.HasValue && _tenantGuidProperty != null)
        {
            var scopedTenantGuid = tenantGuid.Value;
            return items.Where(item => BelongsToTenant(item, scopedTenantGuid)).ToList();
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
    internal async Task ApplyConflictResolutionAsync(
        ConflictResolution resolution,
        Guid guid,
        T? localItem,
        T? remoteItem,
        SyncFilterOptions<T> filterOptions,
        SyncProgress progress,
        CancellationToken cancellationToken = default)
    {
        // No catch here (CR-H106): a failed conflict-resolution write must propagate to the caller's
        // per-item try/catch (which records a SyncError and increments progress.Errors), exactly like
        // the sibling Create/Update/Delete branches. The previous swallowing catch — with the false
        // "Error already handled in calling method" comment — silently lost the write and left
        // result.Success = true.
        switch (resolution)
        {
            case ConflictResolution.UseLocal when localItem != null:
                if (filterOptions.CanSaveToRemote?.Invoke(localItem) != false)
                {
                    // CR-L222: forward the token so cancellation is observed during conflict resolution.
                    await _remoteStore.UpdateAsync(localItem, ct: cancellationToken);
                    progress.UpdatedItems++;
                }
                break;

            case ConflictResolution.UseRemote when remoteItem != null:
                if (filterOptions.CanSaveToLocal?.Invoke(remoteItem) != false)
                {
                    await _localStore.UpdateAsync(remoteItem, ct: cancellationToken);
                    progress.UpdatedItems++;
                }
                break;

            case ConflictResolution.Skip:
                progress.SkippedItems++;
                break;
        }
    }

    /// <summary>
    /// Create sync knowledge item
    /// </summary>
    /// <remarks>
    /// SH-H050: the tenant is <b>passed in</b> — the one value <see cref="ResolveTenantScope"/> returned for
    /// this run — rather than re-derived here from <c>options.TenantGuid</c> with an ambient-tenant fallback.
    /// Re-deriving it was how knowledge came to be keyed to a different tenant than the writes were filtered
    /// to; a knowledge row keyed to the wrong tenant makes the *next* run's change detection wrong, so the
    /// damage outlives the run that recorded it.
    /// </remarks>
    internal ISyncKnowledgeItem CreateKnowledgeItem(
        Guid guid,
        T? localItem,
        T? remoteItem,
        bool hasKnowledge,
        SyncOptions options,
        Guid? tenantGuid)
    {
        return new TenantSyncKnowledgeItem
        {
            EntityGuid = guid,
            Scope = options.Scope,
            TenantGuid = tenantGuid ?? Guid.Empty,
            LastSyncedAt = DateTime.UtcNow,
            LocalVersion = GetVersionHash(localItem),
            RemoteVersion = GetVersionHash(remoteItem),
            // Only record a side as deleted with POSITIVE evidence: the entity was previously known
            // (synced before) AND is now absent on that side. Inferring deletion from mere absence in
            // a single (possibly fetch-filtered) fetch flagged never-before-synced or predicate-
            // excluded items as deleted, which later drove spurious conflicts / erroneous deletes
            // (CR-H105). A first-seen item absent on one side is a one-sided create, not a deletion.
            IsLocalDeleted = hasKnowledge && localItem == null,
            IsRemoteDeleted = hasKnowledge && remoteItem == null
        };
    }

    /// <summary>
    /// Get UpdatedAt from entity
    /// </summary>
    // CR-L223: resolve the UpdatedAt PropertyInfo ONCE per closed generic type instead of calling
    // typeof(T).GetProperty on every GetUpdatedAt invocation (which runs twice per GetVersionHash, plus
    // in the conflict/newest paths — an uncached reflection lookup across potentially large item sets).
    // A static readonly field is the static-method equivalent of the ctor-cached _guidProperty; the type
    // guard (DateTime or DateTime?) is baked in, so a non-matching property caches as null.
    private static readonly PropertyInfo? _updatedAtProperty = ResolveUpdatedAtProperty();

    private static PropertyInfo? ResolveUpdatedAtProperty()
    {
        var prop = typeof(T).GetProperty("UpdatedAt");
        // Match both DateTime and DateTime? — the exact-DateTime guard missed nullable timestamps.
        return prop != null && (prop.PropertyType == typeof(DateTime) || Nullable.GetUnderlyingType(prop.PropertyType) == typeof(DateTime))
            ? prop
            : null;
    }

    internal static DateTime? GetUpdatedAt(T entity)
    {
        return _updatedAtProperty?.GetValue(entity) as DateTime?;
    }

    /// <summary>
    /// Get version hash for entity
    /// </summary>
    internal static string? GetVersionHash(T? entity)
    {
        if (entity == null) return null;

        var updatedAt = GetUpdatedAt(entity);
        if (updatedAt.HasValue)
        {
            return updatedAt.Value.ToString("O");
        }

        // No timestamp source: derive a DETERMINISTIC hash from the entity's serialized state instead
        // of a random GUID (CR-H104). A fresh GUID per call made the recorded version never match
        // across syncs and made Preview/Sync results non-reproducible; a content hash is stable and
        // actually reflects whether the entity changed.
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(entity);
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json)));
        }
        catch
        {
            return string.Empty;
        }
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
