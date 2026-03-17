# Birko.Messaging.Razor.Tests

Unit tests for the Birko.Messaging.Razor project.

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0
- **RazorLight** 2.3.1

## Test Coverage

- **RazorTemplateOptionsTests** — Default values, property assignment
- **RazorFileTemplateProviderTests** — File loading, caching, invalidation, directory traversal protection, error handling
- **RazorTemplateEngineTests** — Inline rendering, Razor conditionals/loops, file-based rendering, IMessageTemplate support, caching, dispose behavior, error handling

## Running Tests

```bash
dotnet test Birko.Messaging.Razor.Tests.csproj
```

## License

Part of the Birko Framework. See [License.md](License.md).
