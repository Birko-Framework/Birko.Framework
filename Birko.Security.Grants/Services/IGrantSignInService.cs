using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Grants.Models;

namespace Birko.Security.Grants.Services;

/// <summary>
/// What the host knows at sign-in, after mapping the external identity to its own user.
/// </summary>
/// <param name="UserGuid">The host's local user — never the provider's raw subject.</param>
/// <param name="Provider">The identity provider, e.g. <c>github</c>; matched against bootstrap admins.</param>
/// <param name="Subject">The provider's stable user id (OIDC <c>sub</c>).</param>
/// <param name="Email">The email the provider returned, if any.</param>
/// <param name="EmailVerified">
/// The provider's own statement that the email is verified — OIDC's <c>email_verified</c> claim, or GitHub's
/// <c>/user/emails</c> <c>verified</c> flag. <b>Not</b> <c>UserLogin.IsVerified</c>, which is "always true for OAuth"
/// and says nothing about the email. Invitations redeem only when this is true.
/// </param>
/// <param name="InvitationToken">A token the user arrived with (an invitation link), if any.</param>
public sealed record SignInContext(
    Guid UserGuid,
    string Provider,
    string Subject,
    string? Email,
    bool EmailVerified,
    string? InvitationToken = null);

/// <summary>An invitation that was found for the user but not redeemed, and why (a <see cref="GrantErrorCodes"/> code).</summary>
public sealed record SkippedInvitation(Invitation Invitation, string Code);

/// <summary>The outcome of <see cref="IGrantSignInService.OnSignInAsync"/>.</summary>
/// <param name="IsBootstrapAdmin">The identity is listed in <see cref="GrantSignInOptions.BootstrapAdmins"/>.</param>
/// <param name="AdminGrantWritten">This sign-in wrote the admin grant (the first sign-in of a bootstrap admin).</param>
public sealed record SignInResult(
    bool IsBootstrapAdmin,
    bool AdminGrantWritten,
    IReadOnlyList<Invitation> Redeemed,
    IReadOnlyList<SkippedInvitation> Skipped);

/// <summary>
/// Runs at every sign-in: makes a configured bootstrap identity an admin, then redeems the invitations waiting for the
/// user's verified email. Call it after the host has mapped the external identity to its local user.
/// </summary>
public interface IGrantSignInService
{
    /// <remarks>
    /// Order: bootstrap admin, then invitations found by the verified email, then the explicit token. Each redeemed
    /// invitation in <see cref="SignInResult.Redeemed"/> is the stored record after redemption.
    /// </remarks>
    /// <exception cref="GrantRefusedException">
    /// Only for an explicit <see cref="SignInContext.InvitationToken"/> that cannot be redeemed — the user followed a
    /// link and must be told why. It is thrown <b>last</b>, after the bootstrap grant and every email invitation have
    /// been written, so a dead link loses nothing but its own report. Invitations found by email are reported in
    /// <see cref="SignInResult.Skipped"/> instead.
    /// </exception>
    Task<SignInResult> OnSignInAsync(SignInContext context, CancellationToken ct = default);
}
