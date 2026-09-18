# Birko.Security.OAuth.Server

OAuth2 authorization server for the Birko Framework. Issues access and refresh tokens; persists clients, codes and tokens via `Birko.Data.Stores` so the same code runs against any supported backend (SQL, ElasticSearch, MongoDB, RavenDB, CosmosDB, JSON, XML).

Companion to [`Birko.Communication.OAuth`](../Birko.Communication.OAuth) — that project consumes tokens from upstream providers; this one issues them.

## Features

- **All standard grant types** — `client_credentials`, `authorization_code` (with PKCE), `refresh_token`, RFC 8628 device-code.
- **PKCE enforced for public clients** by default (RFC 7636; OAuth 2.1 SHOULD).
- **Refresh-token rotation** by default (RFC 6819 §5.2.2.3) — every refresh revokes the old token.
- **Pure handlers** — no ASP.NET dependency. Plug into Minimal APIs, controllers, `Birko.Communication.REST.Server`, or anything that can route HTTP.
- **Provider-agnostic persistence** — five `IAsyncStore<T>` interfaces, one for each entity.
- **Dynamic client registration** (RFC 7591) — register/read/update/delete from your admin panel.

## Quick start

```csharp
using Birko.Security;
using Birko.Security.Jwt;
using Birko.Security.OAuth.Server;
using Birko.Security.OAuth.Server.Endpoints.Token;

var settings = new OAuthServerSettings
{
    Location = "https://auth.example.com/",
    Issuer = "https://auth.example.com/",
    AccessTokenLifetimeSeconds = 3600,
    SupportedScopes = { "read", "write", "admin" }
};

var tokenOptions = new TokenOptions
{
    Secret = builder.Configuration["Oauth:SigningSecret"]!,
    Issuer = settings.Issuer,
    Audience = "https://api.example.com/"
};

var server = new OAuthServer(
    settings,
    tokens: new JwtTokenProvider(tokenOptions),
    tokenOptions: tokenOptions,
    clientStore: clientStore,       // your IOAuthClientStore implementation
    codeStore: codeStore,            // ...
    refreshStore: refreshStore,
    deviceStore: deviceStore,
    consentStore: consentStore,
    deviceVerificationUri: "https://auth.example.com/device");
```

Then route HTTP requests to the four handlers:

```csharp
app.MapPost("/token", async (HttpRequest req) =>
{
    var form = await req.ReadFormAsync();
    var request = new TokenRequest
    {
        GrantType = form["grant_type"]!,
        ClientId = form["client_id"]!,
        ClientSecret = form["client_secret"],
        Code = form["code"],
        RedirectUri = form["redirect_uri"],
        CodeVerifier = form["code_verifier"],
        RefreshToken = form["refresh_token"],
        DeviceCode = form["device_code"],
        Scope = form["scope"]
    };
    try
    {
        var response = await server.Token.HandleAsync(request);
        return Results.Json(new
        {
            access_token = response.AccessToken,
            token_type = response.TokenType,
            expires_in = response.ExpiresIn,
            refresh_token = response.RefreshToken,
            scope = response.Scope
        });
    }
    catch (OAuthServerException ex)
    {
        return Results.Json(new
        {
            error = ex.ErrorCode,
            error_description = ex.ErrorDescription
        }, statusCode: 400);
    }
});
```

## Persistence

You supply implementations of these five interfaces (any `IAsyncStore<T>` backend will do):

- `IOAuthClientStore` — registered OAuth clients
- `IAuthorizationCodeStore` — short-lived authorization codes
- `IRefreshTokenStore` — refresh tokens (stored as SHA-256 hashes, never plaintext)
- `IDeviceCodeStore` — in-flight RFC 8628 device-code requests
- `IConsentStore` — prior user-consent records

The simplest setup is to use the same backend you already use for the rest of your app. For example, with `Birko.Data.SQL`:

```csharp
public class SqlClientStore : Birko.Data.Stores.AbstractAsyncStore<OAuthClient>, IOAuthClientStore { /* … */ }
```

## What's out of scope

- **OpenID Connect** — `id_token`, UserInfo endpoint, discovery document. A separate `Birko.Security.OIDC.Server` is planned.
- **SAML 2.0** — different protocol entirely.
- **Resource-owner password grant** — deprecated by OAuth 2.1.
- **Implicit (`response_type=token`) flow** — deprecated by OAuth 2.1.

## License

MIT — see [License.md](License.md).
