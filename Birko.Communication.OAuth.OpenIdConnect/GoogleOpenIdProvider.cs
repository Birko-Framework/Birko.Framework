namespace Birko.Communication.OAuth.OpenIdConnect;

/// <summary>Google sign-in (OpenID Connect).</summary>
public static class GoogleOpenIdProvider
{
    /// <summary>The provider name an identity carries: <c>google</c>.</summary>
    public const string Name = "google";

    /// <summary>Google's discovery document.</summary>
    public const string MetadataAddress = "https://accounts.google.com/.well-known/openid-configuration";

    /// <summary>
    /// Options for Google sign-in. Google issues <c>id_token</c>s with <c>iss</c> either
    /// <c>https://accounts.google.com</c> (the discovery issuer) or <c>accounts.google.com</c>; both are accepted.
    /// </summary>
    /// <param name="clientId">The OAuth client id (a "Web application" client in Google Cloud).</param>
    /// <param name="clientSecret">Its client secret.</param>
    /// <param name="redirectUri">An authorized redirect URI of that client.</param>
    /// <param name="scope">Default <c>openid email profile</c>.</param>
    public static OpenIdProviderOptions Create(string clientId, string clientSecret, string redirectUri, string scope = "openid email profile")
    {
        var options = new OpenIdProviderOptions
        {
            Name = Name,
            MetadataAddress = MetadataAddress,
            ClientId = clientId,
            ClientSecret = clientSecret,
            RedirectUri = redirectUri,
            Scope = scope,
            AdditionalIssuers = { "accounts.google.com" },
        };
        options.Validate();
        return options;
    }
}
