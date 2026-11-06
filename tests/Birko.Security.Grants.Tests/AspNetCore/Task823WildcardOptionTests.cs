using System.Security.Claims;
using Birko.Security.AspNetCore;
using Birko.Security.AspNetCore.Authorization;
using Birko.Security.Grants.AspNetCore;
using Birko.Security.Grants.Services;
using Birko.Security.Grants.Stores;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Security.Grants.Tests.AspNetCore;

/// <summary>
/// TASK-823 — with <see cref="BirkoSecurityOptions.WildcardPermissionEnabled"/> off, a <c>"*"</c> from the host's own
/// resolver is not superadmin, in the grants layer too; the admin grant still is (the user's decision, option 1).
/// Before: the host's <c>"*"</c> still made a grants admin, and the admin grant's <c>"*"</c> was stripped from
/// <c>ICurrentUser.Permissions</c>, so a grants admin was an admin to one layer and not to the other.
/// </summary>
public class Task823WildcardOptionTests
{
    private const string Secret = "task823-wildcard-option-tests-secret-key-0123456789";
    private readonly InMemoryResourceGrantStore _grants = new();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly Guid _hostSuperadmin = Guid.NewGuid();

    private ServiceProvider Host(bool wildcardEnabled)
    {
        var services = new ServiceCollection();
        services.AddBirkoSecurity(o => { o.Jwt.Secret = Secret; o.WildcardPermissionEnabled = wildcardEnabled; });
        services.UseResolvedPermissions();
        services.AddSingleton<IResourceGrantStore>(_grants);
        services.AddSingleton<IInvitationStore>(new InMemoryInvitationStore());
        services.AddSingleton<IUserPermissionResolver>(new FixedPermissionResolver().Give(_hostSuperadmin, "*", "billing:read"));
        services.AddBirkoGrants(o => GrantsFixture.Configure(o)).WithGrantPermissions();
        return services.BuildServiceProvider();
    }

    /// <summary>Runs the resolution middleware for <paramref name="user"/> and answers what <see cref="ICurrentUser"/> then holds.</summary>
    private static async Task<IReadOnlySet<string>> PermissionsOf(ServiceProvider sp, Guid user)
    {
        using var scope = sp.CreateScope();
        var http = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.ToString()) }, "test")),
        };
        sp.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        await new PermissionResolutionMiddleware(_ => Task.CompletedTask).InvokeAsync(
            http, scope.ServiceProvider.GetRequiredService<IUserPermissionResolver>(), scope.ServiceProvider.GetRequiredService<ClaimMappingOptions>());
        return scope.ServiceProvider.GetRequiredService<ICurrentUser>().Permissions;
    }

    private static async Task<bool> IsGrantsAdmin(ServiceProvider sp, Guid user)
    {
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IGrantAdminSource>().IsAdminAsync(user, null);
    }

    private Task SeedAdminGrant() => _grants.CreateAsync(new Models.ResourceGrant
    {
        UserGuid = _admin, ResourceType = GrantRoleTable.Wildcard, ResourceId = null, Role = GrantRoleTable.Wildcard, CreatedAt = DateTime.UtcNow,
    });

    [Fact]
    public async Task With_the_wildcard_off_the_hosts_star_makes_nobody_a_grants_admin()
    {
        using var sp = Host(wildcardEnabled: false);

        (await IsGrantsAdmin(sp, _hostSuperadmin)).Should().BeFalse();
        (await PermissionsOf(sp, _hostSuperadmin)).Should().BeEquivalentTo(new[] { "billing:read" });
    }

    [Fact]
    public async Task With_the_wildcard_off_an_admin_grant_holder_keeps_the_star()
    {
        await SeedAdminGrant();
        using var sp = Host(wildcardEnabled: false);

        (await IsGrantsAdmin(sp, _admin)).Should().BeTrue();
        (await PermissionsOf(sp, _admin)).Should().Contain("*", "the admin grant is Birko's own, not the host's data");
    }

    [Fact]
    public async Task With_the_wildcard_on_the_default_the_hosts_star_is_a_grants_admin() // contract pin
    {
        await SeedAdminGrant();
        using var sp = Host(wildcardEnabled: true);

        (await IsGrantsAdmin(sp, _hostSuperadmin)).Should().BeTrue();
        (await PermissionsOf(sp, _hostSuperadmin)).Should().Contain("*");
        (await PermissionsOf(sp, _admin)).Should().Contain("*");
    }
}
