# Birko.Helpers

General-purpose helper utilities and extensions for the Birko Framework.

## Features

- Collection operations (Batch, Page, DistinctBy, Diff)
- String operations (Truncate, ToSlug)
- Guid and DateTime helpers
- Path validation
- HTML helpers

## Installation

```bash
dotnet add package Birko.Helpers
```

## Dependencies

- .NET 10.0

## Usage

```csharp
using Birko.Helpers;

// Collection helpers
var batches = items.Batch(100);
var page = items.Page(pageNumber: 2, pageSize: 20);
var unique = items.DistinctBy(x => x.Name);
var diff = EnumerableHelper.Diff(listA, listB);

// String helpers
var slug = "Hello World!".ToSlug(); // "hello-world"
var truncated = longText.Truncate(50);
```

## API Reference

- **EnumerableHelper** - Batch, Page, DistinctBy, Diff
- **StringHelper** - Truncate, ToSlug
- **ObjectHelper** - Object utilities
- **HtmlHelper** - HTML utilities
- **PathValidator** - Path validation

## Related Projects

- [Birko.Helpers.Tests](../Birko.Helpers.Tests/) - Unit tests

## License

Part of the Birko Framework.
