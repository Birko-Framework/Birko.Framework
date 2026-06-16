using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.OAuth.Server.Internal;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Stores;
using Birko.Time;

namespace Birko.Security.OAuth.Server.Endpoints.Authorize;

/// <summary>
/// Handles the OAuth2 /authorize endpoint (RFC 6749 §4.1.1). Only the
/// <c>response_type=code</c> flow is supported — implicit/token flow is intentionally
/// omitted (deprecated by OAuth 2.1).
/// </summary>
public class AuthorizationEndpointHandler
{
    private readonly OAuthServerSettings _settings;
    private readonly IOAuthClientStore _clients;
    private readonly IAuthorizationCodeStore _codes;
    private readonly IConsentStore _consents;
    private readonly IDateTimeProvider _clock;

    public AuthorizationEndpointHandler(
        OAuthServerSettings settings,
        IOAuthClientStore clients,
        IAuthorizationCodeStore codes,
        IConsentStore consents,
        IDateTimeProvider? clock = null)
    {
        _settings = settings;
        _clients = clients;
        _codes = codes;
        _consents = consents;
        _clock = clock ?? new SystemDateTimeProvider();
    }

    /// <summary>
    /// First step of /authorize — validates the request, then either reports that consent
    /// is needed (host renders the consent UI) or returns a freshly minted authorization code
    /// for the host to send back via 302 redirect.
    /// </summary>
    public async Task<AuthorizeResponse> HandleAuthorizeAsync(AuthorizeRequest request, string userId, CancellationToken ct = default)
    {
        var (client, scope) = await ValidateAsync(request, ct).ConfigureAwait(false);

        var prior = await _consents.GetAsync(userId, client.ClientId, ct).ConfigureAwait(false);
        if (prior != null && CoversAllScopes(prior.Scope, scope))
        {
            var code = await IssueCodeAsync(request, client, userId, scope, ct).ConfigureAwait(false);
            return new AuthorizeResponse
            {
                Code = code,
                RedirectUri = request.RedirectUri,
                State = request.State,
                RequestedScope = scope
            };
        }

        return new AuthorizeResponse
        {
            RequiresConsent = true,
            RequestedScope = scope,
            RedirectUri = request.RedirectUri,
            State = request.State
        };
    }

    /// <summary>
    /// Second step — called after the user clicks "Allow" on the consent UI. Records consent
    /// and issues the authorization code. Pass <paramref name="approved"/>=false to record the
    /// denial; the host should redirect with <c>error=access_denied</c> in that case.
    /// </summary>
    public async Task<AuthorizeResponse> HandleConsentAsync(AuthorizeRequest request, string userId, bool approved, CancellationToken ct = default)
    {
        var (client, scope) = await ValidateAsync(request, ct).ConfigureAwait(false);

        if (!approved)
        {
            throw new OAuthServerException(OAuthErrorCodes.AccessDenied, "User denied the request.");
        }

        var existing = await _consents.GetAsync(userId, client.ClientId, ct).ConfigureAwait(false);
        if (existing == null)
        {
            await _consents.CreateAsync(new ConsentRecord
            {
                UserId = userId,
                ClientId = client.ClientId,
                Scope = scope,
                GrantedAt = _clock.UtcNow
            }, ct: ct).ConfigureAwait(false);
        }
        else if (!CoversAllScopes(existing.Scope, scope))
        {
            existing.Scope = MergeScopes(existing.Scope, scope);
            existing.GrantedAt = _clock.UtcNow;
            await _consents.UpdateAsync(existing, ct: ct).ConfigureAwait(false);
        }

        var code = await IssueCodeAsync(request, client, userId, scope, ct).ConfigureAwait(false);
        return new AuthorizeResponse
        {
            Code = code,
            RedirectUri = request.RedirectUri,
            State = request.State,
            RequestedScope = scope
        };
    }

    private async Task<(OAuthClient client, string scope)> ValidateAsync(AuthorizeRequest request, CancellationToken ct)
    {
        if (!string.Equals(request.ResponseType, OAuthResponseTypes.Code, StringComparison.Ordinal))
        {
            throw new OAuthServerException(OAuthErrorCodes.UnsupportedResponseType, "Only response_type=code is supported.");
        }
        if (string.IsNullOrEmpty(request.ClientId))
            throw new OAuthServerException(OAuthErrorCodes.InvalidRequest, "client_id is required.");
        if (string.IsNullOrEmpty(request.RedirectUri))
            throw new OAuthServerException(OAuthErrorCodes.InvalidRequest, "redirect_uri is required.");

        var client = await _clients.GetByClientIdAsync(request.ClientId, ct).ConfigureAwait(false);
        if (client == null || !client.IsEnabled)
            throw new OAuthServerException(OAuthErrorCodes.UnauthorizedClient, "Unknown or disabled client.");

        if (!client.RedirectUris.Contains(request.RedirectUri))
            throw new OAuthServerException(OAuthErrorCodes.InvalidRequest, "redirect_uri does not match a registered URI.");

        if (!client.AllowedGrantTypes.Contains(OAuthGrantTypes.AuthorizationCode))
            throw new OAuthServerException(OAuthErrorCodes.UnauthorizedClient, "Client is not authorized for authorization_code grant.");

        if (client.ClientType == OAuthClientType.Public && _settings.RequirePkceForPublicClients
            && string.IsNullOrEmpty(request.CodeChallenge))
        {
            throw new OAuthServerException(OAuthErrorCodes.InvalidRequest, "PKCE code_challenge is required for public clients.");
        }

        var scope = NarrowScope(request.Scope, client.AllowedScopes);
        return (client, scope);
    }

    private async Task<string> IssueCodeAsync(AuthorizeRequest request, OAuthClient client, string userId, string scope, CancellationToken ct)
    {
        var code = RandomStringGenerator.Base64Url(32);
        await _codes.CreateAsync(new AuthorizationCode
        {
            Code = code,
            ClientId = client.ClientId,
            UserId = userId,
            RedirectUri = request.RedirectUri,
            Scope = scope,
            CodeChallenge = request.CodeChallenge,
            CodeChallengeMethod = string.IsNullOrEmpty(request.CodeChallenge) ? null : (request.CodeChallengeMethod ?? PkceValidator.MethodS256),
            ExpiresAt = _clock.UtcNow.AddSeconds(_settings.AuthorizationCodeLifetimeSeconds)
        }, ct: ct).ConfigureAwait(false);
        return code;
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

    private static bool CoversAllScopes(string granted, string requested)
    {
        var grantedSet = granted.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return requested.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(grantedSet.Contains);
    }

    private static string MergeScopes(string existing, string toAdd)
    {
        var set = existing.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        foreach (var s in toAdd.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            set.Add(s);
        }
        return string.Join(' ', set);
    }
}
