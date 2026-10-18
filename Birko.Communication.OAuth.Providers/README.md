# Birko.Communication.OAuth.Providers

Pre-configured OAuth client factories for specific services.

## Overview

Birko.Communication.OAuth.Providers offers ready-made factory methods that create properly configured OAuth clients for specific services, eliminating boilerplate setup.

## Components

| Type | Namespace | Description |
|------|-----------|-------------|
| `GitHubOAuthProvider` | `Birko.Communication.OAuth.Providers` | GitHub settings: device flow, and web (authorization-code) sign-in |
| `GitHubUserClient` / `IGitHubUserClient` | `Birko.Communication.OAuth.Providers` | Reads the signed-in user (`GitHubUser`) and their verified primary email |

## Dependencies

- **Birko.Communication.OAuth** — OAuth client infrastructure

## Usage

```xml
<Import Project="..\Birko.Communication.OAuth.Providers\Birko.Communication.OAuth.Providers.projitems" Label="Shared" />
```

```csharp
using Birko.Communication.OAuth.Providers;

var client = GitHubOAuthProvider.CreateDeviceFlowClient("client-id");
```

### GitHub web sign-in

```csharp
var settings = GitHubOAuthProvider.CreateWebFlowSettings(clientId, clientSecret, "https://app.example.com/auth/github/callback");
services.AddSingleton<IOAuthStateStore, InMemoryOAuthStateStore>();
services.AddHttpClient<IOAuthCodeExchanger, OAuthCodeExchanger>((http, _) => new OAuthCodeExchanger(settings, http));
services.AddHttpClient<IGitHubUserClient, GitHubUserClient>((http, _) => new GitHubUserClient(http, "MyApp"));

// in the callback, after the state check:
var token = await exchanger.ExchangeAsync(code);
var user  = await users.GetUserAsync(token.AccessToken);              // key accounts on user.Id
var email = user.Email ?? await users.GetPrimaryEmailAsync(token.AccessToken);
```

## License

MIT License - see [License.md](License.md)
