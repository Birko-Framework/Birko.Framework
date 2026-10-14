using Birko.Data.InMemory.Stores;
using Birko.Security.Grants.Internal;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Services;
using Birko.Security.Grants.Stores;

namespace Birko.Security.Grants.Tests;

public class InMemoryInvitationStore : AsyncInMemoryStore<Invitation>, IInvitationStore
{
    /// <summary>
    /// Held after every single-entity read, so two redemptions on two instances both read the invitation before either
    /// claims it. Without it the in-memory store completes synchronously and the second redemption is turned away by
    /// the plain state check, so a test of the claim passes with the claim check removed (measured 2026-10-14).
    /// </summary>
    public TimeSpan ReadDelay { get; set; }

    // The public method, not a *Core seam: Birko.Data.InMemory overrides ReadAsync(Guid) itself and never reaches
    // ReadCoreAsync for it, so a delay there never runs (measured 2026-10-14). A test double only — never do this in a store.
    public override async Task<Invitation?> ReadAsync(Guid guid, CancellationToken ct = default)
    {
        var invitation = await base.ReadAsync(guid, ct);
        if (ReadDelay > TimeSpan.Zero)
            await Task.Delay(ReadDelay, ct);
        return invitation;
    }
}

/// <summary><see cref="GrantsFixture"/> plus invitations, admins and sign-in; the three new services share one private lock map, the grant service keeps its own.</summary>
public sealed class SignInFixture
{
    public const string BootstrapProvider = "github";
    public const string BootstrapSubject = "4821337";

    public GrantsFixture Grants { get; } = new();
    public InMemoryInvitationStore InvitationStore { get; } = new();
    public GrantSignInOptions Options { get; } = new();
    public InvitationService Invitations { get; }
    public AdminGrantService Admins { get; }
    public GrantSignInService SignIn { get; }

    public SignInFixture()
    {
        Options.BootstrapAdmins.Add($"{BootstrapProvider}:{BootstrapSubject}");
        var locks = new KeyedLock();
        Invitations = new InvitationService(InvitationStore, Grants.Service, Grants.Table, Grants.Clock, Options, locks);
        Admins = new AdminGrantService(Grants.Store, Grants.Clock, null, locks);
        SignIn = new GrantSignInService(InvitationStore, Invitations, Admins, Grants.Clock, Options);
    }

    public Task<SignInResult> SignInAsync(Guid user, string? email, bool verified = true, string? token = null,
        string provider = "google", string? subject = null)
        => SignIn.OnSignInAsync(new SignInContext(user, provider, subject ?? user.ToString(), email, verified, token));

    public async Task<List<Invitation>> AllInvitationsAsync() => (await InvitationStore.ReadAsync(null, null, null, null)).ToList();
}
