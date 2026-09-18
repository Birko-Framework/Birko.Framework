# Birko.CQRS.Tests

Unit tests for the Birko.CQRS project.

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0
- **.NET 10.0**

## Test Coverage

- **UnitTests** — Unit struct (value, equality, comparison, hash, toString)
- **MediatorTests** — Command/query dispatch, handler resolution, error cases
- **PipelineTests** — Behavior ordering, short-circuit, no-behaviors, query behaviors
- **DiRegistrationTests** — Service collection extensions, handler/behavior registration, scoping

## Running Tests

```bash
dotnet test Birko.CQRS.Tests.csproj
```

## License

MIT License - see [License.md](License.md) for details.
