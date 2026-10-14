using FluentAssertions;
using Xunit;
using static Birko.Security.Grants.Tests.GrantsFixture;
using static Birko.Security.Grants.Tests.SignInFixture;

namespace Birko.Security.Grants.Tests;

public class BootstrapAdminTests
{
    private readonly SignInFixture _f = new();
    private readonly Guid _admin = Guid.NewGuid();

    [Fact]
    public async Task ListedIdentity_BecomesAdmin_OnFirstSignIn()
    {
        var result = await _f.SignInAsync(_admin, null, provider: BootstrapProvider, subject: BootstrapSubject);

        result.IsBootstrapAdmin.Should().BeTrue();
        result.AdminGrantWritten.Should().BeTrue();
        (await _f.Grants.Service.CanAsync(_admin, Draft, Guid.NewGuid(), "delete")).Should().BeTrue();
    }

    [Fact]
    public async Task TwoSignIns_WriteOneAdminGrant()
    {
        await _f.SignInAsync(_admin, null, provider: BootstrapProvider, subject: BootstrapSubject);
        var second = await _f.SignInAsync(_admin, null, provider: BootstrapProvider, subject: BootstrapSubject);

        second.AdminGrantWritten.Should().BeFalse();
        (await _f.Grants.AllAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ProviderComparesCaseInsensitively_SubjectExactly()
    {
        var upper = await _f.SignInAsync(_admin, null, provider: "GitHub", subject: BootstrapSubject);
        var other = Guid.NewGuid();
        var wrongSubject = await _f.SignInAsync(other, null, provider: BootstrapProvider, subject: BootstrapSubject + "0");

        upper.IsBootstrapAdmin.Should().BeTrue();
        wrongSubject.IsBootstrapAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task TheFirstUserToSignIn_GetsNothing_UnlessListed()
    {
        var first = Guid.NewGuid();

        var result = await _f.SignInAsync(first, "first@example.com", provider: "google", subject: "1");

        result.IsBootstrapAdmin.Should().BeFalse();
        (await _f.Grants.AllAsync()).Should().BeEmpty();
        (await _f.Grants.Service.CanAsync(first, Draft, Guid.NewGuid(), "read")).Should().BeFalse();
    }

    [Fact]
    public async Task SameSubject_FromAnotherProvider_IsNotAdmin()
    {
        var result = await _f.SignInAsync(_admin, null, provider: "google", subject: BootstrapSubject);

        result.IsBootstrapAdmin.Should().BeFalse();
    }

    [Theory]
    [InlineData("github")]
    [InlineData(":4821337")]
    [InlineData("github:")]
    [InlineData("4821337")]
    [InlineData("github:   ")]
    public void MalformedBootstrapEntry_Refuses(string entry)
    {
        var options = new GrantSignInOptions();
        options.BootstrapAdmins.Add(entry);

        var build = () => new Services.GrantSignInService(_f.InvitationStore, _f.Invitations, _f.Admins, _f.Grants.Clock, options);

        build.Should().Throw<GrantConfigurationException>();
    }

    [Fact]
    public async Task RevokeAdmin_RemovesAnotherAdmin_ButNeverTheLast()
    {
        var other = Guid.NewGuid();
        await _f.Grants.SeedAdminAsync(_admin);
        await _f.Grants.SeedAdminAsync(other);

        (await _f.Admins.RevokeAdminAsync(_admin, other)).Should().BeTrue();
        var last = () => _f.Admins.RevokeAdminAsync(_admin, _admin);

        (await last.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.LastAdmin);
        (await _f.Grants.Service.CanAsync(_admin, Draft, null, "delete")).Should().BeTrue();
        (await _f.Grants.Service.CanAsync(other, Draft, null, "delete")).Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAdmin_ByANonAdmin_IsRefused()
    {
        await _f.Grants.SeedAdminAsync(_admin);
        await _f.Grants.SeedAdminAsync(Guid.NewGuid());

        var revoke = () => _f.Admins.RevokeAdminAsync(Guid.NewGuid(), _admin);

        (await revoke.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.NotAuthorized);
    }

    [Fact]
    public async Task RevokeAdmin_OfANonAdmin_ReturnsFalse()
    {
        await _f.Grants.SeedAdminAsync(_admin);

        (await _f.Admins.RevokeAdminAsync(_admin, Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task ATenantAdmin_IsRemovable_WhileAGlobalAdminStillAdministersTheTenant()
    {
        var tenant = Guid.NewGuid();
        await _f.Grants.SeedAdminAsync(_admin);
        var tenantAdmin = Guid.NewGuid();
        await _f.Grants.SeedAdminAsync(tenantAdmin, tenant);

        (await _f.Admins.RevokeAdminAsync(_admin, tenantAdmin, tenant)).Should().BeTrue();
    }

    [Fact]
    public async Task TheOnlyAdminOfATenant_WithNoGlobalAdmin_IsNotRemovable()
    {
        var tenant = Guid.NewGuid();
        var tenantAdmin = Guid.NewGuid();
        await _f.Grants.SeedAdminAsync(tenantAdmin, tenant);

        var revoke = () => _f.Admins.RevokeAdminAsync(tenantAdmin, tenantAdmin, tenant);

        (await revoke.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.LastAdmin);
    }

    [Fact]
    public async Task ATenantAdmin_CannotRemoveAGlobalAdmin()
    {
        var tenant = Guid.NewGuid();
        await _f.Grants.SeedAdminAsync(_admin);
        await _f.Grants.SeedAdminAsync(Guid.NewGuid());
        var tenantAdmin = Guid.NewGuid();
        await _f.Grants.SeedAdminAsync(tenantAdmin, tenant);

        var revoke = () => _f.Admins.RevokeAdminAsync(tenantAdmin, _admin);

        (await revoke.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.NotAuthorized);
    }

    [Fact]
    public async Task BootstrapEntry_WithWhitespace_StillMatches()
    {
        var options = new GrantSignInOptions();
        options.BootstrapAdmins.Add(" GitHub : 4821337 ");
        var signIn = new Services.GrantSignInService(_f.InvitationStore, _f.Invitations, _f.Admins, _f.Grants.Clock, options);

        var result = await signIn.OnSignInAsync(new Services.SignInContext(_admin, "github", "4821337", null, false));

        result.IsBootstrapAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentSelfRevokes_WithNoSharedLock_NeverLeaveNoAdmin()
    {
        // Two instances: separate lock maps, so only the post-write recount keeps one admin.
        var other = Guid.NewGuid();
        await _f.Grants.SeedAdminAsync(_admin);
        await _f.Grants.SeedAdminAsync(other);
        var a = new Services.AdminGrantService(_f.Grants.Store, _f.Grants.Clock, null, new Internal.KeyedLock());
        var b = new Services.AdminGrantService(_f.Grants.Store, _f.Grants.Clock, null, new Internal.KeyedLock());
        _f.Grants.Store.CountDelay = TimeSpan.FromMilliseconds(50);

        var outcomes = await Task.WhenAll(Attempt(a, _admin), Attempt(b, other));

        outcomes.Should().Contain(GrantErrorCodes.LastAdmin);
        (await _f.Grants.AllAsync()).Should().Contain(g => g.Role == "*");
    }

    private static async Task<string?> Attempt(Services.AdminGrantService service, Guid user)
    {
        try
        {
            await service.RevokeAdminAsync(user, user);
            return null;
        }
        catch (GrantRefusedException ex)
        {
            return ex.Code;
        }
    }

    [Fact]
    public async Task ConcurrentSelfRevokesOfTwoAdmins_LeaveExactlyOne()
    {
        var other = Guid.NewGuid();
        await _f.Grants.SeedAdminAsync(_admin);
        await _f.Grants.SeedAdminAsync(other);
        _f.Grants.Store.CountDelay = TimeSpan.FromMilliseconds(50);

        var outcomes = await Task.WhenAll(Attempt(_admin), Attempt(other));

        outcomes.Count(o => o == null).Should().Be(1);
        (await _f.Grants.AllAsync()).Should().ContainSingle();
    }

    private async Task<string?> Attempt(Guid user)
    {
        try
        {
            await _f.Admins.RevokeAdminAsync(user, user);
            return null;
        }
        catch (GrantRefusedException ex)
        {
            return ex.Code;
        }
    }
}
