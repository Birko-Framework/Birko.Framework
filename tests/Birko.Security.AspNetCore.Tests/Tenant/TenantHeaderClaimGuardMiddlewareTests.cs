using System.Text;
using Birko.Data.Tenant.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;
using DataTenantContext = Birko.Data.Tenant.Models.TenantContext;
using DataTenantMiddleware = Birko.Data.Tenant.Middleware.TenantMiddleware;
using SecurityTenantMiddleware = Birko.Security.AspNetCore.TenantMiddleware;

namespace Birko.Security.AspNetCore.Tests.Tenant;

/// <summary>
/// SH-H048 / TASK-118 — the tenant/claim guard must correlate <b>whatever tenant the request addressed</b>
/// with the caller's <c>tenant_id</c> claim, not one hard-coded <c>X-Tenant-Id</c> header.
///
/// <para>
/// The defect: a caller authenticated in tenant A could address tenant B through any door the guard did not
/// know about — a query-string key, a route value, a subdomain, either custom-resolver hook, or simply a
/// <i>renamed</i> <c>TenantMiddlewareOptions.TenantHeaderName</c>. Repository scoping followed the resolved
/// tenant while permissions stayed with the token, so the request succeeded and returned (or wrote) the
/// victim's data. Nothing failed, nothing logged.
/// </para>
/// <para>
/// These are end-to-end through the real resolving middleware rather than unit tests of the guard alone,
/// because the bug was precisely that the guard and the resolver disagreed about what "the tenant" is.
/// Asserting the guard in isolation would restate the fix instead of testing it.
/// </para>
/// </summary>
public class TenantHeaderClaimGuardMiddlewareTests
{
    private static readonly Guid HomeTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid VictimTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ---------------------------------------------------------------------------------------------------
    // Fix-dependent: every alternative door, each of which the header-only guard waved through.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task QueryStringSource_AddressingAnotherTenant_IsRefused()
    {
        var result = await RunStackBAsync(
            new TenantMiddlewareOptions { TenantQueryStringKey = "tenant" },
            HomeUser(),
            ctx => ctx.Request.QueryString = new QueryString($"?tenant={VictimTenant}"));

        result.ShouldBeRefused();
    }

    [Fact]
    public async Task RouteValueSource_AddressingAnotherTenant_IsRefused()
    {
        // The task as filed asserted no route tenant source exists. It does —
        // Birko.Data.Tenant/Middleware/TenantMiddleware.cs reads TenantRouteKey via GetRouteValue.
        var result = await RunStackBAsync(
            new TenantMiddlewareOptions { TenantRouteKey = "tenantId" },
            HomeUser(),
            ctx => ctx.Request.RouteValues["tenantId"] = VictimTenant.ToString());

        result.ShouldBeRefused();
    }

    [Fact]
    public async Task RenamedTenantHeader_AddressingAnotherTenant_IsRefused()
    {
        // The quiet one: the guard's hard-coded constant stopped matching the configured header name, so it
        // silently stopped guarding anything on a deployment that looked correctly configured.
        var result = await RunStackBAsync(
            new TenantMiddlewareOptions { TenantHeaderName = "X-Org-Id" },
            HomeUser(),
            ctx => ctx.Request.Headers["X-Org-Id"] = VictimTenant.ToString());

        result.ShouldBeRefused();
    }

    [Fact]
    public async Task CustomTenantResolver_AddressingAnotherTenant_IsRefused()
    {
        var result = await RunStackBAsync(
            new TenantMiddlewareOptions { CustomTenantResolver = _ => VictimTenant },
            HomeUser(),
            _ => { });

        result.ShouldBeRefused();
    }

    [Fact]
    public async Task SubdomainResolver_AddressingAnotherTenant_IsRefused()
    {
        // Stack A: Birko.Security.AspNetCore has its own resolver chain, which the finding never mentioned.
        var result = await RunStackAAsync(
            new StubTenantResolver(new TenantInfo(VictimTenant, "victim")),
            HomeUser());

        result.ShouldBeRefused();
    }

    [Fact]
    public async Task CustomITenantResolver_AddressingAnotherTenant_IsRefused()
    {
        var result = await RunStackAAsync(
            new StubTenantResolver(new TenantInfo(VictimTenant, "victim")),
            HomeUser());

        result.ShouldBeRefused();
    }

    [Fact]
    public async Task RefusalBodyNamesTheSourceThatWasUsed()
    {
        var result = await RunStackBAsync(
            new TenantMiddlewareOptions { TenantQueryStringKey = "tenant" },
            HomeUser(),
            ctx => ctx.Request.QueryString = new QueryString($"?tenant={VictimTenant}"));

        // The code stays stable for consumers; only the human half names the door.
        result.Body.Should().Contain("Tenant.HeaderClaimMismatch");
        result.Body.Should().Contain("query string parameter");
    }

