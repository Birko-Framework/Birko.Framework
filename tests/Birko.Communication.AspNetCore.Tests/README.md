# Birko.Communication.AspNetCore.Tests

xUnit + FluentAssertions tests for [`Birko.Communication.AspNetCore`](../Birko.Communication.AspNetCore).

## Coverage

- **`OwnedCrudResultsTests`** — the host-free guards: `ReadOwned` (200 + DTO when owned, 404 when absent/foreign), `CreateClash` (409 when the id is the caller's, 404 when foreign, `null` to proceed), `RequireOwned` (yields the owned entity or 404).
- **`MapOwnedCrudIntegrationTests`** — the wired `MapOwnedCrud` endpoints over a test host: GET/POST/PUT/DELETE happy path, foreign-entity → 404 on every verb, create id-clash → 409, and the `Created` location header.

## Test framework

- xUnit
- FluentAssertions
- `Microsoft.AspNetCore.App` framework reference (minimal-API host under test)

## Running tests

```
dotnet test
```

## License

MIT — see [License.md](License.md).
