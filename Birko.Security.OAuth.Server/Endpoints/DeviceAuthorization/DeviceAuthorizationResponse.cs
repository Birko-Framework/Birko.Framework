namespace Birko.Security.OAuth.Server.Endpoints.DeviceAuthorization;

/// <summary>
/// /device_authorization response per RFC 8628 §3.2. Wire names:
/// <c>device_code</c>, <c>user_code</c>, <c>verification_uri</c>, <c>verification_uri_complete</c>,
/// <c>expires_in</c>, <c>interval</c>.
/// </summary>
public class DeviceAuthorizationResponse
{
    public string DeviceCode { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public string VerificationUri { get; set; } = string.Empty;
    public string? VerificationUriComplete { get; set; }
    public int ExpiresIn { get; set; }
    public int Interval { get; set; }
}
