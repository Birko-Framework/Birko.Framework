using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.Grants.Internal;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Stores;
using Birko.Time;

namespace Birko.Security.Grants.Services;

/// <summary>Default <see cref="IInvitationService"/>; also redeems invitations for <see cref="GrantSignInService"/>.</summary>
/// <remarks>
/// <b>Single use across processes</b> rests on a <i>claim</i>: a filtered <see cref="PropertyUpdate{T}"/> that sets
/// <c>RedeemedBy</c> only where the invitation is still unredeemed and unrevoked, then a re-read to see whose claim stuck.
/// That holds across instances only on a backend that runs a filtered property update as <b>one conditional statement</b>:
/// the SQL providers (<c>UPDATE … WHERE</c>) and MongoDB (per-document <c>$set</c>). The portable fallback in
/// <c>AbstractAsyncBulkStore</c> reads and then writes, so file and in-memory stores get single use within one process,
/// from the per-invitation lock, and nothing more; they are single-process stores anyway. Revoke claims the same way.
/// </remarks>
public sealed class InvitationService : IInvitationService
{
    private readonly IInvitationStore _invitations;
    private readonly IGrantService _grantService;
    private readonly GrantRoleTable _table;
    private readonly IDateTimeProvider _clock;
    private readonly GrantSignInOptions _options;
    private readonly KeyedLock _locks;

    public InvitationService(IInvitationStore invitations, IGrantService grantService,
        GrantRoleTable table, IDateTimeProvider clock, GrantSignInOptions? options = null)
        : this(invitations, grantService, table, clock, options, KeyedLock.Shared)
    {
    }

    internal InvitationService(IInvitationStore invitations, IGrantService grantService,
        GrantRoleTable table, IDateTimeProvider clock, GrantSignInOptions? options, KeyedLock locks)
    {
        _invitations = invitations ?? throw new ArgumentNullException(nameof(invitations));
        _grantService = grantService ?? throw new ArgumentNullException(nameof(grantService));
        _table = table ?? throw new ArgumentNullException(nameof(table));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new GrantSignInOptions();
        if (_options.InvitationLifetime <= TimeSpan.Zero)
            throw new GrantConfigurationException($"Invitation lifetime must be positive; it is {_options.InvitationLifetime}.");
        _locks = locks;
    }

    public async Task<InvitationCreated> CreateAsync(Guid callerGuid, string email, string resourceType, Guid? resourceId, string role, Guid? tenantGuid = null, CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(email);
        if (!IsAcceptableEmail(normalized))
            throw new ArgumentException(
                "An invitation needs an ASCII email address with one '@'. Non-ASCII addresses are refused: case-folding them can map a different mailbox onto this one.",
                nameof(email));
        _table.RequireResourceType(resourceType);
        _table.RequireRole(role);

        await _grantService.AuthorizeGrantAsync(callerGuid, resourceType, resourceId, role, tenantGuid, ct).ConfigureAwait(false);

        var token = TokenHasher.NewToken();
        var now = _clock.UtcNow;
        var invitation = new Invitation
        {
            Email = normalized,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Role = role,
            TenantGuid = tenantGuid,
            TokenHash = TokenHasher.Hash(token),
            InvitedBy = callerGuid,
            CreatedAt = now,
            ExpiresAt = now + _options.InvitationLifetime,
        };
        await _invitations.CreateAsync(invitation, ct: ct).ConfigureAwait(false);
        return new InvitationCreated(invitation, token);
    }

    public async Task<bool> RevokeAsync(Guid callerGuid, Guid invitationGuid, CancellationToken ct = default)
    {
        using var _ = await _locks.AcquireAsync(LockKey(invitationGuid), ct).ConfigureAwait(false);

        var invitation = await _invitations.ReadAsync(invitationGuid, ct).ConfigureAwait(false);
        if (invitation == null || StateOf(invitation) != null)
            return false;

        if (callerGuid != invitation.InvitedBy)
            await _grantService.AuthorizeGrantAsync(callerGuid, invitation.ResourceType, invitation.ResourceId, invitation.Role, invitation.TenantGuid, ct).ConfigureAwait(false);

        var now = _clock.UtcNow;
        await _invitations.UpdateAsync(i => i.Guid == invitationGuid && i.RedeemedAt == null && i.RevokedAt == null,
            new PropertyUpdate<Invitation>().Set(i => i.RevokedAt, (DateTime?)now), ct).ConfigureAwait(false);

        // A redemption on another instance may have claimed it first: report what the store says, not what we meant.
        var after = await _invitations.ReadAsync(invitationGuid, CancellationToken.None).ConfigureAwait(false);
        return after != null && after.RedeemedAt == null && after.RevokedAt != null;
    }

