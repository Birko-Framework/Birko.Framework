# Birko.Communication.OAuth.Providers.Tests

Unit tests for the pre-configured OAuth provider factories (GitHub device flow).

## Running Tests

```bash
dotnet test
```

Asserts the OAuthSettings produced by `GitHubOAuthProvider.CreateDeviceFlowSettings`
(endpoints, grant type, default scope, polling/timeout) and that `CreateDeviceFlowClient`
returns a non-null `IOAuthClient`.

## License

Part of the Birko Framework.