    [Fact]
    public async Task ConfiguredKeyNamesCannotBreakOutOfTheJsonBody()
    {
        // The source description embeds a consumer-configured key, and the 403 body is hand-written JSON.
        var result = await RunStackBAsync(
            new TenantMiddlewareOptions { TenantQueryStringKey = "ten\"ant" },
            HomeUser(),
            ctx => ctx.Request.QueryString = new QueryString($"?ten%22ant={VictimTenant}"));

        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        // Exactly the two quoted field names plus their two quoted values — no injected extras.
        result.Body.Count(c => c == '"').Should().Be(8);
    }

    [Fact]
    public async Task SystemScopeToken_CannotAddressARealTenant()
    {
        // Guid.Empty is the system/no-tenant scope, not a wildcard. Filed as a contract pin and moved here
        // when the step-6 revert failed it: the pre-fix guard only ever compared the literal header, and this
        // reaches the victim through the query string, so it was never pinning old behaviour.
        var result = await RunStackBAsync(
            new TenantMiddlewareOptions { TenantQueryStringKey = "tenant" },
            new TestCurrentUser(isAuthenticated: true, tenantGuid: null),
            ctx => ctx.Request.QueryString = new QueryString($"?tenant={VictimTenant}"));

        result.ShouldBeRefused();
    }

    // ---------------------------------------------------------------------------------------------------
    // Contract pins — these held before this task too. They are here to pin behaviour the fix must not
    // change, NOT as evidence that the fix works. All of them stay green when the fix is reverted.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task LiteralTenantHeader_AddressingAnotherTenant_IsStillRefused()
    {
        // Retained deliberately: an app that never wired tenant resolution must be no less protected than it
        // was before this task, because its own code may read the header directly.
        var result = await RunGuardOnlyAsync(
            HomeUser(),
            ctx => ctx.Request.Headers["X-Tenant-Id"] = VictimTenant.ToString());

        result.ShouldBeRefused();
    }

    [Fact]
    public async Task MatchingTenant_IsAllowed()
    {
        var result = await RunStackBAsync(
            new TenantMiddlewareOptions { TenantQueryStringKey = "tenant" },
            HomeUser(),
            ctx => ctx.Request.QueryString = new QueryString($"?tenant={HomeTenant}"));

        result.ShouldBeAllowed();
    }

    [Fact]
    public async Task NoTenantAddressed_IsAllowed()
    {
        // SSE in particular cannot set headers; the claim is then the only tenant source.
        var result = await RunStackBAsync(new TenantMiddlewareOptions(), HomeUser(), _ => { });

        result.ShouldBeAllowed();
    }

    [Fact]
    public async Task UnparseableSource_IsAllowed()
    {
        var result = await RunStackBAsync(
            new TenantMiddlewareOptions { TenantQueryStringKey = "tenant" },
            HomeUser(),
            ctx => ctx.Request.QueryString = new QueryString("?tenant=not-a-guid"));

        result.ShouldBeAllowed();
    }

    [Fact]
    public async Task UnauthenticatedRequest_IsAllowed()
    {
        var result = await RunStackBAsync(
            new TenantMiddlewareOptions { TenantQueryStringKey = "tenant" },
            new TestCurrentUser(isAuthenticated: false, tenantGuid: null),
            ctx => ctx.Request.QueryString = new QueryString($"?tenant={VictimTenant}"));

        result.ShouldBeAllowed();
    }

    [Fact]
    public async Task WildcardHolder_IsAllowed()
    {
        var result = await RunStackBAsync(
            new TenantMiddlewareOptions { TenantQueryStringKey = "tenant" },
            new TestCurrentUser(isAuthenticated: true, tenantGuid: HomeTenant, permissions: new() { "*" }),
            ctx => ctx.Request.QueryString = new QueryString($"?tenant={VictimTenant}"));

        result.ShouldBeAllowed();
    }

    [Fact]
    public async Task GuardDisabled_IsAllowed()
    {
        var result = await RunStackBAsync(
            new TenantMiddlewareOptions { TenantQueryStringKey = "tenant" },
            HomeUser(),
            ctx => ctx.Request.QueryString = new QueryString($"?tenant={VictimTenant}"),
            new BirkoSecurityOptions { RequireTenantHeaderMatchesClaim = false });

        result.ShouldBeAllowed();
    }

    // ---------------------------------------------------------------------------------------------------
    // The carrier itself. Both are fix-dependent by construction — they name APIs the fix introduces, so
    // they do not compile against pre-fix code at all.
    // ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ResolutionIsPublishedPerRequest_NotThroughATenantContextRegistration()
    {
        // The fail-open this design avoids: UseTenantMiddleware captures its ITenantContext from
        // ApplicationServices while the guard resolves one from RequestServices, so under
        // AddTenantContextScoped() the two are different objects. Here the guard is handed a context that
        // was NEVER set, standing in for that mismatch — it must still refuse.
        var neverSet = new TenantContextAdapter(new DataTenantContext());
        var nextCalled = false;

        var guard = new TenantHeaderClaimGuardMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            new BirkoSecurityOptions());

        var context = NewContext();
        ResolvedTenant.Publish(context, VictimTenant, "the tenant resolved for this request");

