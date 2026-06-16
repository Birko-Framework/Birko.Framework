using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.OAuth.Server.Internal;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Stores;
using Birko.Time;

namespace Birko.Security.OAuth.Server.Endpoints.ClientRegistration;

/// <summary>
/// Implements Dynamic Client Registration (RFC 7591). The host is responsible for
/// gating access to this endpoint — usually behind an admin-only auth policy.
/// </summary>
public class ClientRegistrationHandler
{
    private readonly IOAuthClientStore _clients;
    private readonly IDateTimeProvider _clock;

    public ClientRegistrationHandler(IOAuthClientStore clients, IDateTimeProvider? clock = null)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
        _clock = clock ?? new SystemDateTimeProvider();
    }

    public async Task<ClientRegistrationResponse> RegisterAsync(ClientRegistrationRequest request, CancellationToken ct = default)
    {
        if (request.RedirectUris.Count == 0 && request.AllowedGrantTypes.Contains(OAuthGrantTypes.AuthorizationCode))
        {
            throw new OAuthServerException(OAuthErrorCodes.InvalidRequest,
                "At least one redirect_uri is required when authorization_code grant is enabled.");
        }

        var clientId = RandomStringGenerator.Base64Url(16);
        string? plaintextSecret = null;
        string? hash = null;
        if (request.ClientType == OAuthClientType.Confidential)
        {
            plaintextSecret = RandomStringGenerator.Base64Url(32);
            hash = ClientSecretHasher.Hash(plaintextSecret);
        }

        var client = new OAuthClient
        {
            ClientId = clientId,
            ClientSecretHash = hash,
            ClientType = request.ClientType,
            Name = request.Name,
            RedirectUris = request.RedirectUris,
            AllowedGrantTypes = request.AllowedGrantTypes,
            AllowedScopes = request.AllowedScopes,
            CreatedAt = _clock.UtcNow,
            IsEnabled = true
        };

        await _clients.CreateAsync(client, ct: ct).ConfigureAwait(false);

        return new ClientRegistrationResponse
        {
            ClientId = clientId,
            ClientSecret = plaintextSecret,
            ClientType = client.ClientType,
            Name = client.Name,
            RedirectUris = client.RedirectUris,
            AllowedGrantTypes = client.AllowedGrantTypes,
            AllowedScopes = client.AllowedScopes,
            CreatedAt = client.CreatedAt
        };
    }

    public async Task<ClientRegistrationResponse?> GetAsync(string clientId, CancellationToken ct = default)
    {
        var client = await _clients.GetByClientIdAsync(clientId, ct).ConfigureAwait(false);
        if (client == null) return null;
        return new ClientRegistrationResponse
        {
            ClientId = client.ClientId,
            ClientSecret = null,
            ClientType = client.ClientType,
            Name = client.Name,
            RedirectUris = client.RedirectUris,
            AllowedGrantTypes = client.AllowedGrantTypes,
            AllowedScopes = client.AllowedScopes,
            CreatedAt = client.CreatedAt
        };
    }

    public async Task<ClientRegistrationResponse> UpdateAsync(string clientId, ClientRegistrationRequest request, CancellationToken ct = default)
    {
        var client = await _clients.GetByClientIdAsync(clientId, ct).ConfigureAwait(false)
            ?? throw new OAuthServerException(OAuthErrorCodes.InvalidClient, "Unknown client_id.");

        client.Name = request.Name;
        client.RedirectUris = request.RedirectUris;
        client.AllowedGrantTypes = request.AllowedGrantTypes;
        client.AllowedScopes = request.AllowedScopes;
        // ClientType is immutable post-creation — changing it would invalidate stored secret/PKCE assumptions.

        await _clients.UpdateAsync(client, ct: ct).ConfigureAwait(false);
        return new ClientRegistrationResponse
        {
            ClientId = client.ClientId,
            ClientSecret = null,
            ClientType = client.ClientType,
            Name = client.Name,
            RedirectUris = client.RedirectUris,
            AllowedGrantTypes = client.AllowedGrantTypes,
            AllowedScopes = client.AllowedScopes,
            CreatedAt = client.CreatedAt
        };
    }

    public async Task DeleteAsync(string clientId, CancellationToken ct = default)
    {
        var client = await _clients.GetByClientIdAsync(clientId, ct).ConfigureAwait(false);
        if (client == null) return;
        await _clients.DeleteAsync(client, ct).ConfigureAwait(false);
    }
}
