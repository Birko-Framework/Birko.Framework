using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Tenant;

public class TenantMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_TenantResolved_SetsTenantContext()
    {
        var tenantGuid = Guid.NewGuid();
        var resolver = new TestTenantResolver(new TenantInfo(tenantGuid, "Acme"));
        var tenantContext = new TestTenantContext();
        Guid? capturedTenantGuid = null;

        var middleware = new TenantMiddleware(_ =>
        {
            capturedTenantGuid = tenantContext.CurrentTenantGuid;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext(), resolver, tenantContext);

        capturedTenantGuid.Should().Be(tenantGuid);
    }

    [Fact]
    public async Task InvokeAsync_TenantResolved_ClearsAfterRequest()
    {
        var tenantGuid = Guid.NewGuid();
        var resolver = new TestTenantResolver(new TenantInfo(tenantGuid, "Acme"));
        var tenantContext = new TestTenantContext();

        var middleware = new TenantMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(new DefaultHttpContext(), resolver, tenantContext);

        tenantContext.HasTenant.Should().BeFalse();
        tenantContext.CurrentTenantGuid.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_NoTenantResolved_ContextRemainsEmpty()
    {
        var resolver = new TestTenantResolver(null);
        var tenantContext = new TestTenantContext();

        var middleware = new TenantMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(new DefaultHttpContext(), resolver, tenantContext);

        tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_NextThrows_StillClearsTenant()
    {
        var tenantGuid = Guid.NewGuid();
        var resolver = new TestTenantResolver(new TenantInfo(tenantGuid, "Acme"));
        var tenantContext = new TestTenantContext();

        var middleware = new TenantMiddleware(_ => throw new InvalidOperationException("boom"));

        var act = () => middleware.InvokeAsync(new DefaultHttpContext(), resolver, tenantContext);

        await act.Should().ThrowAsync<InvalidOperationException>();
        tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_CallsNextMiddleware()
    {
        var resolver = new TestTenantResolver(null);
        var tenantContext = new TestTenantContext();
        var nextCalled = false;

        var middleware = new TenantMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext(), resolver, tenantContext);

        nextCalled.Should().BeTrue();
    }

    private class TestTenantResolver : ITenantResolver
    {
        private readonly TenantInfo? _result;
        public TestTenantResolver(TenantInfo? result) => _result = result;
        public Task<TenantInfo?> ResolveAsync(HttpContext context, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private class TestTenantContext : ITenantContext
    {
        public Guid? CurrentTenantGuid { get; private set; }
        public string? CurrentTenantName { get; private set; }
        public bool HasTenant => CurrentTenantGuid.HasValue;

        public void SetTenant(Guid tenantGuid, string? tenantName = null)
        {
            CurrentTenantGuid = tenantGuid;
            CurrentTenantName = tenantName;
        }

        public void ClearTenant()
        {
            CurrentTenantGuid = null;
            CurrentTenantName = null;
        }
    }
}
