using Birko.Security.Grants.Internal;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Services;
using Birko.Security.Grants.Stores;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Grants.SQL.Tests;

/// <summary>
/// The services write some fields through a filtered <c>PropertyUpdate</c>, which on SQL binds values in
/// <c>PropertyUpdateSqlTranslator</c> rather than through the field's own write path (found at TASK-590's close review).
/// Each such value must read back exactly as the same value written through a whole-entity create.
/// </summary>
public class PropertyUpdatePathTests : IDisposable
{
    private readonly SqliteGrantsFixture _f = new();
    private readonly TestDateTimeProvider _clock = new(new DateTimeOffset(2026, 10, 14, 9, 30, 15, 123, TimeSpan.Zero));
    private readonly GrantRoleTable _table = new GrantOptions()
        .DefineRole("owner", "read", "edit", "share").DefineRole("editor", "read", "edit").DefineRole("viewer", "read")
        .ResourceType("doc").Build();

    public void Dispose() => _f.Dispose();

    private IInvitationStore Invitations => _f.Invitations;

    /// <summary>The same instant written by a plain create — the reference the update path must match.</summary>
    private async Task<DateTime> ThroughCreateAsync(DateTime value)
    {
        var reference = new Invitation
        {
            Email = "ref@example.com", ResourceType = "doc", Role = "viewer", TokenHash = Guid.NewGuid().ToString("N"),
            CreatedAt = value, ExpiresAt = value, RedeemedAt = value,
        };
        await Invitations.CreateAsync(reference);
        return (await Invitations.ReadAsync(reference.Guid!.Value))!.RedeemedAt!.Value;
    }

    [Fact]
    public async Task RedeemedAt_And_RedeemedBy_WrittenByTheClaim_ReadBackLikeACreatedValue()
    {
        var grants = new GrantService(_f.Grants, _table, _clock, null, new KeyedLock());
        var owner = Guid.NewGuid();
        var doc = Guid.NewGuid();
        await grants.GrantOwnerOnCreateAsync(owner, "doc", doc);
        var invitations = new InvitationService(Invitations, grants, _table, _clock, null, new KeyedLock());
        var created = await invitations.CreateAsync(owner, "jana@example.com", "doc", doc, "editor");
        var user = Guid.NewGuid();

        var redeemed = await invitations.RedeemAsync(created.Invitation.Guid!.Value, user, "jana@example.com", default);

        redeemed.Code.Should().BeNull();
        var stored = (await Invitations.ReadAsync(created.Invitation.Guid!.Value))!;
        var reference = await ThroughCreateAsync(_clock.UtcNow);
        stored.RedeemedAt!.Value.Ticks.Should().Be(reference.Ticks);
        stored.RedeemedAt!.Value.Kind.Should().Be(reference.Kind);
        stored.RedeemedBy.Should().Be(user);
    }

    [Fact]
    public async Task RevokedAt_WrittenByRevoke_ReadsBackLikeACreatedValue()
    {
        var grants = new GrantService(_f.Grants, _table, _clock, null, new KeyedLock());
        var owner = Guid.NewGuid();
        var doc = Guid.NewGuid();
        await grants.GrantOwnerOnCreateAsync(owner, "doc", doc);
        var invitations = new InvitationService(Invitations, grants, _table, _clock, null, new KeyedLock());
        var created = await invitations.CreateAsync(owner, "jana@example.com", "doc", doc, "editor");

        (await invitations.RevokeAsync(owner, created.Invitation.Guid!.Value)).Should().BeTrue();

        var stored = (await Invitations.ReadAsync(created.Invitation.Guid!.Value))!;
        var reference = await ThroughCreateAsync(_clock.UtcNow);
        stored.RevokedAt!.Value.Ticks.Should().Be(reference.Ticks);
        stored.RevokedAt!.Value.Kind.Should().Be(reference.Kind);
    }

    [Fact]
    public async Task ARoleAndGrantedBy_ReplacedByKey_ReadBackExactly()
    {
        var grants = new GrantService(_f.Grants, _table, _clock, null, new KeyedLock());
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var doc = Guid.NewGuid();
        await grants.GrantOwnerOnCreateAsync(owner, "doc", doc);
        await grants.GrantAsync(owner, other, "doc", doc, "viewer");

        await grants.GrantAsync(owner, other, "doc", doc, "editor");   // PropertyUpdate by key: Role + GrantedBy

        var stored = await ((IResourceGrantStore)_f.Grants).GetExactAsync(other, "doc", doc, null);
        stored!.Role.Should().Be("editor");
        stored.GrantedBy.Should().Be(owner);
    }

    [Fact]
    public async Task ReleasingAClaim_WritesNullsBack()
    {
        // A claim whose grant fails is released by a PropertyUpdate that sets RedeemedAt / RedeemedBy to null.
        var grants = new GrantService(_f.Grants, _table, _clock, null, new KeyedLock());
        var sharer = Guid.NewGuid();
        var doc = Guid.NewGuid();
        await grants.GrantOwnerOnCreateAsync(Guid.NewGuid(), "doc", doc);
        await _f.Grants.CreateAsync(new ResourceGrant { UserGuid = sharer, ResourceType = "doc", ResourceId = doc, Role = "owner" });
        var invitations = new InvitationService(Invitations, grants, _table, _clock, null, new KeyedLock());
        var created = await invitations.CreateAsync(sharer, "jana@example.com", "doc", doc, "viewer");
        await grants.RevokeAsync(sharer, sharer, "doc", doc);   // the inviter loses share, so redemption is refused

        var result = await invitations.RedeemAsync(created.Invitation.Guid!.Value, Guid.NewGuid(), "jana@example.com", default);

        result.Code.Should().Be(GrantErrorCodes.NotAuthorized);
        var stored = (await Invitations.ReadAsync(created.Invitation.Guid!.Value))!;
        stored.RedeemedAt.Should().BeNull();
        stored.RedeemedBy.Should().BeNull();
    }
}
