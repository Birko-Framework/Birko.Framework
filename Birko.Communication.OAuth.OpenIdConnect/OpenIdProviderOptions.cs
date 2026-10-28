using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Communication.OAuth.OpenIdConnect;

/// <summary>
/// One OpenID Connect provider an app signs users in with: where its discovery document is, the app's registration
/// with it, and how its <c>id_token</c> issuer is checked. Use <see cref="GoogleOpenIdProvider"/> or
/// <see cref="MicrosoftOpenIdProvider"/>, or fill it for another provider.
/// </summary>
public sealed class OpenIdProviderOptions
{
    /// <summary>Parameters the sign-in sets itself; <see cref="AuthorizationParameters"/> may not carry them.</summary>
    public static readonly IReadOnlyCollection<string> ReservedAuthorizationParameters = new[]
    {
        "response_type", "client_id", "redirect_uri", "scope", "state", "nonce", "code_challenge", "code_challenge_method",
    };

    /// <summary>The provider's name in <see cref="OpenIdIdentity.Provider"/>, e.g. <c>google</c>. Store an identity as
    /// <c>Provider:Subject</c>: a subject is unique only within its provider.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The discovery document's URL (<c>…/.well-known/openid-configuration</c>). Must be https.</summary>
    public string MetadataAddress { get; set; } = string.Empty;

    /// <summary>The app's client id with the provider; the <c>id_token</c>'s audience.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The app's client secret, sent with the code exchange beside the PKCE verifier. Empty for a public client.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The callback URL registered with the provider.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>Space-separated scopes; must include <c>openid</c>. Default <c>openid email profile</c>.</summary>
    public string Scope { get; set; } = "openid email profile";

    /// <summary>
    /// Issuers accepted besides the discovery document's. Google issues both <c>https://accounts.google.com</c> and
    /// <c>accounts.google.com</c>. A discovery issuer holding <c>{tenantid}</c> (Microsoft's multi-tenant endpoints) is
    /// matched with the token's <c>tid</c> put in its place, and <c>tid</c> must then be a GUID.
    /// </summary>
    public IList<string> AdditionalIssuers { get; set; } = new List<string>();

    /// <summary>
    /// When not empty, only tokens whose <c>tid</c> claim is one of these tenant ids are accepted (Microsoft). A token
    /// without <c>tid</c> is then refused.
    /// </summary>
    public IList<Guid> AllowedTenantIds { get; set; } = new List<Guid>();

    /// <summary>Extra authorization-request parameters, e.g. <c>prompt=select_account</c>. Not the reserved ones.</summary>
    public IDictionary<string, string> AuthorizationParameters { get; set; } = new Dictionary<string, string>();

    /// <summary>Tolerance on <c>exp</c> and <c>nbf</c> for clock difference with the provider. Default 2 minutes.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long the discovery document and signing keys are used before they are fetched again. Default 24 hours.</summary>
    public TimeSpan MetadataRefreshInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// The least time between two fetches of the keys. A token signed with a key not in the cached set causes one fetch
    /// (the provider rotated its keys); within this interval it does not, so forged tokens cannot make the app hammer
    /// the provider. Default 5 minutes.
    /// </summary>
    public TimeSpan MinimumMetadataRefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Timeout of one request to the provider (discovery, keys, code exchange). Default 30 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Throws <see cref="ArgumentException"/> naming the first setting that cannot work.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("A provider name is required.", nameof(Name));
        if (!Uri.TryCreate(MetadataAddress, UriKind.Absolute, out var metadata) || metadata.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("MetadataAddress must be an absolute https URL.", nameof(MetadataAddress));
        if (string.IsNullOrWhiteSpace(ClientId)) throw new ArgumentException("A client id is required.", nameof(ClientId));
        if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out _))
            throw new ArgumentException("RedirectUri must be an absolute URL.", nameof(RedirectUri));
        if (!(Scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("openid", StringComparer.Ordinal))
            throw new ArgumentException("Scope must include openid, or the provider returns no id_token.", nameof(Scope));
        var reserved = AuthorizationParameters.Keys.FirstOrDefault(k => ReservedAuthorizationParameters.Contains(k, StringComparer.Ordinal));
        if (reserved != null)
            throw new ArgumentException($"AuthorizationParameters may not set '{reserved}'; the sign-in sets it.", nameof(AuthorizationParameters));
        if (ClockSkew < TimeSpan.Zero) throw new ArgumentException("ClockSkew may not be negative.", nameof(ClockSkew));
        if (MetadataRefreshInterval <= TimeSpan.Zero) throw new ArgumentException("MetadataRefreshInterval must be positive.", nameof(MetadataRefreshInterval));
        if (MinimumMetadataRefreshInterval < TimeSpan.Zero)
            throw new ArgumentException("MinimumMetadataRefreshInterval may not be negative.", nameof(MinimumMetadataRefreshInterval));
        if (RequestTimeout <= TimeSpan.Zero) throw new ArgumentException("RequestTimeout must be positive.", nameof(RequestTimeout));
    }
}
