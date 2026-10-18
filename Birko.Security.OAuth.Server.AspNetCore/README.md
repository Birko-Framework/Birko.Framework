# Birko.Security.OAuth.Server.AspNetCore

ASP.NET Core endpoints for the Birko OAuth authorization server.

## Features
- `app.MapOAuthServer()` maps `/token`, `/device_authorization`, `/device/approve`, `/authorize` and, opt-in,
  `/register` (+ `/register/{clientId}/disable`)
- Form and HTTP Basic client authentication, RFC 6749 error bodies and status codes, `no-store` on credential responses
- `/authorize` and device approval act for the signed-in user from the request principal
- `GrantHandlers` lets the app take over one grant type (or add an extension grant)

## Usage

```csharp
builder.Services.AddOAuthServerSqLiteStores("data/oauth.db");
builder.Services.AddSingleton(sp => new OAuthServer(settings, tokenProvider, tokenOptions, /* the five stores */ ...));

app.UseAuthentication();
app.UseAuthorization();
app.MapOAuthServer(o =>
{
    o.AllowDynamicRegistration = true;
    o.ConfigureRegistration = b => b.RequireAuthorization("admin");
    o.AuthorizeResponseMode = OAuthAuthorizeResponseMode.Redirect;
});
```

## Running tests

```
dotnet test tests/Birko.Security.OAuth.Server.AspNetCore.Tests
```

## License
See the repository root [LICENSE](../LICENSE).
