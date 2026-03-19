using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.OAuth;

/// <summary>
/// OAuth2 client supporting Client Credentials, Authorization Code, Authorization Code + PKCE,
/// Device Code, and Refresh Token flows with automatic token caching and refresh.
/// </summary>
public class OAuthClient : IOAuthClient
{
    private readonly OAuthSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private OAuthToken? _cachedToken;

    /// <summary>
    /// Creates a new OAuth2 client with the specified settings.
    /// </summary>
    public OAuthClient(OAuthSettings settings) : this(settings, null)
    {
    }

    /// <summary>
    /// Creates a new OAuth2 client with the specified settings and optional HttpClient.
    /// </summary>
    public OAuthClient(OAuthSettings settings, HttpClient? httpClient)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        if (string.IsNullOrWhiteSpace(settings.TokenEndpoint))
            throw new ArgumentException("TokenEndpoint (Location) is required", nameof(settings));
        if (string.IsNullOrWhiteSpace(settings.ClientId))
            throw new ArgumentException("ClientId (UserName) is required", nameof(settings));

        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
    }

    /// <inheritdoc />
    public async Task<OAuthToken> GetTokenAsync(CancellationToken ct = default)
    {
        if (_cachedToken != null && !_cachedToken.IsExpired(_settings.TokenExpiryBufferSeconds))
            return _cachedToken;

        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken != null && !_cachedToken.IsExpired(_settings.TokenExpiryBufferSeconds))
                return _cachedToken;

            // Try refresh token first if available
            if (_cachedToken?.RefreshToken != null)
            {
                try
                {
                    _cachedToken = await RequestTokenAsync(BuildRefreshTokenParameters(_cachedToken.RefreshToken), ct)
                        .ConfigureAwait(false);
                    return _cachedToken;
                }
                catch (OAuthException)
                {
                    // Refresh token may be expired, fall through to normal flow
                }
            }

            return _settings.GrantType switch
            {
                OAuthGrantType.ClientCredentials => _cachedToken = await RequestTokenAsync(BuildClientCredentialsParameters(), ct).ConfigureAwait(false),
                OAuthGrantType.RefreshToken when _cachedToken?.RefreshToken != null =>
                    _cachedToken = await RequestTokenAsync(BuildRefreshTokenParameters(_cachedToken.RefreshToken), ct).ConfigureAwait(false),
                _ => throw new OAuthException(
                    $"Cannot automatically obtain a token for grant type {_settings.GrantType}. " +
                    "Use ExchangeCodeAsync, PollDeviceTokenAsync, or provide a refresh token.")
            };
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OAuthToken> RefreshTokenAsync(CancellationToken ct = default)
    {
        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cachedToken?.RefreshToken != null)
            {
                _cachedToken = await RequestTokenAsync(BuildRefreshTokenParameters(_cachedToken.RefreshToken), ct)
                    .ConfigureAwait(false);
                return _cachedToken;
            }

            if (_settings.GrantType == OAuthGrantType.ClientCredentials)
            {
                _cachedToken = await RequestTokenAsync(BuildClientCredentialsParameters(), ct).ConfigureAwait(false);
                return _cachedToken;
            }

            throw new OAuthException("No refresh token available and grant type does not support automatic token acquisition.");
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OAuthToken> ExchangeCodeAsync(string code, string? codeVerifier = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = _settings.ClientId,
            ["code"] = code
        };

        if (!string.IsNullOrEmpty(_settings.RedirectUri))
            parameters["redirect_uri"] = _settings.RedirectUri;

        // Confidential clients send client_secret
        if (!string.IsNullOrEmpty(_settings.ClientSecret) && codeVerifier == null)
            parameters["client_secret"] = _settings.ClientSecret;

        // PKCE flow sends code_verifier instead of client_secret
        if (codeVerifier != null)
            parameters["code_verifier"] = codeVerifier;

        AddCommonParameters(parameters);

        await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _cachedToken = await RequestTokenAsync(parameters, ct).ConfigureAwait(false);
            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DeviceAuthorizationResponse> RequestDeviceAuthorizationAsync(CancellationToken ct = default)
    {
        var endpoint = _settings.DeviceAuthorizationEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new OAuthException("DeviceAuthorizationEndpoint is required for Device Code flow.");

        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = _settings.ClientId
        };

        if (!string.IsNullOrEmpty(_settings.Scope))
            parameters["scope"] = _settings.Scope;

        if (!string.IsNullOrEmpty(_settings.Audience))
            parameters["audience"] = _settings.Audience;

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _httpClient.PostAsync(endpoint, content, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            ThrowFromErrorResponse(json, (int)response.StatusCode);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new DeviceAuthorizationResponse
        {
            DeviceCode = root.GetProperty("device_code").GetString() ?? string.Empty,
            UserCode = root.GetProperty("user_code").GetString() ?? string.Empty,
            VerificationUri = GetStringProperty(root, "verification_uri")
                ?? GetStringProperty(root, "verification_url") ?? string.Empty,
            VerificationUriComplete = GetStringProperty(root, "verification_uri_complete")
                ?? GetStringProperty(root, "verification_url_complete"),
            ExpiresInSeconds = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 600,
            IntervalSeconds = root.TryGetProperty("interval", out var intv) ? intv.GetInt32() : _settings.DeviceCodePollingIntervalSeconds
        };
    }

    /// <inheritdoc />
    public async Task<OAuthToken> PollDeviceTokenAsync(string deviceCode, int? intervalSeconds = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(deviceCode);

        var interval = intervalSeconds ?? _settings.DeviceCodePollingIntervalSeconds;
        var deadline = DateTime.UtcNow.AddSeconds(_settings.DeviceCodeTimeoutSeconds);

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
            ["client_id"] = _settings.ClientId,
            ["device_code"] = deviceCode
        };

        AddCommonParameters(parameters);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            await Task.Delay(TimeSpan.FromSeconds(interval), ct).ConfigureAwait(false);

            using var content = new FormUrlEncodedContent(parameters);
            using var response = await _httpClient.PostAsync(_settings.TokenEndpoint, content, ct).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    _cachedToken = ParseTokenResponse(json);
                    return _cachedToken;
                }
                finally
                {
                    _tokenLock.Release();
                }
            }

            // Parse error to determine if we should keep polling
            using var errorDoc = JsonDocument.Parse(json);
            var error = GetStringProperty(errorDoc.RootElement, "error");

            switch (error)
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += 5;
                    continue;
                default:
                    var errorDescription = GetStringProperty(errorDoc.RootElement, "error_description");
                    throw new OAuthException(
                        $"Device code token request failed: {error}",
                        error, errorDescription, (int)response.StatusCode);
            }
        }

        throw new OAuthException("Device code authorization timed out.", "expired_token", "The device code has expired.", null);
    }

    /// <inheritdoc />
    public string BuildAuthorizationUrl(string state, PkceChallenge? pkceChallenge = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var endpoint = _settings.AuthorizationEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new OAuthException("AuthorizationEndpoint is required for Authorization Code flow.");

        var parameters = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _settings.ClientId,
            ["state"] = state
        };

        if (!string.IsNullOrEmpty(_settings.RedirectUri))
            parameters["redirect_uri"] = _settings.RedirectUri;

        if (!string.IsNullOrEmpty(_settings.Scope))
            parameters["scope"] = _settings.Scope;

        if (!string.IsNullOrEmpty(_settings.Audience))
            parameters["audience"] = _settings.Audience;

        if (pkceChallenge != null)
        {
            parameters["code_challenge"] = pkceChallenge.CodeChallenge;
            parameters["code_challenge_method"] = pkceChallenge.CodeChallengeMethod;
        }

        foreach (var extra in _settings.ExtraParameters)
        {
            parameters[extra.Key] = extra.Value;
        }

        var separator = endpoint.Contains('?') ? '&' : '?';
        var queryString = new List<string>();
        foreach (var kvp in parameters)
        {
            queryString.Add($"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}");
        }

        return $"{endpoint}{separator}{string.Join('&', queryString)}";
    }

    /// <inheritdoc />
    public void ClearTokenCache()
    {
        _tokenLock.Wait();
        try
        {
            _cachedToken = null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        _tokenLock.Dispose();
    }

    #region Private Helpers

    private Dictionary<string, string> BuildClientCredentialsParameters()
    {
        if (string.IsNullOrEmpty(_settings.ClientSecret))
            throw new OAuthException("ClientSecret (Password) is required for Client Credentials flow.");

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _settings.ClientId,
            ["client_secret"] = _settings.ClientSecret
        };

        AddCommonParameters(parameters);
        return parameters;
    }

    private Dictionary<string, string> BuildRefreshTokenParameters(string refreshToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = _settings.ClientId,
            ["refresh_token"] = refreshToken
        };

        if (!string.IsNullOrEmpty(_settings.ClientSecret))
            parameters["client_secret"] = _settings.ClientSecret;

        AddCommonParameters(parameters);
        return parameters;
    }

    private void AddCommonParameters(Dictionary<string, string> parameters)
    {
        if (!string.IsNullOrEmpty(_settings.Scope) && !parameters.ContainsKey("scope"))
            parameters["scope"] = _settings.Scope;

        if (!string.IsNullOrEmpty(_settings.Audience) && !parameters.ContainsKey("audience"))
            parameters["audience"] = _settings.Audience;

        foreach (var extra in _settings.ExtraParameters)
        {
            if (!parameters.ContainsKey(extra.Key))
                parameters[extra.Key] = extra.Value;
        }
    }

    private async Task<OAuthToken> RequestTokenAsync(Dictionary<string, string> parameters, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _httpClient.PostAsync(_settings.TokenEndpoint, content, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            ThrowFromErrorResponse(json, (int)response.StatusCode);

        return ParseTokenResponse(json);
    }

    private OAuthToken ParseTokenResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new OAuthException("No access_token in response");

        var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
        var tokenType = GetStringProperty(root, "token_type") ?? "Bearer";
        var refreshToken = GetStringProperty(root, "refresh_token");
        var scope = GetStringProperty(root, "scope");
        var idToken = GetStringProperty(root, "id_token");

        return new OAuthToken
        {
            AccessToken = accessToken,
            TokenType = tokenType,
            ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn),
            RefreshToken = refreshToken,
            Scope = scope,
            IdToken = idToken
        };
    }

    private static void ThrowFromErrorResponse(string json, int statusCode)
    {
        string? error = null;
        string? errorDescription = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            error = GetStringProperty(doc.RootElement, "error");
            errorDescription = GetStringProperty(doc.RootElement, "error_description");
        }
        catch (JsonException)
        {
            // Response is not JSON — use raw body
        }

        var message = errorDescription ?? error ?? $"Token request failed with status {statusCode}";
        throw new OAuthException(message, error, errorDescription, statusCode);
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    #endregion
}
