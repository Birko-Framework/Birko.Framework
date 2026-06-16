using System;
using System.Collections.Generic;

namespace Birko.Security.OAuth.Server.Endpoints.ClientRegistration;

/// <summary>
/// Dynamic Client Registration response (RFC 7591 §3.2.1). <see cref="ClientSecret"/>
/// is returned only on the initial registration — subsequent reads omit it.
/// </summary>
public class ClientRegistrationResponse
{
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Plaintext secret — returned once at registration; never stored in plaintext.</summary>
    public string? ClientSecret { get; set; }

    public OAuthClientType ClientType { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> RedirectUris { get; set; } = new();
    public List<string> AllowedGrantTypes { get; set; } = new();
    public List<string> AllowedScopes { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
