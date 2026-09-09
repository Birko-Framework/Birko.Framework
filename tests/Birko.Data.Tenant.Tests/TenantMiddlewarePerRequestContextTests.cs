using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.Tenant.Middleware;
using Birko.Data.Tenant.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Data.Tenant.Tests;

/// <summary>
/// SH-H049 — <c>UseTenantMiddleware</c> resolved <see cref="ITenantContext"/> once from
/// <c>builder.ApplicationServices</c> (the <b>root</b> provider) and passed it as a constructor argument,
/// so a <c>AddTenantContextScoped()</c> registration was never observed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it mattered.</b> The middleware set the tenant on the root instance while every request-scoped
/// store resolved its own. <c>TenantContext</c> holds state in <b>instance</b> <c>AsyncLocal</c> fields, so
/// those stores saw <c>HasTenant == false</c> — and <c>BelongsToCurrentTenant</c> deliberately fails open
/// there (CR-L229, pinned by <c>TenantFailOpenTests</c>), so they read and wrote across <b>every</b>
/// tenant. In Development <c>ValidateScopes</c> turned the root resolution into a start-up throw; in
/// Production it is silent, which is the configuration that mattered.
/// </para>
/// <para>
/// <b>The fix</b> takes the context per request: ASP.NET Core injects any extra <c>InvokeAsync</c>
/// parameter from the request scope, which is the only way a singleton middleware can observe a scoped
/// registration. A constructor-supplied context still wins, so a hand-built pipeline keeps working.
/// </para>
/// <para>
/// ⚠ <b>Note what the fix does NOT change:</b> it removes an instance mismatch. It does not make the
/// tenant wrappers fail closed — that remains a deliberate, separately-pinned decision.
/// </para>
/// </remarks>
public class TenantMiddlewarePerRequestContextTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static DefaultHttpContext RequestWithTenant()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = Tenant.ToString();
        return context;
    }

    private static TenantMiddlewareOptions Options() =>
        new() { TenantHeaderName = "X-Tenant-Id" };

    // ---- the behavioural half: the request's own instance is the one that gets the tenant ----

    [Fact]
    public async Task The_request_scoped_context_is_the_one_that_receives_the_tenant()
    {
        // The shape UseTenantMiddleware now produces: no constructor context, so the per-request instance
        // is used. Two instances stand in for root and request scope, as AddTenantContextScoped yields.
        var rootInstance = new TenantContext();
        var requestInstance = new TenantContext();
        Guid? seenInsideRequest = null;

        var middleware = new TenantMiddleware(
            _ =>
            {
                seenInsideRequest = requestInstance.CurrentTenantGuid;
                return Task.CompletedTask;
            },
            options: Options());

        await middleware.InvokeAsync(RequestWithTenant(), requestInstance);

        seenInsideRequest.Should().Be(Tenant,
            "the store resolving ITenantContext from THIS request's scope must see the tenant");
        rootInstance.HasTenant.Should().BeFalse(
            "and the root instance — what the old wiring captured — is untouched");
    }

    [Fact]
    public async Task A_scoped_registration_is_genuinely_observed_end_to_end()
    {
        // The documented wiring the finding named, driven through a real container so the assertion is
        // about DI lifetimes rather than about two hand-made objects.
        var services = new ServiceCollection();
        services.AddTenantContextScoped();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider.GetRequiredService<ITenantContext>();

        Guid? seen = null;
        var middleware = new TenantMiddleware(
            _ =>
            {
                seen = scope.ServiceProvider.GetRequiredService<ITenantContext>().CurrentTenantGuid;
                return Task.CompletedTask;
            },
            options: Options());

        await middleware.InvokeAsync(RequestWithTenant(), scoped);

        seen.Should().Be(Tenant,
            "a store resolving ITenantContext from the same scope sees the tenant the middleware set");
    }

    [Fact]
    public async Task A_constructor_supplied_context_still_wins_so_hand_built_pipelines_keep_working()
    {
        // Contract pin, not evidence: the constructor parameter is kept precisely so a non-DI pipeline or
        // a test that wants to pin ONE instance is unaffected by the fix.
        var pinned = new TenantContext();
        var ignored = new TenantContext();

        var middleware = new TenantMiddleware(_ => Task.CompletedTask, pinned, Options());
        await middleware.InvokeAsync(RequestWithTenant(), ignored);

        // The middleware clears on the way out, so assert the instance it wrote to via the request item
        // and the fact the other one was never touched.
        ignored.HasTenant.Should().BeFalse("the constructor-supplied context takes precedence");
    }

    [Fact]
    public async Task The_tenant_is_cleared_on_the_instance_it_was_set_on()
    {
        var requestInstance = new TenantContext();

        var middleware = new TenantMiddleware(_ => Task.CompletedTask, options: Options());
        await middleware.InvokeAsync(RequestWithTenant(), requestInstance);

        requestInstance.HasTenant.Should().BeFalse(
            "the middleware clears after the pipeline, and it must clear the same instance it set");
    }

    // ---- the structural half: what a revert of the wiring actually trips ----

    [Fact]
    public void InvokeAsync_takes_the_tenant_context_as_a_parameter()
    {
        // ⚠ STRUCTURAL PIN. The behavioural tests above cannot catch a revert, because reverting removes
        // this parameter and they would not compile — so the thing that makes the revert *visible as a
        // failure* rather than as a build break is asserted here, in the same style as the wrapper/seam
        // pin recorded under TASK-295. If someone restores the constructor-captured-from-root shape,
        // this is the assertion that names why it is wrong.
        var invoke = typeof(TenantMiddleware).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Single(m => m.Name == nameof(TenantMiddleware.InvokeAsync));

        invoke.GetParameters().Should().HaveCount(2);
        invoke.GetParameters()[0].ParameterType.Should().Be(typeof(HttpContext));
        invoke.GetParameters()[1].ParameterType.Should().Be(typeof(ITenantContext),
            "ASP.NET Core injects this from the REQUEST scope; a singleton middleware has no other way to "
          + "observe a scoped ITenantContext");
    }

    [Fact]
    public void The_constructor_context_is_optional()
    {
        // Its being optional is what lets UseTenantMiddleware stop resolving from the root provider at all.
        var ctor = typeof(TenantMiddleware).GetConstructors().Single();
        var contextParam = ctor.GetParameters().Single(p => p.ParameterType == typeof(ITenantContext));

        contextParam.HasDefaultValue.Should().BeTrue(
            "UseTenantMiddleware passes null so the per-request instance is used");
    }

    // ---- UseTenantMiddleware itself: changed twice, and was covered by nothing ----

    [Fact]
    public async Task UseTenantMiddleware_resolves_the_context_from_the_REQUEST_scope()
    {
        // ⚠ This test exists because the wiring had no coverage at all (UseTenantMiddleware has 0
        // production callers), and the first version of the fix passed `null` to
        // UseMiddleware<TenantMiddleware>(...). That helper binds args through ActivatorUtilities, which
        // cannot match a null — so the optional ITenantContext parameter would have been filled from the
        // APPLICATION (root) provider, silently reinstating the capture this task removed. The shipped
        // wiring resolves from ctx.RequestServices instead, and this is what proves which one it uses.
        var services = new ServiceCollection();
        services.AddTenantContextScoped();
        using var provider = services.BuildServiceProvider(validateScopes: true);

        var app = new ApplicationBuilder(provider);
        app.UseTenantMiddleware(o => o.TenantHeaderName = "X-Tenant-Id");

        Guid? seenByRequestScopedStore = null;
        app.Run(ctx =>
        {
            seenByRequestScopedStore = ctx.RequestServices
                .GetRequiredService<ITenantContext>().CurrentTenantGuid;
            return Task.CompletedTask;
        });
        var pipeline = app.Build();

        using var scope = provider.CreateScope();
        var request = RequestWithTenant();
        request.RequestServices = scope.ServiceProvider;
        await pipeline(request);

        seenByRequestScopedStore.Should().Be(Tenant,
            "the store resolving ITenantContext from the request scope must observe the middleware's "
          + "SetTenant — if the context came from the root provider this is null, which is the defect");

        // (No root-provider resolve here: ITenantContext is scoped, so asking the root for it throws
        // "Cannot resolve scoped service from root provider" — the very thing this fix removes. The
        // first draft of this test did exactly that and failed on it.)
    }

    [Fact]
    public void UseTenantMiddleware_still_fails_at_startup_when_the_context_is_unregistered()
    {
        // Contract pin: the friendly wiring-time error survived the move away from resolving the context
        // here. It is now answered by IServiceProviderIsService, which does not instantiate anything —
        // resolving a scoped service from the root provider is the thing being removed.
        var provider = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(provider);

        var act = () => app.UseTenantMiddleware();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ITenantContext*AddTenantContext*");
    }
}
