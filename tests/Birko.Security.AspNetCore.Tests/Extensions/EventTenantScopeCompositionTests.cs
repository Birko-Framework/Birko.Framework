using System;
using Birko.Data.Tenant.Middleware;
using Birko.EventBus;
using Birko.EventBus.Enrichment;
using Birko.EventBus.Tenant;
using Birko.Security.AspNetCore;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Extensions;

/// <summary>
/// TASK-328. The event ↔ tenant bridge now refuses a tenant context that is a different object from the one it reads.
/// The live consumer (Symbio) wires it as below, bridge first and <c>AddBirkoSecurity</c> after, and that wiring is
/// correct: <c>AddBirkoSecurity</c> registers the context scoped, as <c>_ =&gt; Tenant.Current</c>. This pins it
/// against the real registration, not a copy of it, so a change on either side that breaks the consumer fails here.
/// </summary>
public class EventTenantScopeCompositionTests
{
    private static ServiceProvider Build(Action<IServiceCollection> wire)
    {
        var services = new ServiceCollection();
        wire(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static void Security(IServiceCollection services)
        => services.AddBirkoSecurity(o => o.Jwt.Secret = "a-test-secret-that-is-long-enough-for-hmac-sha256!!");

    [Fact]
    public void The_consumers_wiring_builds_both_halves_of_the_bridge()
    {
        using var root = Build(s => { s.AddEventTenantScope(); Security(s); });

        root.GetRequiredService<IEventScopeAccessor>().Should().BeOfType<TenantEventScopeAccessor>();
        root.GetServices<IEventEnricher>().Should().ContainSingle(e => e is TenantEventEnricher);
    }

    [Fact]
    public void Adding_a_per_scope_context_after_AddBirkoSecurity_is_refused()
    {
        // The last registration wins resolution, so this application's repositories read a new TenantContext per
        // scope while the bridge reads Tenant.Current.
        using var root = Build(s => { s.AddEventTenantScope(); Security(s); s.AddTenantContextScoped(); });

        var accessor = () => root.GetRequiredService<IEventScopeAccessor>();
        accessor.Should().Throw<InvalidOperationException>().WithMessage("*ALL tenants*");
    }
}
