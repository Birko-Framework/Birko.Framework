using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.Sync.Tenant;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Tenant.Tests;

/// <summary>
/// CR-L224: TenantSyncQueue redundantly shadowed the base SyncQueue.EnqueueAsync(scope, op, ct) with a
/// `new` overload that added no behavior (the base already keys via the virtual GetQueueKey(scope) this
/// class overrides) and introduced a member-hiding footgun. It was removed; the tenant-explicit overload
/// with a null tenantGuid covers the context-based case with an identical queue key.
/// </summary>
public class TenantSyncQueueTests
{
    [Fact]
    public void DoesNotShadowBaseEnqueueAsync_WithARedundantNewOverload()
    {
        var declared = typeof(TenantSyncQueue)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name == "EnqueueAsync")
            .ToList();

        declared.Should().HaveCount(1, "only the tenant-explicit EnqueueAsync overload should be declared");
        declared[0].GetParameters().Should().HaveCount(4,
            "the surviving overload is EnqueueAsync(scope, tenantGuid, op, ct)");
    }

    [Fact]
    public async Task ContextEnqueue_ViaNullTenant_RunsTheOperation()
    {
        // No tenant context ⇒ the context-based case is expressed as tenantGuid: null on the surviving
        // overload. The queued operation still executes and returns its result.
        var queue = new TenantSyncQueue();

        var result = await queue.EnqueueAsync<int>("scope", null, () => Task.FromResult(42));

        result.Should().Be(42);
    }
}
