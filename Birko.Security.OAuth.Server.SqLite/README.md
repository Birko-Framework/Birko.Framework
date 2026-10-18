# Birko.Security.OAuth.Server.SqLite

SQLite stores for the Birko OAuth authorization server.

## Features
- All five store interfaces on one SQLite file; tables `oauth_*` created on first use
- A code is redeemed, and a refresh token rotated, exactly once even under concurrent requests (conditional updates)
- OAuth clients' collections and refresh records' extra claims persist (JSON columns)
- `AddOAuthServerSqLiteStores(settings)` / `AddOAuthServerSqLiteStores("data/oauth.db")` registers one shared instance
  of each

## Usage

```csharp
services.AddOAuthServerSqLiteStores("data/oauth.db");
// then build OAuthServer from the five registered stores
```

## Running tests

```
dotnet test tests/Birko.Security.OAuth.Server.SqLite.Tests
```

## License
See the repository's root `LICENSE`.
