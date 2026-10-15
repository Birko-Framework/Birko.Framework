using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.Expressions;
using Birko.Data.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using Birko.Data.Sync.Tenant.Models;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;

namespace Birko.Data.Sync.Tenant.Providers;

/// <summary>
/// Tenant-aware synchronization: the shared <see cref="AsyncSyncProvider{TStore, T, TKnowledge}"/> run under one tenant.
/// </summary>
/// <remarks>
/// <para><b>TASK-630: one engine.</b> This class used to re-implement the batch loop, the decision, conflict resolution,
/// preview and knowledge bookkeeping, and the copy had drifted from the engine in eleven ways (SH-M342 to SH-M352). It now
/// contributes only what is tenant-specific, and the engine does the run:</para>
/// <list type="bullet">
/// <item><b>The tenant</b>, resolved once per run (<see cref="ResolveTenantScope"/>, SH-H050).</item>
/// <item><b>The tenant predicates</b> on the fetch and on every save, delete included (<see cref="ApplyTenantFiltering"/>).
/// The engine also enforces the fetch predicate in process, so a backend that widens it cannot leak another tenant's
/// rows.</item>
/// <item><b>Tenant-scoped knowledge</b>, by running the engine under <see cref="TenantScope"/>: the caller's scope
/// qualified by the tenant. Any knowledge store therefore keeps each tenant's knowledge apart (SH-M066), and no
/// tenant-specific knowledge store is needed (SH-M065). A run in an explicit all-tenants scope, and a type with no
/// <c>TenantGuid</c>, use the caller's scope as given; their knowledge is separate from any tenant's.</item>
/// </list>
/// </remarks>
public class TenantSyncProvider<TStore, T, TKnowledge> : ISyncProvider
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel
    where TKnowledge : Data.Models.AbstractModel, ISyncKnowledgeItem
{
    private readonly AsyncSyncProvider<TStore, T, TKnowledge> _engine;
    private readonly ITenantContext _tenantContext;
    private readonly PropertyInfo? _tenantGuidProperty;

    /// <summary>
    /// Create a new tenant-aware sync provider
    /// </summary>
    public TenantSyncProvider(
        TStore localStore,
        TStore remoteStore,
        IAsyncSyncKnowledgeItemStore<TKnowledge> knowledgeStore,
        ITenantContext? tenantContext = null)
    {
        _engine = new AsyncSyncProvider<TStore, T, TKnowledge>(localStore, remoteStore, knowledgeStore);
        _tenantContext = tenantContext ?? Data.Tenant.Models.Tenant.Current;
        _tenantGuidProperty = typeof(T).GetProperty("TenantGuid");
    }

    /// <summary>
    /// The knowledge scope a run uses: the caller's scope, qualified by the tenant when the run is tenant-scoped.
    /// </summary>
    public static string TenantScope(string scope, Guid? tenantGuid)
        => tenantGuid is Guid tenant ? $"{scope}@tenant:{tenant:N}" : scope;

    /// <summary>
    /// Preview sync with automatic tenant scoping
    /// </summary>
    public async Task<SyncPreview> PreviewAsync(
        SyncOptions? baseOptions = null,
        SyncFilterOptions<T>? filterOptions = null)
    {
        var options = baseOptions ?? new SyncOptions();
        var tenantGuid = ResolveTenantScope(options);
        var preview = await _engine.PreviewAsync(RunOptions(options, tenantGuid), ApplyTenantFiltering(filterOptions, tenantGuid));
        preview.Scope = options.Scope;
        return preview;
    }

    /// <summary>
    /// Execute sync with automatic tenant scoping
    /// </summary>
    public async Task<SyncResult> SyncAsync(
        SyncOptions? baseOptions = null,
        SyncFilterOptions<T>? filterOptions = null)
    {
        var options = baseOptions ?? new SyncOptions();
        var tenantGuid = ResolveTenantScope(options);
        var result = await _engine.SyncAsync(RunOptions(options, tenantGuid), ApplyTenantFiltering(filterOptions, tenantGuid));
        return new TenantSyncResult
        {
            Success = result.Success,
            TotalProcessed = result.TotalProcessed,
            Created = result.Created,
            Updated = result.Updated,
            Deleted = result.Deleted,
            Skipped = result.Skipped,
            Conflicts = result.Conflicts,
            Errors = result.Errors,
            Duration = result.Duration,
            StartTime = result.StartTime,
            EndTime = result.EndTime,
            Scope = options.Scope,
            Direction = result.Direction,
            IsInitialSync = result.IsInitialSync,
            TenantGuid = tenantGuid,
            TenantName = (options as TenantSyncOptions)?.TenantName
                ?? (tenantGuid.HasValue && tenantGuid == _tenantContext.CurrentTenantGuid ? _tenantContext.CurrentTenantName : null),
        };
    }

    // The engine runs on a copy: the caller's options are never written to (CR-M168), and a reused instance stays as given.
    private static SyncOptions RunOptions(SyncOptions options, Guid? tenantGuid)
    {
        var run = options.Clone();
        run.Scope = TenantScope(options.Scope, tenantGuid);
        return run;
    }

    // SH-M352 (TASK-630): a filter of another entity type was dropped by `as`, so the run went unfiltered. Refused instead.
    private static SyncFilterOptions<T>? TypedFilter(object? filterOptions) => filterOptions switch
    {
        null => null,
        SyncFilterOptions<T> typed => typed,
        _ => throw new ArgumentException(
            $"Filter options for {typeof(T).Name} must be SyncFilterOptions<{typeof(T).Name}>, not {filterOptions.GetType().Name}.",
            nameof(filterOptions)),
    };

    Task<SyncPreview> ISyncProvider.PreviewAsync(SyncOptions? options, object? filterOptions)
        => PreviewAsync(options, TypedFilter(filterOptions));

    Task<SyncResult> ISyncProvider.SyncAsync(SyncOptions? options, object? filterOptions)
        => SyncAsync(options, TypedFilter(filterOptions));

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
    /// from the save paths, so the engine, which enforces the fetch predicate in process too, keeps the
    /// run fail-closed rather than widening it.
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
    public static TenantSyncSetup<TStore, T, TKnowledge> CreateTenantSync<TStore, T, TKnowledge>(
        this (TStore Local, TStore Remote) stores,
        IAsyncSyncKnowledgeItemStore<TKnowledge> knowledgeStore,
        ITenantContext? tenantContext = null)
        where TStore : IAsyncBulkStore<T>
        where T : Data.Models.AbstractModel
        where TKnowledge : Data.Models.AbstractModel, ISyncKnowledgeItem
    {
        return new TenantSyncSetup<TStore, T, TKnowledge>
        {
            SyncProvider = new TenantSyncProvider<TStore, T, TKnowledge>(stores.Local, stores.Remote, knowledgeStore, tenantContext),
            KnowledgeStore = knowledgeStore
        };
    }

    /// <summary>
    /// Wrap stores with tenant-aware sync
    /// </summary>
    public static TenantSyncProvider<TStore, T, TKnowledge> WithTenantSync<TStore, T, TKnowledge>(
        this TStore localStore,
        TStore remoteStore,
        IAsyncSyncKnowledgeItemStore<TKnowledge> knowledgeStore,
        ITenantContext? tenantContext = null)
        where TStore : IAsyncBulkStore<T>
        where T : Data.Models.AbstractModel
        where TKnowledge : Data.Models.AbstractModel, ISyncKnowledgeItem
    {
        return new TenantSyncProvider<TStore, T, TKnowledge>(localStore, remoteStore, knowledgeStore, tenantContext);
    }
}

/// <summary>
/// Complete tenant sync setup
/// </summary>
public class TenantSyncSetup<TStore, T, TKnowledge>
    where TStore : IAsyncBulkStore<T>
    where T : Data.Models.AbstractModel
    where TKnowledge : Data.Models.AbstractModel, ISyncKnowledgeItem
{
    public required TenantSyncProvider<TStore, T, TKnowledge> SyncProvider { get; init; }
    public required IAsyncSyncKnowledgeItemStore<TKnowledge> KnowledgeStore { get; init; }
}
