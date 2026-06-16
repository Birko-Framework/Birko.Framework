using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.OAuth.Server.Internal;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Stores;
using Birko.Time;

namespace Birko.Security.OAuth.Server.Endpoints.Token;

/// <summary>
/// Handles the OAuth2 /token endpoint for all four supported grant types:
/// <c>client_credentials</c>, <c>authorization_code</c> (+ PKCE), <c>refresh_token</c>, and the
/// RFC 8628 device-code grant.
/// <para>
/// Pure handler — no ASP.NET dependency. The host is responsible for parsing the form-encoded
/// body into <see cref="TokenRequest"/>, accepting HTTP Basic auth as an alternative to the
/// <c>client_id</c> / <c>client_secret</c> body fields, and serializing the result as JSON.
/// </para>
/// </summary>
public class TokenEndpointHandler
{
    private readonly OAuthServerSettings _settings;
    private readonly ITokenProvider _tokens;
    private readonly TokenOptions _tokenOptions;
    private readonly IOAuthClientStore _clients;
    private readonly IAuthorizationCodeStore _codes;
    private readonly IRefreshTokenStore _refreshes;
    private readonly IDeviceCodeStore _devices;
    private readonly IDateTimeProvider _clock;

    public TokenEndpointHandler(
        OAuthServerSettings settings,
        ITokenProvider tokens,
        TokenOptions tokenOptions,
        IOAuthClientStore clients,
        IAuthorizationCodeStore codes,
        IRefreshTokenStore refreshes,
        IDeviceCodeStore devices,
        IDateTimeProvider? clock = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _tokenOptions = tokenOptions ?? throw new ArgumentNullException(nameof(tokenOptions));
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _codes = codes ?? throw new ArgumentNullException(nameof(codes));
        _refreshes = refreshes ?? throw new ArgumentNullException(nameof(refreshes));
        _devices = devices ?? throw new ArgumentNullException(nameof(devices));
        _clock = clock ?? new SystemDateTimeProvider();
    }

