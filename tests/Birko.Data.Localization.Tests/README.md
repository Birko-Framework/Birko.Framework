# Birko.Data.Localization.Tests

Unit tests for the Birko.Data.Localization entity localization project.

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0
- **Target:** .NET 10.0

## Test Coverage

- **EntityTranslationModelTests** — CopyTo, default values
- **EntityTranslationFilterTests** — All filter factory methods and expression matching
- **LocalizedStoreWrapperTests** — Sync bulk wrapper: create, read, update, delete with localization, constructor validation
- **AsyncLocalizedStoreWrapperTests** — Async bulk wrapper: same scenarios as sync

## Running Tests

```bash
dotnet test Birko.Data.Localization.Tests.csproj
```

## License

MIT — see [License.md](License.md)
