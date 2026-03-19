using System;
using System.Collections.Generic;
using Birko.Configuration;

namespace Birko.Communication.OAuth;

/// <summary>
/// OAuth2 grant type.
/// </summary>
public enum OAuthGrantType
{
    /// <summary>
    /// Client Credentials flow — machine-to-machine, no user interaction.
    /// </summary>
    ClientCredentials,

    /// <summary>
    /// Authorization Code flow — server-side web apps with confidential client.
    /// </summary>
    AuthorizationCode,

    /// <summary>
    /// Authorization Code with PKCE — public clients (SPAs, mobile, CLI).
    /// </summary>
    AuthorizationCodePkce,

    /// <summary>
    /// Device Code flow — input-constrained devices (CLI, IoT, smart TV).
    /// </summary>
    DeviceCode,

    /// <summary>
    /// Refresh Token — exchange a refresh token for a new access token.
    /// </summary>
    RefreshToken
}

/// <summary>
/// OAuth2 client settings extending <see cref="RemoteSettings"/> for token endpoint connectivity.
/// <para>
/// Location = token endpoint URL (e.g., "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token")
/// UserName = client_id, Password = client_secret.
/// </para>
/// </summary>
public class OAuthSettings : RemoteSettings
{
    /// <summary>
    /// The OAuth2 grant type to use.
    /// </summary>
    public OAuthGrantType GrantType { get; set; } = OAuthGrantType.ClientCredentials;

    /// <summary>
    /// The authorization endpoint URL (required for AuthorizationCode / AuthorizationCodePkce / DeviceCode flows).
    /// </summary>
    public string? AuthorizationEndpoint { get; set; }

    /// <summary>
    /// The device authorization endpoint URL (required for DeviceCode flow).
    /// </summary>
    public string? DeviceAuthorizationEndpoint { get; set; }

    /// <summary>
    /// Space-separated scopes to request.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// Redirect URI for Authorization Code flows.
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// Audience / resource identifier (used by some providers instead of scope).
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Extra parameters to include in token requests (provider-specific).
    /// </summary>
    public Dictionary<string, string> ExtraParameters { get; set; } = new();

    /// <summary>
    /// Number of seconds before actual expiry to consider a token expired (buffer).
    /// Default is 60 seconds.
    /// </summary>
    public int TokenExpiryBufferSeconds { get; set; } = 60;

    /// <summary>
    /// Timeout in seconds for HTTP requests to the token endpoint. Default is 30.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Polling interval in seconds for Device Code flow. Default is 5.
    /// </summary>
    public int DeviceCodePollingIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum time in seconds to wait for device authorization. Default is 300 (5 minutes).
    /// </summary>
    public int DeviceCodeTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Token endpoint URL — alias for <see cref="RemoteSettings.Location"/>.
    /// </summary>
    public string TokenEndpoint
    {
        get => Location ?? string.Empty;
        set => Location = value;
    }

    /// <summary>
    /// Client ID — alias for <see cref="RemoteSettings.UserName"/>.
    /// </summary>
    public string ClientId
    {
        get => UserName ?? string.Empty;
        set => UserName = value;
    }

    /// <summary>
    /// Client secret — alias for <see cref="PasswordSettings.Password"/>.
    /// </summary>
    public string ClientSecret
    {
        get => Password ?? string.Empty;
        set => Password = value;
    }
}
