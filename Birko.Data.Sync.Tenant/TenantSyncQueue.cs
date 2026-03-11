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
    protected Guid CurrentTenantId => _tenantContext?.CurrentTenantId ?? Guid.Empty;

    /// <summary>
    /// Get the effective tenant ID (from parameter or context)
    /// </summary>
    protected Guid? GetEffectiveTenantId(Guid? tenantId)
    {
        return tenantId ?? (HasTenantContext ? CurrentTenantId : null);
    }

    /// <summary>
    /// Get queue key for scope (includes tenant from context if available)
    /// </summary>
    protected override string GetQueueKey(string scope)
    {
        if (HasTenantContext)
        {
            return $"{scope}_{CurrentTenantId}";
        }
        return base.GetQueueKey(scope);
    }

    /// <summary>
    /// Enqueue and execute a sync operation with explicit tenant ID
    /// </summary>
    public async Task<T> EnqueueAsync<T>(
        string scope,
        Guid? tenantId,
        Func<Task<T>> syncOperation,
        CancellationToken cancellationToken = default)
    {
        var effectiveTenantId = GetEffectiveTenantId(tenantId);
        var key = GetQueueKey(scope, effectiveTenantId);
        return await EnqueueWithKeyAsync(key, syncOperation, cancellationToken);
    }

    /// <summary>
    /// Get the number of queued operations for a scope/tenant
    /// </summary>
    public int GetQueueLength(string scope, Guid? tenantId)
    {
        var effectiveTenantId = GetEffectiveTenantId(tenantId);
        var key = GetQueueKey(scope, effectiveTenantId);
        lock (Lock)
        {
            return Queues.TryGetValue(key, out var queue) ? queue.Count : 0;
        }
    }

    /// <summary>
    /// Enqueue and execute a sync operation using tenant from context
    /// </summary>
    public new async Task<T> EnqueueAsync<T>(
        string scope,
        Func<Task<T>> syncOperation,
        CancellationToken cancellationToken = default)
    {
        var key = GetQueueKey(scope);
        return await EnqueueWithKeyAsync(key, syncOperation, cancellationToken);
    }
}
