# Birko.Data.Aggregates.Tests

Unit tests for the Birko.Data.Aggregates project — SQL ↔ NoSQL aggregate mapper.

## Test Framework

- **xUnit** 2.9.3
- **FluentAssertions** 7.0.0

## Test Classes

- **AggregateDefinitionTests** — Fluent API: HasMany/HasOne/Via/Through, relationship metadata, root types
- **AggregateMapperFlattenTests** — Flatten sync/async: root resolution, nested collections/singles, typed accessors, validation
- **AggregateMapperExpandTests** — Expand sync/async: no-change detection, collection add/remove/mixed, OneToOne add/remove/replace
- **DiffByKeyTests** — EnumerableHelper.DiffByKey: empty, added, removed, unchanged, mixed, null keys, custom key types, filter
- **SyncPipelineExtensionsTests** — CreateMapper, FlattenForSync, ExpandFromSync, ExpandManyFromSync

## Running Tests

```bash
dotnet test Birko.Data.Aggregates.Tests/Birko.Data.Aggregates.Tests.csproj
```

## License

MIT License - see [License.md](License.md)
