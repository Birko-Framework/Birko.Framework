using System;
using System.Collections.Generic;
using Birko.Security.OAuth.Server.Models;

namespace Birko.Security.OAuth.Server.Endpoints.Token;

/// <summary>
/// What <see cref="TokenEndpointHandler.ShapeClientCredentialsToken"/> receives for a <c>client_credentials</c> token:
/// the authenticated client and the granted scope, and what it may change: the subject and extra claims.
/// </summary>
public sealed class ClientCredentialsTokenContext
{
    internal ClientCredentialsTokenContext(OAuthClient client, string scope)
    {
        Client = client;
        Scope = scope;
        Subject = client.ClientId;
    }

    /// <summary>The client, already authenticated and allowed the grant.</summary>
    public OAuthClient Client { get; }

    /// <summary>The granted scope (space-delimited), already narrowed to the client's and the server's scopes.</summary>
    public string Scope { get; }

    /// <summary>The token's <c>sub</c>. Default: the client id; e.g. <c>service:billing</c> for a service account.</summary>
    public string Subject { get; set; }

    /// <summary>
    /// Extra access-token claims (e.g. <c>name</c>). A registered claim (<c>sub</c>, <c>client_id</c>, <c>iss</c>,
    /// <c>scope</c>, <c>aud</c>, <c>exp</c>, <c>iat</c>, <c>nbf</c>, <c>jti</c>) is refused: set <see cref="Subject"/>
    /// for <c>sub</c>, and the scope is the grant's.
    /// </summary>
    public IDictionary<string, string> Claims { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
