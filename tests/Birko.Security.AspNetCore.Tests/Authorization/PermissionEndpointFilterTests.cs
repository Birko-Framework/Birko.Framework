using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Authorization;

public class PermissionEndpointFilterTests
{
    [Fact]
    public void Constructor_NullPermission_ThrowsArgumentNullException()
    {
        var act = () => new PermissionEndpointFilter(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task InvokeAsync_Unauthenticated_ReturnsUnauthorized()
    {
        var user = new TestCurrentUser(isAuthenticated: false);
        var (context, next) = CreateFilterContext(user);
        var filter = new PermissionEndpointFilter("users.read");

        var result = await filter.InvokeAsync(context, next);

        result.Should().NotBe("ok");
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithPermission_CallsNext()
    {
        var user = new TestCurrentUser(isAuthenticated: true, permissions: new HashSet<string> { "users.read" });
        var (context, next) = CreateFilterContext(user);
        var filter = new PermissionEndpointFilter("users.read");

        var result = await filter.InvokeAsync(context, next);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithoutPermission_ReturnsForbidden()
    {
        var user = new TestCurrentUser(isAuthenticated: true, permissions: new HashSet<string> { "users.read" });
        var (context, next) = CreateFilterContext(user);
        var filter = new PermissionEndpointFilter("users.delete");

        var result = await filter.InvokeAsync(context, next);

        result.Should().NotBe("ok");
    }

    [Fact]
    public async Task InvokeAsync_WildcardPermission_CallsNext()
    {
        var user = new TestCurrentUser(isAuthenticated: true, permissions: new HashSet<string> { "*" });
        var (context, next) = CreateFilterContext(user);
        var filter = new PermissionEndpointFilter("any.permission");

        var result = await filter.InvokeAsync(context, next);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task InvokeAsync_NoCurrentUserRegistered_ReturnsUnauthorized()
    {
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        var context = new DefaultEndpointFilterInvocationContext(httpContext);
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>("ok");
        var filter = new PermissionEndpointFilter("users.read");

        var result = await filter.InvokeAsync(context, next);

        result.Should().NotBe("ok");
    }

    private static (EndpointFilterInvocationContext context, EndpointFilterDelegate next) CreateFilterContext(ICurrentUser user)
    {
        var services = new ServiceCollection();
        services.AddSingleton(user);
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        var context = new DefaultEndpointFilterInvocationContext(httpContext);
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>("ok");
        return (context, next);
    }

    private class TestCurrentUser : ICurrentUser
    {
        private readonly bool _isAuthenticated;

        public TestCurrentUser(bool isAuthenticated = false, HashSet<string>? permissions = null)
        {
            _isAuthenticated = isAuthenticated;
            Permissions = (IReadOnlySet<string>?)(permissions?.AsReadOnly()) ?? new HashSet<string>().AsReadOnly();
        }

        public Guid? UserId => _isAuthenticated ? Guid.NewGuid() : null;
        public string? Email => null;
        public Guid? TenantId => null;
        public IReadOnlySet<string> Roles => new HashSet<string>().AsReadOnly();
        public IReadOnlySet<string> Permissions { get; }
        public bool IsAuthenticated => _isAuthenticated;
        public string? GetClaim(string claimType) => null;
    }
}
