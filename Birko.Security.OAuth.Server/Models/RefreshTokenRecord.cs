using System;
using System.Collections.Generic;
using Birko.Data.Models;

namespace Birko.Security.OAuth.Server.Models;

/// <summary>
/// A refresh token issued alongside an access token. The presented token is hashed
/// before lookup so the store never holds plaintext.
/// </summary>
public class RefreshTokenRecord : AbstractModel
{
    /// <summary>SHA-256 hash of the refresh token (hex, uppercase).</summary>
    public string TokenHash { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set to <c>true</c> on rotation, logout, or compromise.</summary>
    public bool Revoked { get; set; }

    /// <summary>
    /// Extra access-token claims, beyond the registered ones (<c>sub</c>, <c>client_id</c>, <c>iss</c>, <c>scope</c>),
    /// re-issued on every refresh and carried to the rotated record. Set by
    /// <see cref="Endpoints.Token.TokenEndpointHandler.IssueForSubjectAsync"/>; empty for the protocol grants.
    /// A store must persist it, or a refreshed access token loses these claims.
    /// </summary>
    public Dictionary<string, string> Claims { get; set; } = new();
}
