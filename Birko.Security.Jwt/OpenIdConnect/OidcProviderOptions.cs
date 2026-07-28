using System;

namespace Birko.Security.Jwt.OpenIdConnect;

/// <summary>
/// OpenID Connect settings for one external identity provider whose id tokens we accept.
/// <para>
/// Every field except <see cref="AdditionalAudiences"/> is required. A provider with an incomplete
/// configuration must be treated as <b>not configured</b> and refused — absence of configuration must
/// never degrade into "verification not required".
/// </para>
/// </summary>
public class OidcProviderOptions
{
    /// <summary>
    /// Our client id at the provider. A token's <c>aud</c> must equal this (or one of
    /// <see cref="AdditionalAudiences"/>) — that is what binds the token to <i>this</i> relying party,
    /// so a token harvested from another site cannot be replayed here.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Expected <c>iss</c> claim, e.g. <c>https://accounts.google.com</c>.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// The provider's published JWKS endpoint, e.g. <c>https://www.googleapis.com/oauth2/v3/certs</c>.
    /// </summary>
    public string JwksUri { get; set; } = string.Empty;

    /// <summary>
    /// Extra accepted <c>aud</c> values, for providers that mint id tokens for a sibling client id
    /// (e.g. a native app id alongside the web client id). Empty by default — widen deliberately.
    /// </summary>
    public string[] AdditionalAudiences { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Name of the first missing required setting, or <c>null</c> when fully configured.
    /// Lets a caller log <i>why</i> a provider was refused instead of failing opaquely.
    /// </summary>
    public string? FirstMissingSetting()
    {
        if (string.IsNullOrWhiteSpace(ClientId)) return nameof(ClientId);
        if (string.IsNullOrWhiteSpace(Issuer)) return nameof(Issuer);
        if (string.IsNullOrWhiteSpace(JwksUri)) return nameof(JwksUri);
        return null;
    }
}
