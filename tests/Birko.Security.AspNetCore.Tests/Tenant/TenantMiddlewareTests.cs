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
        var tenantId = Guid.NewGuid();
        var resolver = new TestTenantResolver(new TenantInfo(tenantId, "Acme"));
        var tenantContext = new TestTenantContext();
        Guid? capturedTenantId = null;

        var middleware = new TenantMiddleware(_ =>
        {
            capturedTenantId = tenantContext.CurrentTenantId;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext(), resolver, tenantContext);

        capturedTenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task InvokeAsync_TenantResolved_ClearsAfterRequest()
    {
        var tenantId = Guid.NewGuid();
        var resolver = new TestTenantResolver(new TenantInfo(tenantId, "Acme"));
        var tenantContext = new TestTenantContext();

        var middleware = new TenantMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(new DefaultHttpContext(), resolver, tenantContext);

        tenantContext.HasTenant.Should().BeFalse();
        tenantContext.CurrentTenantId.Should().BeNull();
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
        var tenantId = Guid.NewGuid();
        var resolver = new TestTenantResolver(new TenantInfo(tenantId, "Acme"));
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
        public Guid? CurrentTenantId { get; private set; }
        public string? CurrentTenantName { get; private set; }
        public bool HasTenant => CurrentTenantId.HasValue;

        public void SetTenant(Guid tenantId, string? tenantName = null)
        {
            CurrentTenantId = tenantId;
            CurrentTenantName = tenantName;
        }

        public void ClearTenant()
        {
            CurrentTenantId = null;
            CurrentTenantName = null;
        }
    }
}
