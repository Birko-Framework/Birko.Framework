# Birko.Security.NFC.Tests

Unit tests for the Birko.Security.NFC authentication library.

## Test Coverage

- **NfcAuthProviderTests** — Enroll/authenticate/revoke flows, UID normalization (case, colons, dashes), expiration enforcement, max tags limit, usage tracking, query methods
- **NfcTagMappingTests** — Default values, IsExpired logic
- **NfcAuthResultTests** — Success/Failure factory methods, token inclusion
- **NfcAuthSettingsTests** — Default values
- **InMemoryNfcTagMappingStoreTests** — CRUD operations, duplicate detection, user query

## Test Framework

- xUnit 2.9.3
- FluentAssertions 7.0.0
- .NET 10.0

## Running Tests

```bash
dotnet test Birko.Security.NFC.Tests
```

## License

See [License.md](License.md) for details.
