# Birko.Communication.OAuth

OAuth2 client library for the Birko Framework providing support for multiple OAuth2 flows with automatic token caching and refresh.

## Features

- **Client Credentials** — Machine-to-machine authentication (no user interaction)
- **Authorization Code** — Server-side web apps with confidential client
- **Authorization Code + PKCE** — Public clients (SPAs, mobile, CLI)
- **Device Code** — Input-constrained devices (CLI, IoT, smart TV)
- **Refresh Token** — Automatic and manual token refresh
- **Token Caching** — Thread-safe token caching with configurable expiry buffer
- **DelegatingHandler** — Automatic Bearer token injection for HttpClient with 401 retry
- **PKCE Generation** — Built-in SHA-256 PKCE challenge pair generation
- **Settings Hierarchy** — Extends `RemoteSettings` (ClientId = UserName, ClientSecret = Password, TokenEndpoint = Location)

## Usage

### Client Credentials Flow

```csharp
var settings = new OAuthSettings
{
    TokenEndpoint = "https://auth.example.com/oauth/token",
    ClientId = "my-client-id",
    ClientSecret = "my-client-secret",
    Scope = "api.read api.write",
    GrantType = OAuthGrantType.ClientCredentials
};

var client = new OAuthClient(settings);
var token = await client.GetTokenAsync();
// token.AccessToken, token.ExpiresAt, token.TokenType
```

### Authorization Code + PKCE Flow

```csharp
var settings = new OAuthSettings
{
    TokenEndpoint = "https://auth.example.com/oauth/token",
    AuthorizationEndpoint = "https://auth.example.com/authorize",
    ClientId = "my-public-client",
    RedirectUri = "http://localhost:8080/callback",
    Scope = "openid profile",
    GrantType = OAuthGrantType.AuthorizationCodePkce
};

var client = new OAuthClient(settings);

// 1. Generate PKCE challenge and build authorization URL
var pkce = PkceChallenge.Generate();
var authUrl = client.BuildAuthorizationUrl(state: "random-state", pkce);
// Redirect user to authUrl...

// 2. After callback, exchange code for tokens
var token = await client.ExchangeCodeAsync(code: "auth-code-from-callback", codeVerifier: pkce.CodeVerifier);
```

### Device Code Flow

```csharp
var settings = new OAuthSettings
{
    TokenEndpoint = "https://auth.example.com/oauth/token",
    DeviceAuthorizationEndpoint = "https://auth.example.com/oauth/device/code",
    ClientId = "my-device-client",
    Scope = "openid profile",
    GrantType = OAuthGrantType.DeviceCode
};

var client = new OAuthClient(settings);

// 1. Request device authorization
var deviceAuth = await client.RequestDeviceAuthorizationAsync();
Console.WriteLine($"Visit {deviceAuth.VerificationUri} and enter code: {deviceAuth.UserCode}");

// 2. Poll for token (blocks until user authorizes or timeout)
var token = await client.PollDeviceTokenAsync(deviceAuth.DeviceCode);
```

### DelegatingHandler for Automatic Token Injection

```csharp
var oauthClient = new OAuthClient(settings);
var httpClient = new HttpClient(new OAuthDelegatingHandler(oauthClient)
{
    InnerHandler = new HttpClientHandler()
});

// All requests automatically include Bearer token; 401 triggers a single retry with fresh token
var response = await httpClient.GetAsync("https://api.example.com/data");
```

## Dependencies

- **Birko.Configuration** — `RemoteSettings` base class for `OAuthSettings`
- **System.Text.Json** — Token response parsing
- **System.Security.Cryptography** — PKCE challenge generation

## License

MIT License - see [License.md](License.md)
