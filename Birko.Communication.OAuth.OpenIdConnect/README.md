# Birko.Communication.OAuth.OpenIdConnect

OpenID Connect web sign-in with a verified `id_token`: Google, Microsoft, or any OIDC provider.

## Features
- Authorization code with PKCE, a `nonce`, and a `state` bound to the browser (stops login CSRF)
- The `id_token` verified against the provider's published keys: RS256 only, issuer, audience and `azp`, `exp` / `nbf`,
  nonce, `sub`
- Discovery document and keys cached on a `TimeProvider`; an unknown key refreshes once, rate-limited
- Google (both issuer forms) and Microsoft (`common`, `organizations`, `consumers`, a tenant; the `{tenantid}` issuer
  checked against the token's `tid`; an allow-list of tenants)
- A status result, not exceptions: `Succeeded`, `InvalidState`, `ProviderRefused`, `CodeRejected`, `InvalidIdToken`,
  `ProviderUnavailable`

## Usage

```csharp
var states = new InMemoryOAuthStateStore();            // a shared IOAuthStateStore on a multi-node host
var microsoft = new OpenIdSignIn(
    MicrosoftOpenIdProvider.Create(clientId, clientSecret, "https://app.example/signin/microsoft/callback"),
    states, httpClient);

// Start: set a random HttpOnly, Secure, SameSite=Lax cookie, then redirect to start.AuthorizationUrl
var start = await microsoft.BeginAsync(binding: cookieValue, data: "/decks");

// Callback
var result = await microsoft.CompleteAsync(
    new OpenIdCallback(query["state"], query["code"], query["error"], query["error_description"]), cookieValue);

if (result.Succeeded)
{
    var key = $"{result.Identity!.Provider}:{result.Identity.Subject}";
    // the app decides who may sign in, provisions the user, and sets its own session
}
```

`IdTokenValidator` verifies an `id_token` obtained some other way: `await validator.ValidateAsync(idToken, nonce)`.

## Running tests

```bash
cd tests/Birko.Communication.OAuth.OpenIdConnect.Tests && dotnet test --nologo
```

## License
See the repository's root `LICENSE`.
