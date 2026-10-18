# Birko.Security.OAuth.Server.InMemory

In-memory stores for the Birko OAuth authorization server.

## Features
- All five store interfaces (`IOAuthClientStore`, `IAuthorizationCodeStore`, `IRefreshTokenStore`, `IDeviceCodeStore`,
  `IConsentStore`), thread-safe, no persistence
- A code is redeemed, and a refresh token rotated, exactly once even under concurrent requests
- `AddOAuthServerInMemoryStores()` registers one shared instance of each

## Usage

```csharp
services.AddOAuthServerInMemoryStores();
// then build OAuthServer from the five registered stores
```

## Running tests

```
dotnet test tests/Birko.Security.OAuth.Server.InMemory.Tests
```

## License
See the repository's root `LICENSE`.
