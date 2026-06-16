using System;
using System.Collections.Generic;
using Birko.Data.Models;

namespace Birko.Security.OAuth.Server.Models;

/// <summary>
/// Registered OAuth2 client. <see cref="ClientId"/> is the public identifier;
/// <see cref="ClientSecretHash"/> stores the hashed secret for confidential clients.
/// </summary>
public class OAuthClient : AbstractModel
{
    /// <summary>Public client identifier (sent as <c>client_id</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the client secret; null/empty for public clients.</summary>
    public string? ClientSecretHash { get; set; }

    /// <summary>Confidential (has secret) or Public (PKCE-only).</summary>
    public OAuthClientType ClientType { get; set; } = OAuthClientType.Confidential;

    /// <summary>Human-readable name shown on the consent screen.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Allowed redirect URIs (exact-match per RFC 6749 §3.1.2).</summary>
    public List<string> RedirectUris { get; set; } = new();

    /// <summary>Grant types the client is allowed to use.</summary>
    public List<string> AllowedGrantTypes { get; set; } = new();

    /// <summary>Scopes the client is allowed to request.</summary>
    public List<string> AllowedScopes { get; set; } = new();

    /// <summary>When the client was registered (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Whether the client is currently active. Inactive clients are rejected at the token endpoint.</summary>
    public bool IsEnabled { get; set; } = true;
}
