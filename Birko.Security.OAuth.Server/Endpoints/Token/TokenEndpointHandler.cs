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

    /// <summary>
    /// Claims an extra-claims dictionary may not set: the ones this handler writes itself, and the JWT
    /// registered claims the token provider writes.
    /// </summary>
    private static readonly HashSet<string> ReservedClaims = new(StringComparer.Ordinal)
    {
        "sub", "client_id", "iss", "scope", "aud", "exp", "iat", "nbf", "jti"
    };

    /// <summary>
    /// Issues an access + refresh token pair for a subject that authenticated outside this server (a federated
    /// sign-in such as GitHub), as if an authorization had just completed for <paramref name="clientId"/>. The
    /// refresh token then renews through the normal <c>refresh_token</c> grant, with rotation and reuse detection.
    /// </summary>
    /// <param name="clientId">A registered, enabled client allowed the <c>refresh_token</c> grant.</param>
    /// <param name="subject">The <c>sub</c> claim, any string (e.g. <c>github:42</c>).</param>
    /// <param name="scope">
    /// Space-separated scope, narrowed to the client's allowed scopes and the server's supported scopes as for
    /// <c>client_credentials</c>; null or empty grants all of them.
    /// </param>
    /// <param name="claims">
    /// Extra access-token claims (e.g. <c>name</c>, <c>roles</c>), stored on the refresh record and re-issued on every
    /// refresh. A registered claim (<c>sub</c>, <c>client_id</c>, <c>iss</c>, <c>scope</c>, <c>aud</c>, <c>exp</c>,
    /// <c>iat</c>, <c>nbf</c>, <c>jti</c>) is refused rather than silently overridden: permissions belong in
    /// <paramref name="scope"/>.
    /// </param>
    /// <exception cref="OAuthServerException">
    /// <c>invalid_client</c> for an unknown or disabled client; <c>unauthorized_client</c> when the client may not use
    /// the <c>refresh_token</c> grant (the refresh token would be unusable); <c>invalid_scope</c> when none of the
    /// requested scopes is allowed.
    /// </exception>
    /// <remarks>
    /// The caller is trusted: no client secret is checked, because the server's own host is the one asserting the
    /// sign-in. Never expose this as an endpoint.
    /// </remarks>
    public async Task<TokenResponse> IssueForSubjectAsync(
        string clientId,
        string subject,
        string? scope = null,
        IReadOnlyDictionary<string, string>? claims = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(clientId)) throw new ArgumentException("A client id is required.", nameof(clientId));
        if (string.IsNullOrEmpty(subject)) throw new ArgumentException("A subject is required.", nameof(subject));
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        if (claims != null)
        {
            foreach (var (name, value) in claims)
            {
                if (ReservedClaims.Contains(name))
                    throw new ArgumentException($"The claim '{name}' is set by the server and cannot be supplied.", nameof(claims));
                extra[name] = value ?? throw new ArgumentException($"The claim '{name}' has no value.", nameof(claims));
            }
        }

        var client = await _clients.GetByClientIdAsync(clientId, ct).ConfigureAwait(false);
        if (client == null || !client.IsEnabled)
            throw new OAuthServerException(OAuthErrorCodes.InvalidClient, "Unknown or disabled client.");
        if (!client.AllowedGrantTypes.Contains(OAuthGrantTypes.RefreshToken))
            throw new OAuthServerException(OAuthErrorCodes.UnauthorizedClient,
                $"Client is not authorized to use grant type '{OAuthGrantTypes.RefreshToken}', so its refresh token could not be used.");

        var granted = ScopeUtil.NarrowScope(scope, client.AllowedScopes, _settings.SupportedScopes);
        return await IssueTokenPairAsync(client.ClientId, subject, granted, extra, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Shapes the access token of the <c>client_credentials</c> grant: its subject and extra claims (TASK-520). Called
    /// after the client is authenticated and the scope granted. Null keeps the default (<c>sub</c> = the client id).
    /// </summary>
    /// <remarks>
    /// This exists so a consumer need not replace the grant to name a service account: DraCode re-implemented client
    /// authentication, secret hashing and scope narrowing (<c>ServiceAccountTokenIssuer</c>) only to set
    /// <c>sub = "service:&lt;name&gt;"</c>. Set it at startup, before the server takes requests.
    /// A registered claim in <see cref="ClientCredentialsTokenContext.Claims"/>, or an empty subject, is a configuration
    /// error and throws <see cref="InvalidOperationException"/> rather than being dropped.
    /// </remarks>
    public Func<ClientCredentialsTokenContext, CancellationToken, Task>? ShapeClientCredentialsToken { get; set; }

    private async Task<TokenResponse> HandleClientCredentialsAsync(OAuthClient client, TokenRequest request, CancellationToken ct)
    {
        var scope = ScopeUtil.NarrowScope(request.Scope, client.AllowedScopes, _settings.SupportedScopes);
        var shaped = new ClientCredentialsTokenContext(client, scope);
        if (ShapeClientCredentialsToken is { } shape)
        {
            await shape(shaped, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(shaped.Subject))
                throw new InvalidOperationException("ShapeClientCredentialsToken left the subject empty.");
            foreach (var name in shaped.Claims.Keys)
            {
                if (ReservedClaims.Contains(name))
                    throw new InvalidOperationException($"ShapeClientCredentialsToken set the claim '{name}', which the server sets.");
            }
        }
        // RFC 6749 §4.4.3 — refresh tokens SHOULD NOT be issued.
        return IssueAccessToken(client.ClientId, subject: shaped.Subject, scope: scope, extra: new Dictionary<string, string>(shaped.Claims));
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

        // CR-L351 / TASK-518: the claim is the store's, so it can be atomic. The read-check-write that used to be here
        // let two concurrent redemptions of one code both be issued tokens; the loser now gets invalid_grant.
        if (!await _codes.TryRedeemAsync(code, ct).ConfigureAwait(false))
            throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "Authorization code is invalid or expired.");

        return await IssueTokenPairAsync(client.ClientId, code.UserId, code.Scope, null, ct).ConfigureAwait(false);
    }

    private async Task<TokenResponse> HandleRefreshTokenAsync(OAuthClient client, TokenRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
            throw new OAuthServerException(OAuthErrorCodes.InvalidRequest, "refresh_token is required.");

        var hash = ClientSecretHasher.Hash(request.RefreshToken!);
        var record = await _refreshes.GetByHashAsync(hash, ct).ConfigureAwait(false);
        if (record == null || record.ExpiresAt <= _clock.UtcNow || record.ClientId != client.ClientId)
            throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "Refresh token is invalid or expired.");

        // CR-L350: RFC 6819 §5.2.2.3 / OAuth 2.1 reuse detection — a request presenting an already-revoked
        // (rotated) refresh token that legitimately belongs to this client signals the token may be
        // compromised. Revoke the entire (client, user) family so any concurrently-minted sibling tokens are
        // invalidated too, then reject. Kept behind the client-ownership check above so a token belonging to a
        // different client falls through to the generic invalid_grant without touching this client's family.
        if (record.Revoked)
        {
            await _refreshes.RevokeFamilyAsync(record.ClientId, record.UserId, ct).ConfigureAwait(false);
            throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "Refresh token is invalid or expired.");
        }

        var scope = ScopeUtil.NarrowScope(request.Scope ?? record.Scope, record.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries), _settings.SupportedScopes);

        if (_settings.RotateRefreshTokens)
        {
            // TASK-518: rotation claims the presented token, so two concurrent refreshes of it cannot both issue a pair.
            // The loser only fails (decided by the user 2026-10-18): it read the token while it was still valid, so this
            // is two holders racing (two tabs refreshing together), not a replay, and revoking the family would sign the
            // winner out too. A request that reads the token after the rotation committed takes the reuse path above and
            // does revoke the family: from the server's side it is indistinguishable from a replay.
            if (!await _refreshes.TryRevokeAsync(record, ct).ConfigureAwait(false))
            {
                throw new OAuthServerException(OAuthErrorCodes.InvalidGrant, "Refresh token is invalid or expired.");
            }
            return await IssueTokenPairAsync(client.ClientId, record.UserId, scope, record.Claims, ct).ConfigureAwait(false);
        }

        // No rotation — keep the old refresh token, issue only a new access token.
        var response = IssueAccessToken(client.ClientId, subject: record.UserId, scope: scope, extra: record.Claims);
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
            DeviceCodeStatus.Authorized => await IssueTokenPairAsync(client.ClientId, device.UserId ?? string.Empty, device.Scope, null, ct).ConfigureAwait(false),
            _ => throw new OAuthServerException(OAuthErrorCodes.ServerError, "Unknown device-code state.")
        };
    }

    private TokenResponse IssueAccessToken(string clientId, string subject, string scope, IReadOnlyDictionary<string, string>? extra = null)
    {
        // Extra claims first, so the registered ones always win (IssueForSubjectAsync also refuses them up front).
        var claims = new Dictionary<string, string>(StringComparer.Ordinal);
        if (extra != null)
        {
            foreach (var (name, value) in extra)
            {
                if (!ReservedClaims.Contains(name)) claims[name] = value;
            }
        }
        claims["sub"] = subject;
        claims["client_id"] = clientId;
        claims["iss"] = _settings.GetIssuer();
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

    private async Task<TokenResponse> IssueTokenPairAsync(string clientId, string userId, string scope, IReadOnlyDictionary<string, string>? extra, CancellationToken ct)
    {
        var response = IssueAccessToken(clientId, subject: userId, scope: scope, extra: extra);

        var refresh = RandomStringGenerator.Base64Url(32);
        var record = new RefreshTokenRecord
        {
            TokenHash = ClientSecretHasher.Hash(refresh),
            ClientId = clientId,
            UserId = userId,
            Scope = scope,
            ExpiresAt = _clock.UtcNow.AddSeconds(_settings.RefreshTokenLifetimeSeconds),
            Claims = extra == null ? new Dictionary<string, string>() : new Dictionary<string, string>(extra)
        };
        await _refreshes.CreateAsync(record, ct: ct).ConfigureAwait(false);

        response.RefreshToken = refresh;
        return response;
    }
}
