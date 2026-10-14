using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Grants.Internal;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Stores;
using Birko.Time;

namespace Birko.Security.Grants.Services;

/// <summary>Default <see cref="IGrantSignInService"/>.</summary>
public sealed class GrantSignInService : IGrantSignInService
{
    private readonly IInvitationStore _invitations;
    private readonly InvitationService _invitationService;
    private readonly AdminGrantService _adminService;
    private readonly IDateTimeProvider _clock;
    private readonly HashSet<(string Provider, string Subject)> _bootstrapAdmins;

    public GrantSignInService(IInvitationStore invitations, InvitationService invitationService, AdminGrantService adminService,
        IDateTimeProvider clock, GrantSignInOptions? options = null)
    {
        _invitations = invitations ?? throw new ArgumentNullException(nameof(invitations));
        _invitationService = invitationService ?? throw new ArgumentNullException(nameof(invitationService));
        _adminService = adminService ?? throw new ArgumentNullException(nameof(adminService));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _bootstrapAdmins = ParseBootstrapAdmins((options ?? new GrantSignInOptions()).BootstrapAdmins);
    }

    public async Task<SignInResult> OnSignInAsync(SignInContext context, CancellationToken ct = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));

        var isBootstrapAdmin = _bootstrapAdmins.Contains((Key(context.Provider), (context.Subject ?? string.Empty).Trim()));
        var adminGrantWritten = isBootstrapAdmin
            && await _adminService.EnsureBootstrapAdminAsync(context.UserGuid, ct).ConfigureAwait(false);

        var email = InvitationService.NormalizeEmail(context.Email);
        var redeemed = new List<Invitation>();
        var skipped = new List<SkippedInvitation>();

        // The token's invitation, if any, is looked up first only so the email pass leaves it to the token pass.
        Invitation? tokenInvitation = null;
        if (!string.IsNullOrEmpty(context.InvitationToken))
            tokenInvitation = await _invitations.GetByTokenHashAsync(TokenHasher.Hash(context.InvitationToken), ct).ConfigureAwait(false);

        if (email.Length > 0)
        {
            var pending = (await _invitations.ListPendingForEmailAsync(email, _clock.UtcNow, ct).ConfigureAwait(false))
                .Where(i => tokenInvitation == null || i.Guid != tokenInvitation.Guid)
                .ToList();
            foreach (var invitation in pending)
            {
                if (!context.EmailVerified)
                {
                    skipped.Add(new SkippedInvitation(invitation, GrantErrorCodes.EmailNotVerified));
                    continue;
                }

                var (after, code) = await _invitationService.RedeemAsync(invitation.Guid!.Value, context.UserGuid, email, ct).ConfigureAwait(false);
                if (code == null)
                    redeemed.Add(after!);
                else
                    skipped.Add(new SkippedInvitation(after ?? invitation, code));
            }
        }

        // The explicit token last, so a dead link cannot stop the bootstrap grant or the user's other invitations —
        // everything above is already written when this throws. It throws because the user followed a link and
        // must be told why it did nothing.
        if (!string.IsNullOrEmpty(context.InvitationToken))
        {
            if (tokenInvitation == null)
                throw Refused(GrantErrorCodes.InvitationNotFound, "No invitation matches this link.");
            if (!context.EmailVerified)
                throw Refused(GrantErrorCodes.EmailNotVerified, "The identity provider has not verified this account's email, so the invitation cannot be redeemed.");

            var (after, code) = await _invitationService.RedeemAsync(tokenInvitation.Guid!.Value, context.UserGuid, email, ct).ConfigureAwait(false);
            if (code != null)
                throw Refused(code, $"The invitation cannot be redeemed: {code}.");
            redeemed.Add(after!);
        }

        return new SignInResult(isBootstrapAdmin, adminGrantWritten, redeemed, skipped);
    }

    private static HashSet<(string, string)> ParseBootstrapAdmins(IEnumerable<string> entries)
    {
        var set = new HashSet<(string, string)>();
        foreach (var entry in entries)
        {
            var colon = entry?.IndexOf(':') ?? -1;
            var provider = colon > 0 ? entry![..colon].Trim() : string.Empty;
            var subject = colon > 0 ? entry![(colon + 1)..].Trim() : string.Empty;
            if (provider.Length == 0 || subject.Length == 0)
                throw new GrantConfigurationException($"Bootstrap admin '{entry}' is not 'provider:subject'.");
            set.Add((Key(provider), subject));
        }
        return set;
    }

    private static string Key(string? provider) => (provider ?? string.Empty).Trim().ToLowerInvariant();

    private static GrantRefusedException Refused(string code, string message) => new(code, message);
}
