using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Tenant;

public class HeaderTenantResolverTests
{
    [Fact]
    public async Task ResolveAsync_ValidTenantGuidHeader_ReturnsTenantInfo()
    {
        var tenantGuid = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = tenantGuid.ToString();

        var resolver = new HeaderTenantResolver();
        var result = await resolver.ResolveAsync(httpContext);

        result.Should().NotBeNull();
        result!.TenantGuid.Should().Be(tenantGuid);
    }

    [Fact]
    public async Task ResolveAsync_WithTenantNameHeader_IncludesName()
    {
        var tenantGuid = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = tenantGuid.ToString();
        httpContext.Request.Headers["X-Tenant-Name"] = "Acme Corp";

        var resolver = new HeaderTenantResolver();
        var result = await resolver.ResolveAsync(httpContext);

        result.Should().NotBeNull();
        result!.TenantGuid.Should().Be(tenantGuid);
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
