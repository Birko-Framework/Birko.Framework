namespace Birko.Security.OAuth.Server.Endpoints.Authorize;

/// <summary>
/// Outcome of an /authorize call. One of:
/// <list type="bullet">
/// <item><description><see cref="RequiresConsent"/> = true — host should render the consent UI</description></item>
/// <item><description><see cref="Code"/> set — host should 302-redirect to <see cref="RedirectUri"/> with <c>code</c> and <c>state</c></description></item>
/// </list>
/// </summary>
public class AuthorizeResponse
{
    /// <summary>True when the user has not yet consented for this (client, scopes) pair.</summary>
    public bool RequiresConsent { get; set; }

    /// <summary>The scopes the host should present to the user for approval.</summary>
    public string? RequestedScope { get; set; }

    /// <summary>Authorization code (set when consent already exists / was just given).</summary>
    public string? Code { get; set; }

    /// <summary>The validated redirect URI the host should redirect to.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Opaque <c>state</c> value to echo back.</summary>
    public string? State { get; set; }
}
