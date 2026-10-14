namespace Birko.Security.Grants;

/// <summary>Codes carried by <see cref="GrantRefusedException.Code"/>.</summary>
public static class GrantErrorCodes
{
    /// <summary>The caller is neither an admin nor allowed to share the resource.</summary>
    public const string NotAuthorized = "Grant.NotAuthorized";

    /// <summary>The role being granted, or the target's role being changed, exceeds the caller's own actions.</summary>
    public const string Escalation = "Grant.Escalation";

    /// <summary>The operation would leave a resource with no owner.</summary>
    public const string LastOwner = "Grant.LastOwner";

    /// <summary>The operation would remove the last admin grant in its tenant scope.</summary>
    public const string LastAdmin = "Grant.LastAdmin";

    /// <summary>No invitation matches the token presented.</summary>
    public const string InvitationNotFound = "Invitation.NotFound";

    /// <summary>The invitation's lifetime has passed.</summary>
    public const string InvitationExpired = "Invitation.Expired";

    /// <summary>The invitation was already redeemed.</summary>
    public const string InvitationUsed = "Invitation.Used";

    /// <summary>The invitation was revoked.</summary>
    public const string InvitationRevoked = "Invitation.Revoked";

    /// <summary>The identity provider did not mark the signing-in email as verified.</summary>
    public const string EmailNotVerified = "Invitation.EmailNotVerified";

    /// <summary>The invitation was issued to a different email than the one signing in.</summary>
    public const string EmailMismatch = "Invitation.EmailMismatch";

    /// <summary>The invitation names a role or resource type the role table no longer declares.</summary>
    public const string InvitationInvalid = "Invitation.Invalid";

    /// <summary>The user already holds a role on the resource that the invited role would not contain; replacing it would take actions away.</summary>
    public const string RoleConflict = "Grant.RoleConflict";
}
