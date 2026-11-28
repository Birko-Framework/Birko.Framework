# Birko.Security.OAuth.Server

OAuth2 authorization server for the Birko Framework. Issues access and refresh tokens; persists clients, codes and tokens via `Birko.Data.Stores` so the same code runs against any supported backend (SQL, ElasticSearch, MongoDB, RavenDB, CosmosDB, JSON, XML).

Companion to [`Birko.Communication.OAuth`](../Birko.Communication.OAuth) — that project consumes tokens from upstream providers; this one issues them.

## Features

- **All standard grant types** — `client_credentials`, `authorization_code` (with PKCE), `refresh_token`, RFC 8628 device-code.
- **PKCE enforced for public clients** by default (RFC 7636; OAuth 2.1 SHOULD).
- **Refresh-token rotation** by default (RFC 6819 §5.2.2.3) — every refresh revokes the old token.
- **Pure handlers** — no ASP.NET dependency. Plug into Minimal APIs, controllers, `Birko.Communication.REST.Server`, or anything that can route HTTP.
- **Provider-agnostic persistence** — five `IAsyncStore<T>` interfaces, one for each entity, and
  `AddOAuthServerStores(IStoreFactory)` to put all five on any store-factory backend.
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

The simplest setup is the store factory you already use for the rest of your app (TASK-1013): one call registers
all five, as singletons, on that backend.

```csharp
using Birko.Security.OAuth.Server.Extensions;

services.AddOAuthServerStores(
    StoreFactory.Create(cfg.Backend, cfg.ConnectionString, p => p.AddSqLite().AddPostgreSQL().AddMongoDB()));
// then build OAuthServer from the five registered interfaces
```

- Works on every backend whose stores can claim atomically (`IAsyncConditionalUpdateStore<T>`): SQLite, PostgreSQL,
  MySQL, SQL Server, MongoDB, RavenDB, Cosmos DB, Elasticsearch, InMemory. JSON and XML are refused, by name, at
  registration: a code redeemed (or a refresh token rotated) by two concurrent requests could otherwise succeed twice.
- A code is redeemed, and a refresh token rotated, exactly once under concurrent requests (one conditional update).
- A client's lists and a refresh record's extra claims are stored as JSON text, the same shape on SQL and document
  backends. A predicate on a client's or refresh record's scalar members is answered by the backend
  (`GetByClientIdAsync`, `GetByNameAsync`); one on `RedirectUris`, `AllowedGrantTypes`, `AllowedScopes` or `Claims` is
  refused.
- Tables (`oauth_clients`, `oauth_refresh_tokens`, `oauth_auth_codes`, `oauth_device_codes`, `oauth_consents`) are
  those of the deleted `Birko.Security.OAuth.Server.SqLite`, so a database it wrote is read and written as is.
- On Elasticsearch a search sees a write only after the next index refresh (~1 s), and the lookups are searches, so
  every OAuth write refreshes the store's index (`IAsyncRefreshableStore`): a code is found the moment it is issued.
  That costs the cluster one refresh per OAuth write.

The same stores can be built one by one (`new FactoryOAuthClientStore(factory)`, …, in
`Birko.Security.OAuth.Server.Stores.Factory`). `Birko.Security.OAuth.Server.InMemory` remains the test double.
`Birko.Security.OAuth.Server.SqLite` was replaced by the factory stores and deleted (TASK-1013).

## What's out of scope

- **OpenID Connect** — `id_token`, UserInfo endpoint, discovery document. A separate `Birko.Security.OIDC.Server` is planned.
- **SAML 2.0** — different protocol entirely.
- **Resource-owner password grant** — deprecated by OAuth 2.1.
- **Implicit (`response_type=token`) flow** — deprecated by OAuth 2.1.

## License

MIT — see [License.md](License.md).