        await guard.InvokeAsync(context, HomeUser(), neverSet);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void ResolvedTenant_RoundTripsThroughHttpContextItems()
    {
        var context = new DefaultHttpContext();

        ResolvedTenant.From(context).Should().BeNull();

        ResolvedTenant.Publish(context, VictimTenant, "the tenant resolved for this request");

        ResolvedTenant.From(context)!.TenantGuid.Should().Be(VictimTenant);
    }

    // ---------------------------------------------------------------------------------------------------
    // Harness
    // ---------------------------------------------------------------------------------------------------

    private static TestCurrentUser HomeUser()
        => new(isAuthenticated: true, tenantGuid: HomeTenant);

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<GuardResult> ReadAsync(DefaultHttpContext context, bool nextCalled)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        return new GuardResult(nextCalled, context.Response.StatusCode, body);
    }

    /// <summary>Guard alone — no resolving middleware in the pipeline at all.</summary>
    private static async Task<GuardResult> RunGuardOnlyAsync(
        ICurrentUser user,
        Action<DefaultHttpContext> configureRequest)
    {
        var nextCalled = false;
        var guard = new TenantHeaderClaimGuardMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            new BirkoSecurityOptions());

        var context = NewContext();
        configureRequest(context);

        await guard.InvokeAsync(context, user, new TenantContextAdapter(new DataTenantContext()));

        return await ReadAsync(context, nextCalled);
    }

    /// <summary>Birko.Data.Tenant's resolving middleware, then the guard — the real pipeline order.</summary>
    private static async Task<GuardResult> RunStackBAsync(
        TenantMiddlewareOptions tenantOptions,
        ICurrentUser user,
        Action<DefaultHttpContext> configureRequest,
        BirkoSecurityOptions? securityOptions = null)
    {
        var birkoContext = new DataTenantContext();
        var adapter = new TenantContextAdapter(birkoContext);
        var nextCalled = false;

        var guard = new TenantHeaderClaimGuardMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            securityOptions ?? new BirkoSecurityOptions());

        var resolving = new DataTenantMiddleware(
            ctx => guard.InvokeAsync(ctx, user, adapter),
            birkoContext,
            tenantOptions);

        var context = NewContext();
        configureRequest(context);

        // SH-H049: InvokeAsync now takes the tenant context per request. This test deliberately pins ONE
        // instance (birkoContext) so the guard and the resolving middleware are correlated, so it is
        // supplied to both the constructor and the invocation — the constructor-supplied one wins either
        // way, which is what keeps a hand-built pipeline like this working.
        await resolving.InvokeAsync(context, birkoContext);

        return await ReadAsync(context, nextCalled);
    }

    /// <summary>Birko.Security.AspNetCore's own ITenantResolver chain, then the guard.</summary>
    private static async Task<GuardResult> RunStackAAsync(ITenantResolver resolver, ICurrentUser user)
    {
        var birkoContext = new DataTenantContext();
        var adapter = new TenantContextAdapter(birkoContext);
        var nextCalled = false;

        var guard = new TenantHeaderClaimGuardMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            new BirkoSecurityOptions());

        var resolving = new SecurityTenantMiddleware(ctx => guard.InvokeAsync(ctx, user, adapter));

        var context = NewContext();

        await resolving.InvokeAsync(context, resolver, adapter);

        return await ReadAsync(context, nextCalled);
    }

    private sealed record GuardResult(bool NextCalled, int StatusCode, string Body)
    {
        /// <summary>
        /// Bidirectional on purpose: a refusal that forgot to short-circuit still reaches the endpoint, and
        /// a 403 written after <c>next</c> ran is not a refusal. Asserting the status alone passes for both.
        /// </summary>
        public void ShouldBeRefused()
        {
            NextCalled.Should().BeFalse("the request must not reach the endpoint");
            StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        }

        public void ShouldBeAllowed()
        {
            NextCalled.Should().BeTrue("the request must reach the endpoint");
            StatusCode.Should().Be(StatusCodes.Status200OK);
        }
    }

    private sealed class StubTenantResolver : ITenantResolver
    {
        private readonly TenantInfo? _result;
        public StubTenantResolver(TenantInfo? result) => _result = result;
        public Task<TenantInfo?> ResolveAsync(HttpContext context, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public TestCurrentUser(bool isAuthenticated, Guid? tenantGuid, HashSet<string>? permissions = null)
        {
            IsAuthenticated = isAuthenticated;
            TenantGuid = tenantGuid;
            Permissions = (permissions ?? new HashSet<string>()).AsReadOnly();
        }

        public Guid? UserId => IsAuthenticated ? Guid.Parse("33333333-3333-3333-3333-333333333333") : null;
        public string? Email => null;
        public Guid? TenantGuid { get; }
        public IReadOnlySet<string> Roles => new HashSet<string>().AsReadOnly();
        public IReadOnlySet<string> Permissions { get; }
        public bool IsAuthenticated { get; }
        public string? GetClaim(string claimType) => null;
    }
}
