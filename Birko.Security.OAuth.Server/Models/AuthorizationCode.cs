using System;
using Birko.Data.Models;

namespace Birko.Security.OAuth.Server.Models;

/// <summary>
/// One-time authorization code issued by the /authorize endpoint and exchanged at /token.
/// Per RFC 6749 §4.1.2 the code must be short-lived and single-use.
/// </summary>
public class AuthorizationCode : AbstractModel
{
    /// <summary>The opaque code value (base64url).</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The client that requested the code.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The user who authorized the request (sub claim of issued tokens).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>The redirect_uri presented at /authorize — must match again at /token.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Space-separated scopes the user consented to.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>PKCE code_challenge (RFC 7636 §4.2). Null when PKCE was not used.</summary>
    public string? CodeChallenge { get; set; }

    /// <summary>PKCE code_challenge_method (S256 or plain).</summary>
    public string? CodeChallengeMethod { get; set; }

    /// <summary>UTC time after which the code is rejected.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Whether the code has already been redeemed.</summary>
    public bool Used { get; set; }
}
