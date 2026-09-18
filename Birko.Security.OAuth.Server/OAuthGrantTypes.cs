namespace Birko.Security.OAuth.Server;

/// <summary>
/// Standard OAuth2 grant_type values (RFC 6749 §4, RFC 8628).
/// </summary>
public static class OAuthGrantTypes
{
    public const string ClientCredentials = "client_credentials";
    public const string AuthorizationCode = "authorization_code";
    public const string RefreshToken = "refresh_token";
    public const string DeviceCode = "urn:ietf:params:oauth:grant-type:device_code";
}

/// <summary>
/// Standard OAuth2 response_type values for the authorization endpoint (RFC 6749 §3.1.1).
/// </summary>
public static class OAuthResponseTypes
{
    public const string Code = "code";
    public const string Token = "token";
}

/// <summary>
/// OAuth2 client types (RFC 6749 §2.1).
/// </summary>
public enum OAuthClientType
{
    /// <summary>Public client (no secret) — SPA, mobile app, CLI. PKCE required.</summary>
    Public = 0,

    /// <summary>Confidential client — holds a secret. Server-side web app, machine-to-machine.</summary>
    Confidential = 1
}
