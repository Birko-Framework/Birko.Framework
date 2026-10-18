using System;
using System.Net.Http;
using Birko.Communication.OAuth;

namespace Birko.Communication.OAuth.Providers;

/// <summary>
/// Pre-configured OAuth settings for GitHub: the device flow (GitHub Copilot and other CLI access) and the web
/// (authorization-code) flow for signing users in (TASK-521).
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
    /// GitHub's authorization endpoint for the web flow.
    /// </summary>
    public const string AuthorizationEndpoint = "https://github.com/login/oauth/authorize";

    /// <summary>
    /// Creates OAuthSettings for GitHub's web (authorization-code) sign-in. Build the redirect with
    /// <see cref="OAuthClient.BuildAuthorizationUrl"/>, exchange the callback's code with <see cref="OAuthCodeExchanger"/>,
    /// and read the account with <see cref="GitHubUserClient"/>.
    /// </summary>
    /// <param name="clientId">GitHub OAuth App client ID.</param>
    /// <param name="clientSecret">GitHub OAuth App client secret.</param>
    /// <param name="redirectUri">The callback URL registered on the OAuth App.</param>
    /// <param name="scope">Space-separated scopes. Default <c>read:user user:email</c>: the profile, and the
    /// verified primary address through <see cref="IGitHubUserClient.GetPrimaryEmailAsync"/>.</param>
    public static OAuthSettings CreateWebFlowSettings(string clientId, string clientSecret, string redirectUri, string scope = "read:user user:email")
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new ArgumentException("A client id is required.", nameof(clientId));
        if (string.IsNullOrWhiteSpace(clientSecret)) throw new ArgumentException("A client secret is required.", nameof(clientSecret));
        if (string.IsNullOrWhiteSpace(redirectUri)) throw new ArgumentException("A redirect URI is required.", nameof(redirectUri));

        return new OAuthSettings
        {
            GrantType = OAuthGrantType.AuthorizationCode,
            ClientId = clientId,
            ClientSecret = clientSecret,
            AuthorizationEndpoint = AuthorizationEndpoint,
            TokenEndpoint = TokenEndpoint,
            RedirectUri = redirectUri,
            Scope = scope
        };
    }

    /// <summary>
    /// Creates an IOAuthClient configured for GitHub Device Code flow.
    /// </summary>
    /// <param name="clientId">GitHub OAuth App client ID.</param>
    /// <param name="scope">Space-separated scopes (default: "read:user").</param>
    /// <param name="httpClient">Optional HttpClient to reuse.</param>
    public static IOAuthClient CreateDeviceFlowClient(string clientId, string scope = "read:user", HttpClient? httpClient = null)
    {
        // Delegate to CreateDeviceFlowSettings so the device-flow settings have a single source of
        // truth and the two factory methods can't drift apart (CR-L076).
        return new OAuthClient(CreateDeviceFlowSettings(clientId, scope), httpClient);
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
