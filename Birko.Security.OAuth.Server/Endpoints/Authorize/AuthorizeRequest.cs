namespace Birko.Security.OAuth.Server.Endpoints.Authorize;

/// <summary>
/// Parsed query string of an OAuth2 /authorize request (RFC 6749 §4.1.1).
/// </summary>
public class AuthorizeRequest
{
    /// <summary>Must be <c>code</c> — implicit/token flow is intentionally not supported.</summary>
    public string ResponseType { get; set; } = OAuthResponseTypes.Code;

    public string ClientId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string? Scope { get; set; }

    /// <summary>Opaque value preserved across the redirect to defend against CSRF.</summary>
    public string? State { get; set; }

    public string? CodeChallenge { get; set; }

    /// <summary>S256 (recommended) or plain.</summary>
    public string? CodeChallengeMethod { get; set; }
}