    /// <summary>
    /// Redeems one invitation for <paramref name="userGuid"/>. Returns the redeemed invitation and a <c>null</c> code, or
    /// the <see cref="GrantErrorCodes"/> code saying why not — the invitation is then left pending (or as another
    /// redemption left it).
    /// </summary>
    internal async Task<(Invitation? Invitation, string? Code)> RedeemAsync(Guid invitationGuid, Guid userGuid, string normalizedEmail, CancellationToken ct)
    {
        using var _ = await _locks.AcquireAsync(LockKey(invitationGuid), ct).ConfigureAwait(false);

        var invitation = await _invitations.ReadAsync(invitationGuid, ct).ConfigureAwait(false);
        if (invitation == null)
            return (null, GrantErrorCodes.InvitationNotFound);
        // The email first: a token for someone else's invitation must not learn whether it is used, revoked or expired.
        if (invitation.Email != normalizedEmail)
            return (invitation, GrantErrorCodes.EmailMismatch);
        if (StateOf(invitation) is string state)
            return (invitation, state);

        // Claim it. Only an unredeemed, unrevoked invitation accepts the claim; the re-read says whose claim stuck.
        var now = _clock.UtcNow;
        await _invitations.UpdateAsync(i => i.Guid == invitationGuid && i.RedeemedAt == null && i.RevokedAt == null,
            new PropertyUpdate<Invitation>().Set(i => i.RedeemedAt, (DateTime?)now).Set(i => i.RedeemedBy, (Guid?)userGuid), ct).ConfigureAwait(false);
        var claimed = await _invitations.ReadAsync(invitationGuid, CancellationToken.None).ConfigureAwait(false);
        if (claimed == null)
            return (null, GrantErrorCodes.InvitationNotFound);
        if (claimed.RevokedAt != null)
            return (claimed, GrantErrorCodes.InvitationRevoked);
        if (claimed.RedeemedBy != userGuid)
            return (claimed, GrantErrorCodes.InvitationUsed);

        string? code;
        try
        {
            // As the inviter, so the grant rules run again now — an inviter who lost "share" invites nobody — and
            // never taking away actions the user already holds.
            await _grantService.GrantAtLeastAsync(invitation.InvitedBy, userGuid, invitation.ResourceType, invitation.ResourceId,
                invitation.Role, invitation.TenantGuid, ct).ConfigureAwait(false);
            return (claimed, null);
        }
        catch (GrantRefusedException ex)
        {
            code = ex.Code;
        }
        catch (GrantConfigurationException)
        {
            // A role or type retired after the invitation was written. Reported, never thrown: one stale invitation
            // must not break every later sign-in with that email.
            code = GrantErrorCodes.InvitationInvalid;
        }
        catch (OperationCanceledException)
        {
            await ReleaseClaimAsync(invitationGuid, userGuid).ConfigureAwait(false);
            throw;
        }

        await ReleaseClaimAsync(invitationGuid, userGuid).ConfigureAwait(false);
        return (invitation, code);
    }

    /// <summary>Why an invitation is not pending, or <c>null</c> when it is.</summary>
    internal string? StateOf(Invitation invitation)
    {
        if (invitation.RevokedAt != null) return GrantErrorCodes.InvitationRevoked;
        if (invitation.RedeemedAt != null) return GrantErrorCodes.InvitationUsed;
        if (invitation.ExpiresAt <= _clock.UtcNow) return GrantErrorCodes.InvitationExpired;
        return null;
    }

    /// <summary>
    /// Trims and folds ASCII <c>A–Z</c> only. Invariant lower-casing would also fold some non-ASCII code points onto ASCII
    /// (KELVIN SIGN U+212A becomes <c>k</c>), letting a different, verified mailbox match someone else's invitation.
    /// </summary>
    internal static string NormalizeEmail(string? email)
    {
        var trimmed = (email ?? string.Empty).Trim();
        var chars = trimmed.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= 'A' && chars[i] <= 'Z')
                chars[i] = (char)(chars[i] + ('a' - 'A'));
        }
        return new string(chars);
    }

    private static bool IsAcceptableEmail(string normalized)
    {
        var at = normalized.IndexOf('@');
        if (at <= 0 || at != normalized.LastIndexOf('@') || at == normalized.Length - 1)
            return false;
        foreach (var c in normalized)
        {
            if (c <= ' ' || c > '~')
                return false;
        }
        return true;
    }

    private Task ReleaseClaimAsync(Guid invitationGuid, Guid userGuid)
        => _invitations.UpdateAsync(i => i.Guid == invitationGuid && i.RedeemedBy == userGuid && i.RevokedAt == null,
            new PropertyUpdate<Invitation>().Set(i => i.RedeemedAt, (DateTime?)null).Set(i => i.RedeemedBy, (Guid?)null), CancellationToken.None);

    private static string LockKey(Guid invitationGuid) => $"invitation\n{invitationGuid}";
}
