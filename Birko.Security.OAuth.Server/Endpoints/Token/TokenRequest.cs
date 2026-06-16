namespace Birko.Security.OAuth.Server.Endpoints.Token;

/// <summary>
/// Parsed form-encoded body of an OAuth2 /token request (RFC 6749 §4).
/// Each grant type only populates a subset of fields.
/// </summary>
public class TokenRequest
{
    /// <summary>The <c>grant_type</c> parameter (see <see cref="OAuthGrantTypes"/>).</summary>
    public string GrantType { get; set; } = string.Empty;

    /// <summary>The <c>client_id</c> parameter (also accepted via HTTP Basic in the host layer).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The <c>client_secret</c> parameter — null for public clients.</summary>
    public string? ClientSecret { get; set; }

    // authorization_code
    public string? Code { get; set; }
    public string? RedirectUri { get; set; }
    public string? CodeVerifier { get; set; }

    // refresh_token
    public string? RefreshToken { get; set; }

    // device_code
    public string? DeviceCode { get; set; }

    // client_credentials, refresh_token (optional narrowing)
    public string? Scope { get; set; }
}
