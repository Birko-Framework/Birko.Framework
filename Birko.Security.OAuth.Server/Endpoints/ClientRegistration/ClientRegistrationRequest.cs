using System.Collections.Generic;

namespace Birko.Security.OAuth.Server.Endpoints.ClientRegistration;

/// <summary>
/// Body of a Dynamic Client Registration request (RFC 7591 §2). The host can also
/// expose update/delete via <see cref="ClientRegistrationHandler"/>.
/// </summary>
public class ClientRegistrationRequest
{
    public string Name { get; set; } = string.Empty;
    public OAuthClientType ClientType { get; set; } = OAuthClientType.Confidential;
    public List<string> RedirectUris { get; set; } = new();
    public List<string> AllowedGrantTypes { get; set; } = new();
    public List<string> AllowedScopes { get; set; } = new();
}
