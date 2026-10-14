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
}
