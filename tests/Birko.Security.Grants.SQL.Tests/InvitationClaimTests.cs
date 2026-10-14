using Birko.Data.SQL.Connectors;
using Birko.Security.Grants.Internal;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Services;
using Birko.Security.Grants.SQL.Stores;
using Birko.Security.Grants.Stores;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Grants.SQL.Tests;

/// <summary>
/// The measurement TASK-589/590 left open: single use of an invitation across two instances rests on the SQL bulk
/// store running the claim, a filtered <c>PropertyUpdate</c>, as one <c>UPDATE … WHERE</c>. The in-memory suites could
/// only model that; this runs it on SQLite.
/// </summary>
public class InvitationClaimTests : IDisposable
{
    /// <summary>
    /// Holds every read-by-Guid so two redemptions both read the invitation before either claims it. Without it the
    /// second redemption is turned away by the plain state check and the claim is never exercised.
    /// </summary>
    private sealed class SlowReadInvitationStore : SqlInvitationStore<SqLiteConnector>
    {
        public TimeSpan ReadDelay { get; set; }

        public override async Task<Invitation?> ReadAsync(Guid guid, CancellationToken ct = default)
        {
            var invitation = await base.ReadAsync(guid, ct);
            if (ReadDelay > TimeSpan.Zero)
                await Task.Delay(ReadDelay, ct);
            return invitation;
        }
    }

    /// <summary>
    /// Parks the FIRST read-by-Guid on a gate, so a test can let one redemption read the invitation, run another to
    /// completion, and only then release the first to claim. A deterministic stale read, unlike a race.
    /// </summary>
    private sealed class GatedInvitationStore : SqlInvitationStore<SqLiteConnector>
    {
        private int _reads;
        public TaskCompletionSource FirstReadDone { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Armed { get; set; }

        public override async Task<Invitation?> ReadAsync(Guid guid, CancellationToken ct = default)
        {
            var invitation = await base.ReadAsync(guid, ct);
            if (Armed && Interlocked.Increment(ref _reads) == 1)
            {
                FirstReadDone.SetResult();
                await Release.Task;
            }
            return invitation;
        }
    }

    private readonly SqliteGrantsFixture _f = new();
    private readonly SlowReadInvitationStore _invitations = new();
    private readonly TestDateTimeProvider _clock = new();
    private readonly GrantRoleTable _table = new GrantOptions()
        .DefineRole("owner", "read", "edit", "share").DefineRole("editor", "read", "edit").ResourceType("doc").Build();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _doc = Guid.NewGuid();

    public InvitationClaimTests()
    {
        _invitations.SetSettings(_f.Settings);
    }

    public void Dispose() => _f.Dispose();

    /// <summary>One "process": its own lock map, its own services, the shared database.</summary>
    private GrantSignInService Instance()
    {
        var locks = new KeyedLock();
        var grants = new GrantService(_f.Grants, _table, _clock, null, locks);
        var invitations = new InvitationService(_invitations, grants, _table, _clock, null, locks);
        var admins = new AdminGrantService(_f.Grants, _clock, null, locks);
        return new GrantSignInService(_invitations, invitations, admins, _clock);
    }

    private async Task<Invitation> InviteAsync()
    {
        var grants = new GrantService(_f.Grants, _table, _clock);
        await grants.GrantOwnerOnCreateAsync(_owner, "doc", _doc);
        var invitations = new InvitationService(_invitations, grants, _table, _clock);
        return (await invitations.CreateAsync(_owner, "jana@example.com", "doc", _doc, "editor")).Invitation;
    }

    [Fact]
    public async Task TwoUsersWithOneEmail_OnTwoInstances_OneGetsTheGrant()
    {
        await InviteAsync();
        _invitations.ReadDelay = TimeSpan.FromMilliseconds(50);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var results = await Task.WhenAll(
            Instance().OnSignInAsync(new SignInContext(a, "google", "a", "jana@example.com", true)),
            Instance().OnSignInAsync(new SignInContext(b, "github", "b", "jana@example.com", true)));

        results.Sum(r => r.Redeemed.Count).Should().Be(1);
        results.SelectMany(r => r.Skipped).Should().ContainSingle().Which.Code.Should().Be(GrantErrorCodes.InvitationUsed);
        var editors = (await _f.Grants.ReadAsync(null, null, null, null)).Where(g => g.Role == "editor").ToList();
        editors.Should().ContainSingle();
        var winner = results.Single(r => r.Redeemed.Count == 1).Redeemed.Single();
        editors.Single().UserGuid.Should().Be(winner.RedeemedBy!.Value);
    }

    [Fact]
    public async Task AStaleRedemption_CannotOverwriteAClaimMadeMeanwhile()
    {
        // B reads the invitation while it is pending, then stalls; A redeems it completely; B resumes and claims.
        // Only the claim's WHERE RedeemedAt IS NULL stops B from overwriting A — the re-read alone would let B win.
        var gated = new GatedInvitationStore();
        gated.SetSettings(_f.Settings);
        var grantsSetup = new GrantService(_f.Grants, _table, _clock);
        await grantsSetup.GrantOwnerOnCreateAsync(_owner, "doc", _doc);
        await new InvitationService(gated, grantsSetup, _table, _clock).CreateAsync(_owner, "jana@example.com", "doc", _doc, "editor");
        gated.Armed = true;
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        GrantSignInService Instance(IInvitationStore store)
        {
            var locks = new KeyedLock();
            var grants = new GrantService(_f.Grants, _table, _clock, null, locks);
            return new GrantSignInService(store, new InvitationService(store, grants, _table, _clock, null, locks),
                new AdminGrantService(_f.Grants, _clock, null, locks), _clock);
        }

        var stale = Instance(gated).OnSignInAsync(new SignInContext(b, "github", "b", "jana@example.com", true));
        await gated.FirstReadDone.Task;
        var first = await Instance(gated).OnSignInAsync(new SignInContext(a, "google", "a", "jana@example.com", true));
        gated.Release.SetResult();
        var late = await stale;

        first.Redeemed.Should().ContainSingle();
        late.Redeemed.Should().BeEmpty();
        late.Skipped.Should().ContainSingle().Which.Code.Should().Be(GrantErrorCodes.InvitationUsed);
        (await _f.Grants.ReadAsync(null, null, null, null)).Where(g => g.Role == "editor").Should().ContainSingle()
            .Which.UserGuid.Should().Be(a);
    }

    [Fact]
    public async Task ARevokeAndARedemption_OnTwoInstances_CannotBothSucceed()
    {
        var invitation = await InviteAsync();
        _invitations.ReadDelay = TimeSpan.FromMilliseconds(50);
        var revoker = new InvitationService(_invitations, new GrantService(_f.Grants, _table, _clock, null, new KeyedLock()),
            _table, _clock, null, new KeyedLock());
        var user = Guid.NewGuid();

        var revoke = revoker.RevokeAsync(_owner, invitation.Guid!.Value);
        var signIn = Instance().OnSignInAsync(new SignInContext(user, "google", "u", "jana@example.com", true));
        await Task.WhenAll(revoke, signIn);

        var stored = (await _invitations.ReadAsync(invitation.Guid!.Value))!;
        var granted = (await _f.Grants.ReadAsync(null, null, null, null)).Any(g => g.UserGuid == user);
        (revoke.Result && signIn.Result.Redeemed.Count == 1).Should().BeFalse("the claim admits one of them");
        (stored.RevokedAt != null && stored.RedeemedAt != null).Should().BeFalse();
        granted.Should().Be(signIn.Result.Redeemed.Count == 1);
    }
}
