namespace Birko.Security.OAuth.Server.Endpoints.DeviceAuthorization;

/// <summary>
/// Body of a /device_authorization request (RFC 8628 §3.1).
/// </summary>
public class DeviceAuthorizationRequest
{
    public string ClientId { get; set; } = string.Empty;
    public string? Scope { get; set; }
}
