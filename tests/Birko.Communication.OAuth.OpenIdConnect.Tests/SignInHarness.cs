using Microsoft.IdentityModel.Tokens;

namespace Birko.Communication.OAuth.OpenIdConnect.Tests;

/// <summary>An <see cref="OpenIdSignIn"/> against a <see cref="FakeOpenIdProvider"/>, on a manual clock.</summary>
internal sealed class SignInHarness
{
    public const string ClientId = "client-1";
    public const string Binding = "browser-1";

    public ManualClock Clock { get; } = new();
    public FakeOpenIdProvider Provider { get; } = new();
    public OpenIdProviderOptions Options { get; }
    public InMemoryOAuthStateStore States { get; }
    public OpenIdSignIn SignIn { get; }

    public SignInHarness(Action<OpenIdProviderOptions>? configure = null)
    {
        Options = new OpenIdProviderOptions
        {
            Name = "test",
            MetadataAddress = FakeOpenIdProvider.MetadataAddress,
            ClientId = ClientId,
            ClientSecret = "secret-1",
            RedirectUri = "https://app.test/signin/callback",
        };
        configure?.Invoke(Options);
        States = new InMemoryOAuthStateStore(clock: Clock);
        SignIn = new OpenIdSignIn(Options, States, new HttpClient(Provider), Clock);
    }

    /// <summary>The claims of a valid token for <paramref name="nonce"/>, issued now.</summary>
    public Dictionary<string, object> Claims(string nonce)
    {
        var now = Clock.Now.ToUnixTimeSeconds();
        return new Dictionary<string, object>
        {
            ["iss"] = FakeOpenIdProvider.Base,
            ["aud"] = ClientId,
            ["sub"] = "user-1",
            ["iat"] = now,
            ["nbf"] = now,
            ["exp"] = now + 600,
            ["nonce"] = nonce,
            ["email"] = "ada@example.test",
            ["email_verified"] = true,
            ["name"] = "Ada",
        };
    }

    /// <summary>Begins a sign-in and returns its state and the nonce its authorization URL carries.</summary>
    public async Task<(string State, string Nonce, Uri Url)> BeginAsync(string binding = Binding, string? data = null)
    {
        var start = await SignIn.BeginAsync(binding, data);
        var url = new Uri(start.AuthorizationUrl);
        return (start.State, Query(url)["nonce"], url);
    }

    /// <summary>
    /// A whole sign-in: begin, let the provider answer code <c>good</c> with the token <paramref name="idToken"/> builds
    /// from the claims for this sign-in's nonce, and complete.
    /// </summary>
    public async Task<OpenIdSignInResult> SignInWithAsync(Func<Dictionary<string, object>, string> idToken, string? data = null)
    {
        var (state, nonce, _) = await BeginAsync(data: data);
        Provider.NextIdToken = idToken(Claims(nonce));
        return await SignIn.CompleteAsync(new OpenIdCallback(state, "good"), Binding);
    }

    public Task<OpenIdSignInResult> SignInWithClaimsAsync(Action<Dictionary<string, object>> change, RsaSecurityKey? key = null) =>
        SignInWithAsync(claims => { change(claims); return Provider.Sign(claims, key); });

    public static Dictionary<string, string> Query(Uri url) =>
        url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
}
