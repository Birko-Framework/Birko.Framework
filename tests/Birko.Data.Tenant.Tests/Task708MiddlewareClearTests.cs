using System;
using System.Threading.Tasks;
using Birko.Data.Tenant.Middleware;
using Birko.Data.Tenant.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Birko.Data.Tenant.Tests;

/// <summary>
/// TASK-708. <c>TenantMiddleware</c> called <c>ClearTenant()</c> after <c>await _next(context)</c> with no
/// <c>finally</c>, and called it whether or not it had set a tenant. Measured on a field-backed
/// <see cref="ITenantContext"/>: after a downstream throw the outer catch and a later flow still saw the tenant, and a
/// request with no tenant discarded one an outer flow had set. <see cref="TenantContext"/> showed neither, because its
/// <c>AsyncLocal</c> state is undone when the async call returns; the interface does not require that, so both
/// contexts are pinned here.
/// </summary>
public class Task708MiddlewareClearTests
{
    private static readonly Guid T = Guid.Parse("70870870-8708-7087-0870-870870870870");

    public static TheoryData<string> Contexts => new() { "asynclocal", "field" };

    private static ITenantContext Create(string kind) => kind == "field" ? new FieldContext() : new TenantContext();

    private static DefaultHttpContext WithTenant()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Tenant-Id"] = T.ToString();
        return http;
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task A_downstream_throw_does_not_leave_the_tenant_set(string kind)
    {
        var ctx = Create(kind);
        var sawTenant = false;
        var mw = new TenantMiddleware(_ => { sawTenant = ctx.HasTenant; throw new InvalidOperationException("boom"); }, ctx);

        var act = () => mw.InvokeAsync(WithTenant(), ctx);

        await act.Should().ThrowAsync<InvalidOperationException>();
        sawTenant.Should().BeTrue("the pipeline runs under the tenant");
        ctx.HasTenant.Should().BeFalse();
        (await Task.Run(() => ctx.HasTenant)).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task A_faulted_downstream_task_does_not_leave_the_tenant_set(string kind)
    {
        var ctx = Create(kind);
        var mw = new TenantMiddleware(async _ => { await Task.Yield(); throw new InvalidOperationException("boom"); }, ctx);

        var act = () => mw.InvokeAsync(WithTenant(), ctx);

        await act.Should().ThrowAsync<InvalidOperationException>();
        ctx.HasTenant.Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task A_request_with_no_tenant_keeps_the_outer_tenant(string kind)
    {
        var ctx = Create(kind);
        ctx.SetTenant(T);
        var mw = new TenantMiddleware(_ => Task.CompletedTask, ctx);

        await mw.InvokeAsync(new DefaultHttpContext(), ctx);

        ctx.HasTenant.Should().BeTrue();
        ctx.CurrentTenantGuid.Should().Be(T);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public async Task A_completed_request_clears_the_tenant_it_set(string kind)
    {
        var ctx = Create(kind);
        var mw = new TenantMiddleware(_ => Task.CompletedTask, ctx);

        await mw.InvokeAsync(WithTenant(), ctx);

        ctx.HasTenant.Should().BeFalse();
    }

    /// <summary>An <see cref="ITenantContext"/> holding its state in plain fields, which the interface allows.</summary>
    private sealed class FieldContext : ITenantContext
    {
        public Guid? CurrentTenantGuid { get; private set; }
        public string? CurrentTenantName { get; private set; }
        public bool HasTenant => CurrentTenantGuid.HasValue;
        public void SetTenant(Guid tenantGuid, string? tenantName = null) { CurrentTenantGuid = tenantGuid; CurrentTenantName = tenantName; }
        public void ClearTenant() { CurrentTenantGuid = null; CurrentTenantName = null; }
        public TResult? WithTenant<TResult>(Guid tenantGuid, string? tenantName, Func<TResult> action) => action();
        public Task<TResult?> WithTenantAsync<TResult>(Guid tenantGuid, string? tenantName, Func<Task<TResult>> action) => action()!;
        public void WithTenant(Guid tenantGuid, string? tenantName, Action action) => action();
        public Task WithTenantAsync(Guid tenantGuid, string? tenantName, Func<Task> action) => action();
    }
}
