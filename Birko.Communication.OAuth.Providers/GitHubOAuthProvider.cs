using Birko.Communication.OAuth;

namespace Birko.Communication.OAuth.Providers;

/// <summary>
/// Pre-configured OAuth client factory for GitHub Device Code flow.
/// Used for GitHub Copilot and other GitHub API access.
/// </summary>
public static class GitHubOAuthProvider
{
    /// <summary>
    /// GitHub's device authorization endpoint.
    /// </summary>
    public const string DeviceAuthorizationEndpoint = "https://github.com/login/device/code";

    /// <summary>
    /// GitHub's token endpoint.
    /// </summary>
    public const string TokenEndpoint = "https://github.com/login/oauth/access_token";

    /// <summary>
    /// Creates an IOAuthClient configured for GitHub Device Code flow.
    /// </summary>
    /// <param name="clientId">GitHub OAuth App client ID.</param>
    /// <param name="scope">Space-separated scopes (default: "read:user").</param>
    /// <param name="httpClient">Optional HttpClient to reuse.</param>
    public static IOAuthClient CreateDeviceFlowClient(string clientId, string scope = "read:user", HttpClient? httpClient = null)
    {
        var settings = new OAuthSettings
        {
            GrantType = OAuthGrantType.DeviceCode,
            ClientId = clientId,
            TokenEndpoint = TokenEndpoint,
            DeviceAuthorizationEndpoint = DeviceAuthorizationEndpoint,
            Scope = scope,
            DeviceCodePollingIntervalSeconds = 5,
            DeviceCodeTimeoutSeconds = 600
        };

        return new OAuthClient(settings, httpClient);
    }

    /// <summary>
    /// Creates OAuthSettings for GitHub Device Code flow (for manual IOAuthClient construction).
    /// </summary>
    public static OAuthSettings CreateDeviceFlowSettings(string clientId, string scope = "read:user")
    {
        return new OAuthSettings
        {
            GrantType = OAuthGrantType.DeviceCode,
            ClientId = clientId,
            TokenEndpoint = TokenEndpoint,
            DeviceAuthorizationEndpoint = DeviceAuthorizationEndpoint,
            Scope = scope,
            DeviceCodePollingIntervalSeconds = 5,
            DeviceCodeTimeoutSeconds = 600
        };
    }
}
