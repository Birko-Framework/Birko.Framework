using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Grants.Models;

namespace Birko.Security.Grants.Services;

/// <summary>An invitation just created, and its token — returned this once and never stored. Sending it is the host's job.</summary>
public sealed record InvitationCreated(Invitation Invitation, string Token);

/// <summary>
/// Offers a role on a resource to an email address. Redemption happens at sign-in (<see cref="IGrantSignInService"/>),
/// and only for an email the identity provider marked verified.
/// </summary>
public interface IInvitationService
{
    /// <summary>
    /// Creates an invitation, authorized exactly as granting <paramref name="role"/> would be
    /// (<see cref="IGrantService.AuthorizeGrantAsync"/>). The email is trimmed and lower-cased.
    /// </summary>
    Task<InvitationCreated> CreateAsync(Guid callerGuid, string email, string resourceType, Guid? resourceId, string role, Guid? tenantGuid = null, CancellationToken ct = default);

    /// <summary>
    /// Revokes a pending invitation. The inviter may; so may anyone who could grant its role on its resource.
    /// Returns <c>false</c> when there is no such invitation or it is no longer pending (redeemed, revoked or expired).
    /// </summary>
    Task<bool> RevokeAsync(Guid callerGuid, Guid invitationGuid, CancellationToken ct = default);
}
