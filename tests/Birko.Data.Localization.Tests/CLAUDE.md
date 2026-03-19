# Birko.Data.Localization.Tests

## Overview
Unit tests for entity-level localization (Birko.Data.Localization).

## Project Location
`C:\Source\Birko.Data.Localization.Tests\` (test project, .csproj, net10.0)

## Components

### Test Classes
- **EntityTranslationModelTests** — CopyTo, default values
- **EntityTranslationFilterTests** — All static factory methods, expression matching
- **LocalizedStoreWrapperTests** — Sync bulk wrapper (create/read/update/delete, partial translations, constructor validation)
- **AsyncLocalizedStoreWrapperTests** — Async bulk wrapper (same coverage)

### Test Resources (`TestResources/`)
- **TestLocalizableModel** — Test entity implementing `ILocalizable` with Name, Description, Code
- **TestEntityLocalizationContext** — Mutable `IEntityLocalizationContext` for tests
- **InMemoryBulkStore** — In-memory `IBulkStore<T>` for sync tests
- **InMemoryAsyncBulkStore** — In-memory `IAsyncBulkStore<T>` for async tests

## Dependencies
- Birko.Data.Localization (projitems)
- Birko.Data.Core (projitems)
- Birko.Data.Stores (projitems)
- xUnit 2.9.3, FluentAssertions 7.0.0

## Maintenance
- When adding new features to Birko.Data.Localization, add corresponding tests here
- Follow existing patterns: in-memory stores, TestEntityLocalizationContext for culture switching
