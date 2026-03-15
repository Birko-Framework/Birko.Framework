using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Tenant;

public class HeaderTenantResolverTests
{
    [Fact]
    public async Task ResolveAsync_ValidTenantIdHeader_ReturnsTenantInfo()
    {
        var tenantId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = tenantId.ToString();

        var resolver = new HeaderTenantResolver();
        var result = await resolver.ResolveAsync(httpContext);

        result.Should().NotBeNull();
        result!.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task ResolveAsync_WithTenantNameHeader_IncludesName()
    {
        var tenantId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = tenantId.ToString();
        httpContext.Request.Headers["X-Tenant-Name"] = "Acme Corp";

        var resolver = new HeaderTenantResolver();
        var result = await resolver.ResolveAsync(httpContext);

        result.Should().NotBeNull();
        result!.TenantId.Should().Be(tenantId);
        result.TenantName.Should().Be("Acme Corp");
    }

    [Fact]
    public async Task ResolveAsync_MissingHeader_ReturnsNull()
    {
        var httpContext = new DefaultHttpContext();

        var resolver = new HeaderTenantResolver();
        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_InvalidGuid_ReturnsNull()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "not-a-guid";

        var resolver = new HeaderTenantResolver();
        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_EmptyHeader_ReturnsNull()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "";

        var resolver = new HeaderTenantResolver();
        var result = await resolver.ResolveAsync(httpContext);

        result.Should().BeNull();
    }
}
