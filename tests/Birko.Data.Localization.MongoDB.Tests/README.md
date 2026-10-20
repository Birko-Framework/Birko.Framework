# Birko.Data.Localization.MongoDB.Tests

Live tests for Birko.Data.Localization.MongoDB, gated on `BIRKO_MONGO_HOST` (`BIRKO_REQUIRE_LIVE` makes its absence a failure).

## Test framework
xUnit + FluentAssertions.

## Running tests
```
BIRKO_MONGO_HOST=localhost dotnet test --nologo
```
