# Birko.Data.Localization.RavenDB.Tests

Live tests for Birko.Data.Localization.RavenDB, gated on `BIRKO_RAVEN_URL` (`BIRKO_REQUIRE_LIVE` makes its absence a failure).

## Test framework
xUnit + FluentAssertions.

## Running tests
```
BIRKO_RAVEN_URL=http://localhost:8080 dotnet test --nologo
```
