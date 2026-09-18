namespace Birko.Security.OAuth.Server;

/// <summary>
/// Standard OAuth2 error codes from RFC 6749 §5.2, §4.1.2.1, §4.2.2.1 and RFC 8628 §3.5.
/// </summary>
public static class OAuthErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string InvalidClient = "invalid_client";
    public const string InvalidGrant = "invalid_grant";
    public const string UnauthorizedClient = "unauthorized_client";
    public const string UnsupportedGrantType = "unsupported_grant_type";
    public const string UnsupportedResponseType = "unsupported_response_type";
    public const string InvalidScope = "invalid_scope";
    public const string AccessDenied = "access_denied";
    public const string ServerError = "server_error";
    public const string TemporarilyUnavailable = "temporarily_unavailable";

    // RFC 8628 device authorization grant
    public const string AuthorizationPending = "authorization_pending";
    public const string SlowDown = "slow_down";
    public const string ExpiredToken = "expired_token";
}
