# Birko.Data.Aggregates.Tests

## Overview
Unit tests for the Birko.Data.Aggregates project — SQL ↔ NoSQL aggregate mapper.

## Project Location
`C:\Source\Birko.Data.Aggregates.Tests\`

## Dependencies
- **Birko.Data.Aggregates** (shared project via .projitems)
- **Birko.Data.Core** (AbstractModel)
- **Birko.Data.Stores** (store interfaces)
- xUnit 2.9.3
- FluentAssertions 7.0.0

## Test Classes
- **AggregateDefinitionTests** — Fluent definition: HasMany/HasOne/Via/Through, relationship metadata, root types
- **AggregateMapperFlattenTests** — Flatten sync/async: root resolution, nested collections/singles, typed accessors, validation, FlattenMany
- **AggregateMapperExpandTests** — Expand sync/async: no-change detection, collection add/remove/mixed, OneToOne add/remove/replace
- **CollectionDifferTests** — Diff by Guid: empty, all-added, all-removed, unchanged, mixed, null-Guid handling, AbstractModel overload
- **SyncPipelineExtensionsTests** — CreateMapper, FlattenForSync, ExpandFromSync, ExpandManyFromSync (sync + async)

## Test Resources
- **TestModels.cs** — Product, Category, Tag, Image, ProductCategory, Order, OrderLine, Customer
- **TestAggregateDefinitions.cs** — ProductAggregate (m:n + 1:n + 1:1), OrderAggregate (1:n + 1:1)
- **InMemoryRelatedDataProvider.cs** — Dictionary-based IRelatedDataProvider + IAsyncRelatedDataProvider

## Running Tests
```bash
dotnet test Birko.Data.Aggregates.Tests/Birko.Data.Aggregates.Tests.csproj
```

## Maintenance
When adding new functionality to Birko.Data.Aggregates, add corresponding tests here.
