using Birko.Data.Tenant.Middleware;
using Birko.Data.Tenant.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Birko.Data.Tenant.Tests;

/// <summary>
/// SH-H048 / TASK-118 — `TenantMiddleware` must publish the tenant it resolved, and name the source, so a
/// post-authentication guard can correlate it with the caller's token.
///
/// <para>
/// The correlating guard lives in `Birko.Security.AspNetCore` and is tested there end-to-end. These tests
/// pin the *producer* half in the project that owns it: that every source publishes, that each names itself
/// distinguishably, and that nothing is published when nothing resolved. Without the source description the
/// guard cannot tell an operator which door was used; without the publication it sees nothing at all.
/// </para>
/// </summary>
public class ResolvedTenantPublicationTests
{
    private static readonly Guid TenantGuid = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task HeaderResolution_IsPublished()
    {
        var published = await ResolveAsync(
            new TenantMiddlewareOptions(),
            ctx => ctx.Request.Headers["X-Tenant-Id"] = TenantGuid.ToString());

        published!.TenantGuid.Should().Be(TenantGuid);
        published.Source.Should().Contain("X-Tenant-Id").And.Contain("header");
    }

    [Fact]
    public async Task RenamedHeaderResolution_IsPublishedUnderItsConfiguredName()
    {
        var published = await ResolveAsync(
            new TenantMiddlewareOptions { TenantHeaderName = "X-Org-Id" },
            ctx => ctx.Request.Headers["X-Org-Id"] = TenantGuid.ToString());

        published!.TenantGuid.Should().Be(TenantGuid);
        published.Source.Should().Contain("X-Org-Id");
    }

    [Fact]
    public async Task QueryStringResolution_IsPublished()
    {
        var published = await ResolveAsync(
            new TenantMiddlewareOptions { TenantQueryStringKey = "tenant" },
            ctx => ctx.Request.QueryString = new QueryString($"?tenant={TenantGuid}"));

        published!.TenantGuid.Should().Be(TenantGuid);
        published.Source.Should().Contain("query string");
    }

    [Fact]
    public async Task RouteValueResolution_IsPublished()
    {
        var published = await ResolveAsync(
            new TenantMiddlewareOptions { TenantRouteKey = "tenantId" },
            ctx => ctx.Request.RouteValues["tenantId"] = TenantGuid.ToString());

        published!.TenantGuid.Should().Be(TenantGuid);
        published.Source.Should().Contain("route value");
    }

    [Fact]
    public async Task CustomResolverResolution_IsPublished()
    {
        var published = await ResolveAsync(
            new TenantMiddlewareOptions { CustomTenantResolver = _ => TenantGuid },
            _ => { });

        published!.TenantGuid.Should().Be(TenantGuid);
        published.Source.Should().Contain("custom tenant resolver");
    }

    [Fact]
    public async Task NothingResolved_PublishesNothing()
    {
        var published = await ResolveAsync(new TenantMiddlewareOptions(), _ => { });

        published.Should().BeNull();
    }

    [Fact]
    public async Task UnparseableSource_PublishesNothing()
    {
        var published = await ResolveAsync(
            new TenantMiddlewareOptions { TenantQueryStringKey = "tenant" },
            ctx => ctx.Request.QueryString = new QueryString("?tenant=not-a-guid"));

        published.Should().BeNull();
    }

    [Fact]
    public async Task ConfiguredKeyNames_AreStrippedOfJsonBreakingCharacters()
    {
        // The description reaches a hand-written JSON body in the guard, and the key is consumer-configured.
        var published = await ResolveAsync(
            new TenantMiddlewareOptions { TenantQueryStringKey = "ten\"an\\t" },
            ctx => ctx.Request.QueryString = new QueryString($"?ten%22an%5Ct={TenantGuid}"));

        published!.Source.Should().NotContain("\"").And.NotContain("\\");
    }

    [Fact]
    public async Task PublicationSurvivesAlongsideTheConfigurableContextKey()
    {
        // Both are written: TenantContextKey for existing consumers, the fixed key for the guard. The guard
        // cannot use the former precisely because a consumer may rename it, as here.
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = TenantGuid.ToString();

        await RunAsync(new TenantMiddlewareOptions { TenantContextKey = "SomethingElse" }, context);

        context.Items["SomethingElse"].Should().Be(TenantGuid);
        ResolvedTenant.From(context)!.TenantGuid.Should().Be(TenantGuid);
    }

    private static async Task<ResolvedTenant?> ResolveAsync(
        TenantMiddlewareOptions options,
        Action<DefaultHttpContext> configureRequest)
    {
        var context = new DefaultHttpContext();
        configureRequest(context);

        await RunAsync(options, context);

        return ResolvedTenant.From(context);
    }

    private static async Task RunAsync(TenantMiddlewareOptions options, DefaultHttpContext context)
    {
        var middleware = new TenantMiddleware(_ => Task.CompletedTask, new TenantContext(), options);
        await middleware.InvokeAsync(context);
    }
}