    /// <summary>
    /// Processes a /token request. Throws <see cref="OAuthServerException"/> on any
    /// well-formed protocol error; the host should map that to an HTTP 400 with the
    /// JSON body produced by <see cref="TokenErrorResponse.From"/>.
    /// </summary>
    public async Task<TokenResponse> HandleAsync(TokenRequest request, CancellationToken ct = default)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrEmpty(request.GrantType))
            throw new OAuthServerException(OAuthErrorCodes.InvalidRequest, "grant_type is required.");

        // Reject grant types we don't support before doing any client lookup, so the
        // caller sees `unsupported_grant_type` instead of being told the client is
        // unauthorized for a grant nobody supports.
        if (request.GrantType is not (OAuthGrantTypes.ClientCredentials
            or OAuthGrantTypes.AuthorizationCode
            or OAuthGrantTypes.RefreshToken
            or OAuthGrantTypes.DeviceCode))
        {
            throw new OAuthServerException(OAuthErrorCodes.UnsupportedGrantType, $"Grant type '{request.GrantType}' is not supported.");
        }

        var client = await AuthenticateClientAsync(request, ct).ConfigureAwait(false);

        return request.GrantType switch
        {
            OAuthGrantTypes.ClientCredentials => await HandleClientCredentialsAsync(client, request, ct).ConfigureAwait(false),
            OAuthGrantTypes.AuthorizationCode => await HandleAuthorizationCodeAsync(client, request, ct).ConfigureAwait(false),
            OAuthGrantTypes.RefreshToken => await HandleRefreshTokenAsync(client, request, ct).ConfigureAwait(false),
            OAuthGrantTypes.DeviceCode => await HandleDeviceCodeAsync(client, request, ct).ConfigureAwait(false),
            _ => throw new OAuthServerException(OAuthErrorCodes.UnsupportedGrantType, $"Grant type '{request.GrantType}' is not supported.")
        };
    }

    private async Task<OAuthClient> AuthenticateClientAsync(TokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.ClientId))
            throw new OAuthServerException(OAuthErrorCodes.InvalidClient, "client_id is required.");

        var client = await _clients.GetByClientIdAsync(request.ClientId, ct).ConfigureAwait(false);
        if (client == null || !client.IsEnabled)
            throw new OAuthServerException(OAuthErrorCodes.InvalidClient, "Unknown or disabled client.");

        if (client.ClientType == OAuthClientType.Confidential)
        {
            if (string.IsNullOrEmpty(request.ClientSecret) ||
                string.IsNullOrEmpty(client.ClientSecretHash) ||
                !ClientSecretHasher.Verify(request.ClientSecret, client.ClientSecretHash))
            {
                throw new OAuthServerException(OAuthErrorCodes.InvalidClient, "Invalid client credentials.");
            }
        }

        if (!client.AllowedGrantTypes.Contains(request.GrantType))
        {
            throw new OAuthServerException(OAuthErrorCodes.UnauthorizedClient,
                $"Client is not authorized to use grant type '{request.GrantType}'.");
        }

        return client;
    }

    private Task<TokenResponse> HandleClientCredentialsAsync(OAuthClient client, TokenRequest request, CancellationToken ct)
    {
        var scope = NarrowScope(request.Scope, client.AllowedScopes);
        // RFC 6749 §4.4.3 — refresh tokens SHOULD NOT be issued.
        var response = IssueAccessToken(client.ClientId, subject: client.ClientId, scope: scope);
        return Task.FromResult(response);
    }

    private async Task<TokenResponse> HandleAuthorizationCodeAsync(OAuthClient client, TokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.Code))
            throw new OAuthServerException(OAuthErrorCodes.InvalidRequest, "code is required.");

        var code = await _codes.GetByCodeAsync(request.Code, ct).ConfigureAwait(false);
        if (code == null || code.Used || code.ExpiresAt <= _clock.UtcNow || code.ClientId != client.ClientId)
            throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "Authorization code is invalid or expired.");

        if (!string.Equals(code.RedirectUri, request.RedirectUri, StringComparison.Ordinal))
            throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "redirect_uri does not match.");

        if (!string.IsNullOrEmpty(code.CodeChallenge))
        {
            if (string.IsNullOrEmpty(request.CodeVerifier))
                throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "code_verifier is required.");
            if (!PkceValidator.Verify(request.CodeVerifier!, code.CodeChallenge!, code.CodeChallengeMethod ?? PkceValidator.MethodS256))
                throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "PKCE verification failed.");
        }
        else if (client.ClientType == OAuthClientType.Public && _settings.RequirePkceForPublicClients)
        {
            throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "PKCE is required for public clients.");
        }

        code.Used = true;
        await _codes.UpdateAsync(code, ct: ct).ConfigureAwait(false);

        return await IssueTokenPairAsync(client.ClientId, code.UserId, code.Scope, ct).ConfigureAwait(false);
    }

    private async Task<TokenResponse> HandleRefreshTokenAsync(OAuthClient client, TokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
            throw new OAuthServerException(OAuthErrorCodes.InvalidRequest, "refresh_token is required.");

        var hash = ClientSecretHasher.Hash(request.RefreshToken!);
        var record = await _refreshes.GetByHashAsync(hash, ct).ConfigureAwait(false);
        if (record == null || record.Revoked || record.ExpiresAt <= _clock.UtcNow || record.ClientId != client.ClientId)
            throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "Refresh token is invalid or expired.");

        var scope = NarrowScope(request.Scope ?? record.Scope, record.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (_settings.RotateRefreshTokens)
        {
            record.Revoked = true;
            await _refreshes.UpdateAsync(record, ct: ct).ConfigureAwait(false);
            return await IssueTokenPairAsync(client.ClientId, record.UserId, scope, ct).ConfigureAwait(false);
        }

        // No rotation — keep the old refresh token, issue only a new access token.
        var response = IssueAccessToken(client.ClientId, subject: record.UserId, scope: scope);
        response.RefreshToken = request.RefreshToken;
        return response;
    }

    private async Task<TokenResponse> HandleDeviceCodeAsync(OAuthClient client, TokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.DeviceCode))
            throw new OAuthServerException(OAuthErrorCodes.InvalidRequest, "device_code is required.");

        var device = await _devices.GetByDeviceCodeAsync(request.DeviceCode!, ct).ConfigureAwait(false);
        if (device == null || device.ClientId != client.ClientId)
            throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "Unknown device_code.");

        if (device.ExpiresAt <= _clock.UtcNow)
            throw new OAuthServerException(OAuthErrorCodes.ExpiredToken, "Device code has expired.");

        // RFC 8628 §3.5 — rate-limit polling and bump interval on slow_down.
        if (device.LastPolledAt is { } last)
        {
            var elapsed = (_clock.UtcNow - last).TotalSeconds;
            if (elapsed < _settings.DeviceCodePollingIntervalSeconds)
            {
                throw new OAuthServerException(OAuthErrorCodes.SlowDown,
                    $"Polling too fast — wait at least {_settings.DeviceCodePollingIntervalSeconds} seconds between polls.");
            }
        }

        device.LastPolledAt = _clock.UtcNow;
        await _devices.UpdateAsync(device, ct: ct).ConfigureAwait(false);

        return device.Status switch
        {
            DeviceCodeStatus.Pending => throw new OAuthServerException(OAuthErrorCodes.AuthorizationPending, "User has not yet completed authorization."),
            DeviceCodeStatus.Denied => throw new OAuthServerException(OAuthErrorCodes.AccessDenied, "User denied the request."),
            DeviceCodeStatus.Authorized => await IssueTokenPairAsync(client.ClientId, device.UserId ?? string.Empty, device.Scope, ct).ConfigureAwait(false),
            _ => throw new OAuthServerException(OAuthErrorCodes.ServerError, "Unknown device-code state.")
        };
    }

    private TokenResponse IssueAccessToken(string clientId, string subject, string scope)
    {
        var claims = new Dictionary<string, string>
        {
            ["sub"] = subject,
            ["client_id"] = clientId,
            ["iss"] = _settings.GetIssuer()
        };
        if (!string.IsNullOrEmpty(scope)) claims["scope"] = scope;

        var opts = new TokenOptions
        {
            Secret = _tokenOptions.Secret,
            Issuer = _settings.GetIssuer(),
            Audience = _tokenOptions.Audience,
            ExpirationMinutes = Math.Max(1, _settings.AccessTokenLifetimeSeconds / 60),
            RefreshExpirationDays = _tokenOptions.RefreshExpirationDays
        };

        var result = _tokens.GenerateToken(claims, opts);
        return new TokenResponse
        {
            AccessToken = result.Token,
            TokenType = "Bearer",
            ExpiresIn = _settings.AccessTokenLifetimeSeconds,
            Scope = string.IsNullOrEmpty(scope) ? null : scope
        };
    }

    private async Task<TokenResponse> IssueTokenPairAsync(string clientId, string userId, string scope, CancellationToken ct)
    {
        var response = IssueAccessToken(clientId, subject: userId, scope: scope);

        var refresh = RandomStringGenerator.Base64Url(32);
        var record = new RefreshTokenRecord
        {
            TokenHash = ClientSecretHasher.Hash(refresh),
            ClientId = clientId,
            UserId = userId,
            Scope = scope,
            ExpiresAt = _clock.UtcNow.AddSeconds(_settings.RefreshTokenLifetimeSeconds)
        };
        await _refreshes.CreateAsync(record, ct: ct).ConfigureAwait(false);

        response.RefreshToken = refresh;
        return response;
    }

    private static string NarrowScope(string? requested, IEnumerable<string> allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(requested))
        {
            return string.Join(' ', allowedSet);
        }
        var requestedScopes = requested!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var granted = requestedScopes.Where(allowedSet.Contains).ToArray();
        if (granted.Length == 0 && allowedSet.Count > 0)
        {
            throw new OAuthServerException(OAuthErrorCodes.InvalidScope, "None of the requested scopes are allowed for this client.");
        }
        return string.Join(' ', granted);
    }
}
