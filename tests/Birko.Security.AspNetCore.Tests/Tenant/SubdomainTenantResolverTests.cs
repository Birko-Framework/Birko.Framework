using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Tenant;

public class SubdomainTenantResolverTests
{
    [Fact]
    public async Task ResolveAsync_WithBaseDomain_ExtractsSubdomain()
    {
        var tenantId = Guid.NewGuid();
        var resolver = new SubdomainTenantResolver(
            (subdomain, _) => Task.FromResult<TenantInfo?>(new TenantInfo(tenantId, subdomain)),
            "myapp.com");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("acme.myapp.com");

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().NotBeNull();
        result!.TenantId.Should().Be(tenantId);
        result.TenantName.Should().Be("acme");
    }

    [Fact]
    public async Task ResolveAsync_WithoutBaseDomain_TakesFirstSegment()
    {
        var tenantId = Guid.NewGuid();
        var resolver = new SubdomainTenantResolver(
            (subdomain, _) => Task.FromResult<TenantInfo?>(new TenantInfo(tenantId, subdomain)));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("tenant1.example.com");

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().NotBeNull();
        result!.TenantId.Should().Be(tenantId);
        result.TenantName.Should().Be("tenant1");
    }

    [Fact]
    public async Task ResolveAsync_NoSubdomain_ReturnsNull()
    {
        var resolver = new SubdomainTenantResolver(
            (_, _) => Task.FromResult<TenantInfo?>(new TenantInfo(Guid.NewGuid(), "x")),
            "myapp.com");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("myapp.com");

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_LookupReturnsNull_ReturnsNull()
    {
        var resolver = new SubdomainTenantResolver(
            (_, _) => Task.FromResult<TenantInfo?>(null),
            "myapp.com");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("unknown.myapp.com");

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_NoDot_WithoutBaseDomain_ReturnsNull()
    {
        var resolver = new SubdomainTenantResolver(
            (_, _) => Task.FromResult<TenantInfo?>(new TenantInfo(Guid.NewGuid(), "x")));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("localhost");

        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }
}
