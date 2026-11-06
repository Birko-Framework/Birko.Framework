using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.AspNetCore.Authorization;
using Birko.Security.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Authorization;

/// <summary>
/// TASK-176 — the authorization low findings: SH-L217 (WildcardPermissionEnabled = false changed nothing for the
/// endpoint filter, the permission checker or owner access), SH-L219 (the checker granted to an unauthenticated
/// principal), SH-L229 (the resolution middleware stored a null resolver result and took a null next) and SH-L235
/// (AuthorizationContext treated "*" as a plain name).
/// </summary>
public class Task176AuthorizationTests
{
    private const string Secret = "task176-authorization-tests-secret-key-0123456789";
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static ClaimsPrincipal Principal(bool authenticated, params string[] permissions)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, UserId.ToString()) };
        if (permissions.Length > 0) claims.Add(new Claim(JwtClaimNames.Permission, string.Join(";", permissions)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null));
    }

    private static ClaimsCurrentUser User(ClaimsPrincipal principal, BirkoSecurityOptions? security) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } }, new ClaimMappingOptions(), security);

    private static async Task<bool> FilterLetsThrough(ICurrentUser user, string permission)
    {
        var services = new ServiceCollection().AddSingleton(user).BuildServiceProvider();
        var context = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext { RequestServices = services });
        var result = await new PermissionEndpointFilter(permission).InvokeAsync(context, _ => ValueTask.FromResult<object?>("ok"));
        return Equals(result, "ok");
    }

    // ── SH-L217 ──

    [Fact]
    public async Task With_the_wildcard_off_a_star_grants_nothing_anywhere()
    {
        var user = User(Principal(true, "*"), new BirkoSecurityOptions { WildcardPermissionEnabled = false });

        user.Permissions.Should().NotContain("*");
        (await FilterLetsThrough(user, "orders:delete")).Should().BeFalse("the endpoint filter");
        (await new ClaimsPermissionChecker(user).HasPermissionAsync(UserId, "orders:delete")).Should().BeFalse("the checker");
        user.SeesEverything().Should().BeFalse("owner access");
    }

    [Fact]
    public async Task With_the_wildcard_on_the_default_a_star_grants_everything()
    {
        var user = User(Principal(true, "*"), new BirkoSecurityOptions());

        (await FilterLetsThrough(user, "orders:delete")).Should().BeTrue();
        (await new ClaimsPermissionChecker(user).HasPermissionAsync(UserId, "orders:delete")).Should().BeTrue();
        user.SeesEverything().Should().BeTrue();
    }

    [Fact]
    public async Task The_resolution_middleware_applies_the_option_to_the_hosts_resolver()
    {
        // TASK-823 moved this from ResolvedPermissionsCurrentUser to the middleware, the one place that knows the resolver.
        var services = new ServiceCollection()
            .AddSingleton(new BirkoSecurityOptions { WildcardPermissionEnabled = false }).BuildServiceProvider();
        var http = new DefaultHttpContext { User = Principal(true), RequestServices = services };

        await new PermissionResolutionMiddleware(_ => Task.CompletedTask).InvokeAsync(http, new FixedResolver("*", "a"), new ClaimMappingOptions());

        new ResolvedPermissionsCurrentUser(new HttpContextAccessor { HttpContext = http }, new ClaimMappingOptions())
            .Permissions.Should().BeEquivalentTo(new[] { "a" });
    }

    [Fact]
    public async Task A_resolver_that_applies_the_option_itself_keeps_its_star() // TASK-823: the grants layer's admin grant
    {
        var services = new ServiceCollection()
            .AddSingleton(new BirkoSecurityOptions { WildcardPermissionEnabled = false }).BuildServiceProvider();
        var http = new DefaultHttpContext { User = Principal(true), RequestServices = services };

        await new PermissionResolutionMiddleware(_ => Task.CompletedTask).InvokeAsync(http, new GrantingResolver(), new ClaimMappingOptions());

        ((IReadOnlySet<string>)http.Items[PermissionResolutionMiddleware.ItemsKey]!).Should().Contain("*");
    }

    [Fact]
    public void With_the_wildcard_off_the_loopback_bypass_principal_keeps_its_star() // TASK-823
    {
        var loopback = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(JwtClaimNames.Permission, "*"), new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString()) },
            LoopbackBypass.AuthenticationType));

        User(loopback, new BirkoSecurityOptions { WildcardPermissionEnabled = false }).Permissions.Should().Contain("*");
    }

    private class FixedResolver : IUserPermissionResolver
    {
        private readonly string[] _permissions;
        public FixedResolver(params string[] permissions) => _permissions = permissions;

        public Task<IReadOnlySet<string>> GetAsync(Guid userId, Guid? tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(_permissions));
    }

    private sealed class GrantingResolver : FixedResolver, IAppliesWildcardOption
    {
        public GrantingResolver() : base("*") { }
    }

    [Fact]
    public void AddBirkoSecurity_hands_the_option_to_the_current_user_it_registers()
    {
        var services = new ServiceCollection();
        services.AddBirkoSecurity(o => { o.Jwt.Secret = Secret; o.WildcardPermissionEnabled = false; });
        using var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = Principal(true, "*", "a") };

        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>().Permissions.Should().BeEquivalentTo(new[] { "a" });
    }

    // ── SH-L219 ──

    [Fact]
    public async Task The_checker_grants_nothing_to_an_unauthenticated_principal()
    {
        var checker = new ClaimsPermissionChecker(User(Principal(false, "*", "a"), null));

        (await checker.HasPermissionAsync(UserId, "a")).Should().BeFalse();
        (await checker.GetPermissionsAsync(UserId)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_checker_grants_to_the_authenticated_caller()
    {
        var checker = new ClaimsPermissionChecker(User(Principal(true, "a"), null));

        (await checker.HasPermissionAsync(UserId, "a")).Should().BeTrue();
    }

    // ── SH-L235 ──

    [Fact]
    public void AuthorizationContext_treats_the_star_as_all_access()
    {
        new AuthorizationContext { Permissions = new[] { "*" } }.HasPermission("orders:delete").Should().BeTrue();
        new AuthorizationContext { Permissions = new[] { "a" } }.HasPermission("b").Should().BeFalse();
    }

    // ── SH-L229 ──

    [Fact]
    public void The_resolution_middleware_refuses_a_null_next() =>
        FluentActions.Invoking(() => new PermissionResolutionMiddleware(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public async Task A_resolver_answering_null_is_refused_not_read_as_no_permissions()
    {
        var middleware = new PermissionResolutionMiddleware(_ => Task.CompletedTask);
        var http = new DefaultHttpContext { User = Principal(true) };

        var act = () => middleware.InvokeAsync(http, new NullResolver(), new ClaimMappingOptions());

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(nameof(NullResolver));
        http.Items.ContainsKey(PermissionResolutionMiddleware.ItemsKey).Should().BeFalse();
    }

    private sealed class NullResolver : IUserPermissionResolver
    {
        public Task<IReadOnlySet<string>> GetAsync(Guid userId, Guid? tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(null!);
    }
}
