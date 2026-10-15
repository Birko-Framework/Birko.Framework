using System.Collections.Generic;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Options for configuring Birko JWT authentication.
/// </summary>
public class JwtAuthenticationOptions
{
    /// <summary>HMAC-SHA256 secret key (required, minimum 32 characters recommended).</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>JWT issuer. Default: "Birko".</summary>
    public string Issuer { get; set; } = "Birko";

    /// <summary>JWT audience. Defaults to Issuer if not set.</summary>
    public string? Audience { get; set; }

    /// <summary>Access token expiration in minutes. Default: 60.</summary>
    public int ExpirationMinutes { get; set; } = 60;

    /// <summary>Refresh token expiration in days. Default: 7.</summary>
    public int RefreshExpirationDays { get; set; } = 7;

    /// <summary>Clock skew tolerance for token validation. Default: 1 minute.</summary>
    public int ClockSkewSeconds { get; set; } = 60;

    /// <summary>
    /// Paths (prefixes, matched by segment) on which a <c>?token=</c> query parameter is accepted as the bearer token,
    /// beside the two that need it everywhere: a WebSocket upgrade and an <c>Accept: text/event-stream</c> request
    /// (neither a browser WebSocket nor an EventSource can set a header). Empty by default. A query string is written
    /// to access logs, browser history and Referer headers, so it is not accepted on any other request (SH-M256).
    /// </summary>
    public List<string> QueryTokenPathPrefixes { get; set; } = new();

    /// <summary>Claim name mappings. Override to use custom claim types.</summary>
    public ClaimMappingOptions Claims { get; set; } = new();

    internal string EffectiveAudience => Audience ?? Issuer;
}
