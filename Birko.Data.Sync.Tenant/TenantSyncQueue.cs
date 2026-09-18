using Birko.Data.Tenant.Models;

namespace Birko.Data.Sync.Tenant;

/// <summary>
/// Tenant-aware queue for managing concurrent synchronization operations
/// Automatically uses current tenant from ITenantContext for queue scoping
/// </summary>
public class TenantSyncQueue : SyncQueue
{
    private readonly ITenantContext? _tenantContext;

    /// <summary>
    /// Create a new tenant-aware sync queue
    /// </summary>
    public TenantSyncQueue(ITenantContext? tenantContext = null, int maxConcurrentSyncs = 1)
        : base(maxConcurrentSyncs)
    {
        _tenantContext = tenantContext ?? Data.Tenant.Models.Tenant.Current;
    }

    /// <summary>
    /// Whether this queue has a tenant context
    /// </summary>
    protected bool HasTenantContext => _tenantContext?.HasTenant == true;

    /// <summary>
    /// Get the current tenant ID from context
    /// </summary>
    protected Guid CurrentTenantGuid => _tenantContext?.CurrentTenantGuid ?? Guid.Empty;

    /// <summary>
    /// Get the effective tenant ID (from parameter or context)
    /// </summary>
    protected Guid? GetEffectiveTenantGuid(Guid? tenantGuid)
    {
        return tenantGuid ?? (HasTenantContext ? CurrentTenantGuid : null);
    }

    /// <summary>
    /// Get queue key for scope (includes tenant from context if available)
    /// </summary>
    protected override string GetQueueKey(string scope)
    {
        if (HasTenantContext)
        {
            return $"{scope}_{CurrentTenantGuid}";
        }
        return base.GetQueueKey(scope);
    }

    /// <summary>
    /// Enqueue and execute a sync operation with explicit tenant ID
    /// </summary>
    public async Task<T> EnqueueAsync<T>(
        string scope,
        Guid? tenantGuid,
        Func<Task<T>> syncOperation,
        CancellationToken cancellationToken = default)
    {
        var effectiveTenantGuid = GetEffectiveTenantGuid(tenantGuid);
        var key = GetQueueKey(scope, effectiveTenantGuid);
        return await EnqueueWithKeyAsync(key, syncOperation, cancellationToken);
    }

    /// <summary>
    /// Get the number of queued operations for a scope/tenant
    /// </summary>
    public int GetQueueLength(string scope, Guid? tenantGuid)
    {
        var effectiveTenantGuid = GetEffectiveTenantGuid(tenantGuid);
        var key = GetQueueKey(scope, effectiveTenantGuid);
        lock (Lock)
        {
            return Queues.TryGetValue(key, out var queue) ? queue.Count : 0;
        }
    }

    // CR-L224: removed the redundant `new EnqueueAsync(scope, op, ct)` shadow. It only re-declared the
    // inherited SyncQueue.EnqueueAsync — which already computes its key via the virtual GetQueueKey(scope)
    // this class overrides, so tenant scoping applies to the base method automatically. The `new` shadow
    // added no behavior and introduced a member-hiding footgun. Context-based enqueue on a
    // TenantSyncQueue-typed reference is available via the tenant-explicit overload above with a null
    // tenantGuid — GetEffectiveTenantGuid(null) resolves the context tenant and GetQueueKey(scope, tenant)
    // yields the identical "{scope}_{tenant}" key; base-typed references call the inherited method directly.
}
