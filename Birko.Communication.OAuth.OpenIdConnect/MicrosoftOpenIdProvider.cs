using System;
using System.Collections.Generic;

namespace Birko.Communication.OAuth.OpenIdConnect;

/// <summary>Microsoft sign-in (Microsoft identity platform v2.0: work and school accounts, personal accounts).</summary>
public static class MicrosoftOpenIdProvider
{
    /// <summary>The provider name an identity carries: <c>microsoft</c>.</summary>
    public const string Name = "microsoft";

    /// <summary>Work, school and personal accounts.</summary>
    public const string Common = "common";

    /// <summary>Work and school accounts only.</summary>
    public const string Organizations = "organizations";

    /// <summary>Personal Microsoft accounts only.</summary>
    public const string Consumers = "consumers";

    /// <summary>The discovery document of <paramref name="tenant"/>.</summary>
    public static string MetadataAddressFor(string tenant) =>
        $"https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration";

    /// <summary>
    /// Options for Microsoft sign-in. On <c>common</c> and <c>organizations</c> the discovery issuer is
    /// <c>https://login.microsoftonline.com/{tenantid}/v2.0</c>: a token is accepted only when its <c>iss</c> is that
    /// issuer with the token's own <c>tid</c> (a GUID) in place. Any tenant's account can sign in there, so pass
    /// <paramref name="allowedTenantIds"/> to admit only some organizations.
    /// </summary>
    /// <param name="clientId">The application (client) id of the app registration.</param>
    /// <param name="clientSecret">A client secret of that registration.</param>
    /// <param name="redirectUri">A web redirect URI of that registration.</param>
    /// <param name="tenant"><c>common</c>, <c>organizations</c>, <c>consumers</c>, or a tenant id (GUID).</param>
    /// <param name="allowedTenantIds">When given, only these tenants' accounts are accepted.</param>
    /// <param name="scope">Default <c>openid email profile</c>.</param>
    public static OpenIdProviderOptions Create(
        string clientId,
        string clientSecret,
        string redirectUri,
        string tenant = Common,
        IEnumerable<Guid>? allowedTenantIds = null,
        string scope = "openid email profile")
    {
        if (tenant is not (Common or Organizations or Consumers) && !Guid.TryParseExact(tenant, "D", out _))
            throw new ArgumentException("The tenant must be common, organizations, consumers or a tenant id (GUID).", nameof(tenant));

        var options = new OpenIdProviderOptions
        {
            Name = Name,
            MetadataAddress = MetadataAddressFor(tenant),
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUri = redirectUri,
            Scope = scope,
        };
        if (allowedTenantIds != null)
        {
            foreach (var id in allowedTenantIds) options.AllowedTenantIds.Add(id);
        }
        options.Validate();
        return options;
    }
}
