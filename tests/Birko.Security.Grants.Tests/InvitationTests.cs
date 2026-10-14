using Birko.Security.Grants.Internal;
using FluentAssertions;
using Xunit;
using static Birko.Security.Grants.Tests.GrantsFixture;

namespace Birko.Security.Grants.Tests;

public class InvitationTests
{
    private readonly SignInFixture _f = new();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _invitee = Guid.NewGuid();
    private readonly Guid _resource = Guid.NewGuid();
    private const string Email = "jana@example.com";

    public InvitationTests()
    {
        _f.Grants.SeedAsync(_owner, Presentation, _resource, "owner").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Create_ReturnsTheTokenOnce_AndStoresOnlyItsHash()
    {
        var created = await _f.Invitations.CreateAsync(_owner, "  Jana@Example.COM ", Presentation, _resource, "editor");

        var stored = (await _f.AllInvitationsAsync()).Single();
        stored.Email.Should().Be(Email);
        stored.TokenHash.Should().Be(TokenHasher.Hash(created.Token));
        stored.TokenHash.Should().NotContain(created.Token);
        created.Token.Length.Should().BeGreaterThanOrEqualTo(43);
        stored.ExpiresAt.Should().Be(_f.Grants.Clock.UtcNow + TimeSpan.FromDays(7));
        stored.InvitedBy.Should().Be(_owner);
    }

    [Fact]
    public async Task Create_ByACallerWhoCannotGrantTheRole_IsRefused()
    {
        var create = () => _f.Invitations.CreateAsync(Guid.NewGuid(), Email, Presentation, _resource, "viewer");

        (await create.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.NotAuthorized);
        (await _f.AllInvitationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ARoleAboveTheInvitersOwn_IsEscalation()
    {
        var sharer = Guid.NewGuid();
        await _f.Grants.SeedAsync(sharer, Presentation, _resource, "sharer");

        var create = () => _f.Invitations.CreateAsync(sharer, Email, Presentation, _resource, "owner");

        (await create.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.Escalation);
    }

    [Fact]
    public async Task Create_EmptyEmail_Refuses()
    {
        var create = () => _f.Invitations.CreateAsync(_owner, "  ", Presentation, _resource, "viewer");

        await create.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task VerifiedSignIn_RedeemsByEmail_WithTheInviterAsGranter()
    {
        await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");

        var result = await _f.SignInAsync(_invitee, "JANA@example.com", verified: true);

        result.Redeemed.Should().ContainSingle().Which.RedeemedBy.Should().Be(_invitee);
        result.Skipped.Should().BeEmpty();
        (await _f.Grants.Service.CanAsync(_invitee, Presentation, _resource, "edit")).Should().BeTrue();
        var grant = (await _f.Grants.AllAsync()).Single(g => g.UserGuid == _invitee);
        grant.GrantedBy.Should().Be(_owner);
        var invitation = (await _f.AllInvitationsAsync()).Single();
        invitation.RedeemedBy.Should().Be(_invitee);
        invitation.RedeemedAt.Should().Be(_f.Grants.Clock.UtcNow);
    }

    [Fact]
    public async Task UnverifiedSignIn_RedeemsNothing_AndReportsIt()
    {
        await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");

        var result = await _f.SignInAsync(_invitee, Email, verified: false);

        result.Redeemed.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.Code.Should().Be(GrantErrorCodes.EmailNotVerified);
        (await _f.Grants.Service.CanAsync(_invitee, Presentation, _resource, "read")).Should().BeFalse();
        (await _f.AllInvitationsAsync()).Single().RedeemedAt.Should().BeNull();
    }

    [Fact]
    public async Task UnverifiedSignIn_WithAnExplicitToken_Throws()
    {
        var created = await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");

        var signIn = () => _f.SignInAsync(_invitee, Email, verified: false, token: created.Token);

        (await signIn.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.EmailNotVerified);
        (await _f.Grants.Service.CanAsync(_invitee, Presentation, _resource, "read")).Should().BeFalse();
    }

    [Fact]
    public async Task ExplicitToken_IsRedeemed()
    {
        var created = await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");

        var result = await _f.SignInAsync(_invitee, Email, token: created.Token);

        result.Redeemed.Should().ContainSingle();
        (await _f.Grants.Service.CanAsync(_invitee, Presentation, _resource, "edit")).Should().BeTrue();
    }

    [Fact]
    public async Task ExplicitToken_ForAnotherEmail_IsEmailMismatch()
    {
        var created = await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");

        var signIn = () => _f.SignInAsync(_invitee, "someone.else@example.com", token: created.Token);

        (await signIn.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.EmailMismatch);
    }

    [Fact]
    public async Task UnknownToken_IsNotFound()
    {
        var signIn = () => _f.SignInAsync(_invitee, Email, token: "not-a-token");

        (await signIn.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.InvitationNotFound);
    }

    [Fact]
    public async Task ExpiredInvitation_IsNotRedeemed()
    {
        var created = await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");
        _f.Grants.Clock.Advance(TimeSpan.FromDays(7));

        var byEmail = await _f.SignInAsync(_invitee, Email);
        var byToken = () => _f.SignInAsync(_invitee, Email, token: created.Token);

        byEmail.Redeemed.Should().BeEmpty();
        (await byToken.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.InvitationExpired);
        (await _f.Grants.Service.CanAsync(_invitee, Presentation, _resource, "read")).Should().BeFalse();
    }

    [Fact]
    public async Task UsedInvitation_CannotBeRedeemedAgain()
    {
        var created = await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");
        await _f.SignInAsync(_invitee, Email);

        var other = Guid.NewGuid();
        var reuse = () => _f.SignInAsync(other, Email, token: created.Token);

        (await reuse.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.InvitationUsed);
        (await _f.Grants.Service.CanAsync(other, Presentation, _resource, "read")).Should().BeFalse();
    }

    [Fact]
    public async Task RevokedInvitation_IsNotRedeemed()
    {
        var created = await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");

        (await _f.Invitations.RevokeAsync(_owner, created.Invitation.Guid!.Value)).Should().BeTrue();
        var signIn = () => _f.SignInAsync(_invitee, Email, token: created.Token);

        (await signIn.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.InvitationRevoked);
        (await _f.SignInAsync(_invitee, Email)).Redeemed.Should().BeEmpty();
    }

    [Fact]
    public async Task Revoke_ByAStranger_IsRefused_AndOfANonPendingInvitation_ReturnsFalse()
    {
        var created = await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");
        var id = created.Invitation.Guid!.Value;

        var stranger = () => _f.Invitations.RevokeAsync(Guid.NewGuid(), id);
        await stranger.Should().ThrowAsync<GrantRefusedException>();

        await _f.SignInAsync(_invitee, Email);
        (await _f.Invitations.RevokeAsync(_owner, id)).Should().BeFalse();
        (await _f.Invitations.RevokeAsync(_owner, Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task InviterWhoLostShare_InvitesNobody()
    {
        var sharer = Guid.NewGuid();
        await _f.Grants.SeedAsync(sharer, Presentation, _resource, "sharer");
        await _f.Invitations.CreateAsync(sharer, Email, Presentation, _resource, "viewer");
        await _f.Grants.Service.RevokeAsync(_owner, sharer, Presentation, _resource);

        var result = await _f.SignInAsync(_invitee, Email);

        result.Redeemed.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.Code.Should().Be(GrantErrorCodes.NotAuthorized);
        (await _f.Grants.Service.CanAsync(_invitee, Presentation, _resource, "read")).Should().BeFalse();
    }

    [Fact]
    public async Task Redeeming_NeverDowngradesAnExistingRole()
    {
        await _f.Grants.SeedAsync(_invitee, Presentation, _resource, "editor");
        await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "viewer");

        var result = await _f.SignInAsync(_invitee, Email);

        result.Redeemed.Should().ContainSingle();
        (await _f.Grants.Service.CanAsync(_invitee, Presentation, _resource, "edit")).Should().BeTrue();
    }

    [Fact]
    public async Task Redeeming_ARoleThatWouldTakeActionsAway_IsARoleConflict()
    {
        await _f.Grants.SeedAsync(_invitee, Presentation, _resource, "sharer");
        await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");

        var result = await _f.SignInAsync(_invitee, Email);

        result.Skipped.Should().ContainSingle().Which.Code.Should().Be(GrantErrorCodes.RoleConflict);
        (await _f.Grants.Service.CanAsync(_invitee, Presentation, _resource, "share")).Should().BeTrue();
    }

    [Fact]
    public async Task Redeeming_UpgradesALesserRole()
    {
        await _f.Grants.SeedAsync(_invitee, Presentation, _resource, "viewer");
        await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");

        await _f.SignInAsync(_invitee, Email);

        (await _f.Grants.AllAsync()).Single(g => g.UserGuid == _invitee).Role.Should().Be("editor");
    }

    [Fact]
    public async Task TheSameInvitationRedeemedTwiceAtOnce_ProducesOneGrant()
    {
        await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");
        _f.Grants.Store.CountDelay = TimeSpan.FromMilliseconds(30);

        var results = await Task.WhenAll(_f.SignInAsync(_invitee, Email), _f.SignInAsync(_invitee, Email));

        results.Sum(r => r.Redeemed.Count).Should().Be(1);
        (await _f.Grants.AllAsync()).Where(g => g.UserGuid == _invitee).Should().ContainSingle();
    }

    [Fact]
    public async Task TenantBoundInvitation_GrantsInItsTenant()
    {
        var tenant = Guid.NewGuid();
        await _f.Grants.SeedAsync(_owner, Draft, _resource, "owner", tenant);
        await _f.Invitations.CreateAsync(_owner, Email, Draft, _resource, "viewer", tenant);

        await _f.SignInAsync(_invitee, Email);

        (await _f.Grants.Service.CanAsync(_invitee, Draft, _resource, "read", tenant)).Should().BeTrue();
        (await _f.Grants.Service.CanAsync(_invitee, Draft, _resource, "read")).Should().BeFalse();
    }

    [Fact]
    public async Task TwoUsersWithOneEmail_OnTwoInstances_OnlyOneGetsTheGrant()
    {
        // Two unlinked local accounts with the same verified email, signing in at once on two instances (no shared lock).
        await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");
        var second = new Services.InvitationService(_f.InvitationStore, _f.Grants.Service, _f.Grants.Table, _f.Grants.Clock, _f.Options, new KeyedLock());
        var secondSignIn = new Services.GrantSignInService(_f.InvitationStore, second, _f.Admins, _f.Grants.Clock, _f.Options);
        var other = Guid.NewGuid();
        _f.InvitationStore.ReadDelay = TimeSpan.FromMilliseconds(30);

        var results = await Task.WhenAll(
            _f.SignInAsync(_invitee, Email),
            secondSignIn.OnSignInAsync(new Services.SignInContext(other, "github", "x", Email, true)));

        results.Sum(r => r.Redeemed.Count).Should().Be(1);
        var holders = (await _f.Grants.AllAsync()).Where(g => g.ResourceId == _resource && g.Role == "editor").Select(g => g.UserGuid).ToList();
        holders.Should().ContainSingle();
    }

    [Fact]
    public async Task AFailedRedemption_ReleasesItsClaim_SoTheInvitationStaysPending()
    {
        var sharer = Guid.NewGuid();
        await _f.Grants.SeedAsync(sharer, Presentation, _resource, "sharer");
        await _f.Invitations.CreateAsync(sharer, Email, Presentation, _resource, "viewer");
        await _f.Grants.Service.RevokeAsync(_owner, sharer, Presentation, _resource);

        await _f.SignInAsync(_invitee, Email);

        var invitation = (await _f.AllInvitationsAsync()).Single();
        invitation.RedeemedAt.Should().BeNull();
        invitation.RedeemedBy.Should().BeNull();
    }

    [Fact]
    public async Task InvitationForARetiredRole_IsReported_AndDoesNotBreakSignIn()
    {
        await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");
        await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "viewer");
        var retired = (await _f.AllInvitationsAsync()).Single(i => i.Role == "editor");
        retired.Role = "retired-role";
        await _f.InvitationStore.UpdateAsync(retired);

        var result = await _f.SignInAsync(_invitee, Email);

        result.Skipped.Should().ContainSingle().Which.Code.Should().Be(GrantErrorCodes.InvitationInvalid);
        result.Redeemed.Should().ContainSingle().Which.Role.Should().Be("viewer");
    }

    [Fact]
    public async Task ADeadLink_DoesNotStopTheOtherInvitations()
    {
        await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");

        var signIn = () => _f.SignInAsync(_invitee, Email, token: "dead-link");

        (await signIn.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.InvitationNotFound);
        (await _f.Grants.Service.CanAsync(_invitee, Presentation, _resource, "edit")).Should().BeTrue();
    }

    [Fact]
    public async Task ATokenForAnotherEmail_LearnsNothingAboutItsState()
    {
        var created = await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "editor");
        await _f.Invitations.RevokeAsync(_owner, created.Invitation.Guid!.Value);

        var signIn = () => _f.SignInAsync(_invitee, "someone.else@example.com", token: created.Token);

        (await signIn.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.EmailMismatch);
    }

    [Theory]
    [InlineData("Kate@example.com")]   // KELVIN SIGN, which invariant lower-casing folds to 'k'
    [InlineData("jana@exämple.com")]
    [InlineData("no-at-sign")]
    [InlineData("two@@example.com")]
    [InlineData("@example.com")]
    public async Task Create_RefusesAddressesThatAreNotPlainAscii(string email)
    {
        var create = () => _f.Invitations.CreateAsync(_owner, email, Presentation, _resource, "viewer");

        await create.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AVerifiedLookalikeAddress_DoesNotRedeemTheAsciiInvitation()
    {
        await _f.Invitations.CreateAsync(_owner, "kate@example.com", Presentation, _resource, "editor");

        var result = await _f.SignInAsync(_invitee, "Kate@example.com");

        result.Redeemed.Should().BeEmpty();
        (await _f.Grants.Service.CanAsync(_invitee, Presentation, _resource, "read")).Should().BeFalse();
    }

    [Fact]
    public async Task Revoke_ByANonInviterWhoCanShare_IsAllowed()
    {
        var created = await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "viewer");
        var coOwner = Guid.NewGuid();
        await _f.Grants.SeedAsync(coOwner, Presentation, _resource, "owner");

        (await _f.Invitations.RevokeAsync(coOwner, created.Invitation.Guid!.Value)).Should().BeTrue();
    }

    [Fact]
    public async Task TypeWideInvitation_ByAnExactOwner_IsRefused()
    {
        var create = () => _f.Invitations.CreateAsync(_owner, Email, Presentation, null, "viewer");

        (await create.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.NotAuthorized);
    }

    [Fact]
    public async Task Create_UndeclaredRole_Refuses_EvenForAnAdmin()
    {
        var admin = Guid.NewGuid();
        await _f.Grants.SeedAdminAsync(admin);

        var create = () => _f.Invitations.CreateAsync(admin, Email, Presentation, _resource, "superuser");

        await create.Should().ThrowAsync<GrantConfigurationException>();
    }

    [Fact]
    public async Task Admin_MayRevokeAnInvitationWhoseRoleWasRetired()
    {
        var admin = Guid.NewGuid();
        await _f.Grants.SeedAdminAsync(admin);
        var created = await _f.Invitations.CreateAsync(_owner, Email, Presentation, _resource, "viewer");
        var stored = (await _f.AllInvitationsAsync()).Single();
        stored.Role = "retired-role";
        await _f.InvitationStore.UpdateAsync(stored);

        (await _f.Invitations.RevokeAsync(admin, created.Invitation.Guid!.Value)).Should().BeTrue();
    }

    [Fact]
    public void NonPositiveLifetime_Refuses()
    {
        var options = new GrantSignInOptions { InvitationLifetime = TimeSpan.Zero };

        var build = () => new Services.InvitationService(_f.InvitationStore, _f.Grants.Service, _f.Grants.Table, _f.Grants.Clock, options);

        build.Should().Throw<GrantConfigurationException>();
    }
}
