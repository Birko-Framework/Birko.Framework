using System;
using System.Threading.Tasks;
using Birko.Data.Tenant.Middleware;
using Birko.Data.Tenant.Models;
using Birko.EventBus;
using Birko.EventBus.Enrichment;
using Birko.EventBus.Tenant;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.EventBus.Tenant.Tests;

/// <summary>
/// TASK-328. The bridge used to accept any wiring, and one whose <see cref="ITenantContext"/> was a different object
/// from the application's dispatched every tenant-scoped event across ALL tenants (SH-H053, pinned in
/// <see cref="MismatchedTenantContextWidensToAllTenantsTests"/>). It now refuses that wiring when the container first
/// builds either half, and records each all-tenants dispatch by event source.
/// </summary>
public class TenantContextWiringCheckTests
{
    private sealed class Probe : IEvent
    {
        public Guid EventGuid { get; } = Guid.NewGuid();
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
        public string Source { get; init; } = "probe";
    }

    private static ServiceProvider Build(Action<IServiceCollection> wire)
    {
        var services = new ServiceCollection();
        wire(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static TheoryData<string> SeparateInstanceWirings => new()
    {
        "AddTenantContextScoped", "AddTenantContextTransient", "AddTenantContextSingleton", "AddTenantContext",
    };

    private static void WireSeparateInstance(IServiceCollection services, string wiring)
    {
        switch (wiring)
        {
            case "AddTenantContextScoped": services.AddTenantContextScoped(); break;
            case "AddTenantContextTransient": services.AddTenantContextTransient(); break;
            case "AddTenantContextSingleton": services.AddTenantContextSingleton(); break;
            default: services.AddTenantContext(); break;
        }
    }

    [Theory]
    [MemberData(nameof(SeparateInstanceWirings))]
    public void A_context_that_is_a_separate_instance_is_refused_when_the_bridge_is_built(string wiring)
    {
        // The consumer's order: the bridge first, the application's context afterwards.
        using var root = Build(s => { s.AddEventTenantScope(); WireSeparateInstance(s, wiring); });

        var accessor = () => root.GetRequiredService<IEventScopeAccessor>();
        var enricher = () => root.GetServices<IEventEnricher>();

        accessor.Should().Throw<InvalidOperationException>().WithMessage("*ALL tenants*Tenant.Current*");
        enricher.Should().Throw<InvalidOperationException>().WithMessage("*ALL tenants*");
    }

    [Fact]
    public void The_order_of_registration_does_not_matter()
    {
        using var root = Build(s => { s.AddTenantContextScoped(); s.AddEventTenantScope(); });

        var accessor = () => root.GetRequiredService<IEventScopeAccessor>();
        accessor.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_AddBirkoSecurity_wiring_is_scoped_but_the_same_object_and_is_accepted()
    {
        // AddBirkoSecurity's registration, verbatim: Scoped in name, Tenant.Current in fact. Symbio's shape.
        using var root = Build(s =>
        {
            s.AddEventTenantScope();
            s.AddScoped<ITenantContext>(_ => Birko.Data.Tenant.Models.Tenant.Current);
        });

        root.GetRequiredService<IEventScopeAccessor>().Should().BeOfType<TenantEventScopeAccessor>();
        root.GetServices<IEventEnricher>().Should().ContainSingle(e => e is TenantEventEnricher);
    }

    [Fact]
    public void An_application_that_registers_no_context_is_accepted()
    {
        using var root = Build(s => s.AddEventTenantScope());

        root.GetRequiredService<IEventScopeAccessor>().Should().BeOfType<TenantEventScopeAccessor>();
    }

    [Fact]
    public void An_explicit_process_wide_context_registered_as_that_same_instance_is_accepted()
    {
        var context = new TenantContext();
        using var root = Build(s => { s.AddSingleton<ITenantContext>(context); s.AddEventTenantScope(context); });

        root.GetRequiredService<IEventScopeAccessor>().Should().BeOfType<TenantEventScopeAccessor>();
    }

    [Fact]
    public void An_explicit_context_the_application_does_not_resolve_is_refused()
    {
        using var root = Build(s => { s.AddSingleton<ITenantContext>(new TenantContext()); s.AddEventTenantScope(new TenantContext()); });

        var accessor = () => root.GetRequiredService<IEventScopeAccessor>();
        accessor.Should().Throw<InvalidOperationException>();
    }

    // ---- the dispatch diagnostic ----

    [Fact]
    public async Task An_all_tenants_dispatch_is_recorded_by_event_source_and_a_tenant_dispatch_is_not()
    {
        var accessor = new TenantEventScopeAccessor(new TenantContext());

        await accessor.RunWithScopeAsync(EventContext.From(new Probe { Source = "system.cleanup" }), () => Task.CompletedTask);
        await accessor.RunWithScopeAsync(EventContext.From(new Probe { Source = "system.cleanup" }), () => Task.CompletedTask);
        await accessor.RunWithScopeAsync(EventContext.From(new Probe { Source = "orders" }, Guid.NewGuid()), () => Task.CompletedTask);

        accessor.AllTenantsDispatches.Keys.Should().Equal("system.cleanup");
        accessor.AllTenantsDispatches["system.cleanup"].Count.Should().Be(2);
    }

    [Fact]
    public async Task A_throwing_subscriber_never_fails_the_dispatch_it_observes()
    {
        var accessor = new TenantEventScopeAccessor(new TenantContext());
        AllTenantsDispatch? seen = null;
        accessor.OnAllTenantsDispatch += _ => throw new InvalidOperationException("subscriber bug");
        accessor.OnAllTenantsDispatch += d => seen = d;
        var ran = false;

        await accessor.RunWithScopeAsync(EventContext.From(new Probe()), () => { ran = true; return Task.CompletedTask; });

        ran.Should().BeTrue();
        seen!.Source.Should().Be("probe");
        accessor.SubscriberFailures.Should().Be(1);
    }
}
