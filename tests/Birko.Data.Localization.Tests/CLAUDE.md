# Birko.Data.Localization.Tests

## Overview
Unit tests for entity-level localization (Birko.Data.Localization).

## Project Location
`tests/Birko.Data.Localization.Tests/` (test project, .csproj, net10.0)

## Components

### Test Classes
- **EntityTranslationModelTests** — CopyTo, default values
- **EntityTranslationFilterTests** — All static factory methods, expression matching
- **LocalizedStoreWrapperTests** — Sync bulk wrapper (create/read/update/delete, partial translations, constructor validation)
- **AsyncLocalizedStoreWrapperTests** — Async bulk wrapper (same coverage)
- **LocalizationBatchedReadTests** — TASK-679: one translation read per 500 entities on list reads
- **LocalizationBoundaryTests** — TASK-683: which writes enter the boundary (a recording boundary), the atomic skip of the best-effort save, the cleanup report on a non-atomic delete, the stored items' translations after `EntityNotFoundException`, materialization before a retried boundary
- **EntityTranslationsTests** — TASK-682: every `IEntityTranslations` method (results, read counts, refusals, fallback, `exceptEntityGuid`, ambiguity, the localized-wrapper refusal through a chain, DI), and that the API and the wrappers read each other's writes
- **LocalizationBatchedWriteTests** — TASK-690: write calls counted (one read + at most one bulk delete, update and create per save; one filter delete per 500 entities), the batched unique-key retry, `DeleteCulture`, `ReadAllCultures`, and the wrappers' collection, filter and delete paths

### Test Resources (`TestResources/`)
- **TestLocalizableModel** — Test entity implementing `ILocalizable` with Name, Description, Code
- **TestEntityLocalizationContext** — Mutable `IEntityLocalizationContext` for tests
- **InMemoryBulkStore** — In-memory `IBulkStore<T>` for sync tests
- **InMemoryAsyncBulkStore** — In-memory `IAsyncBulkStore<T>` for async tests
  - Both count filtered reads and write calls (`CreateCalls` / `UpdateCalls` / `DeleteCalls`, one per call whatever its size); a collection create is all or nothing, every `BeforeCreate` running first, as a SQL store's is;
    `BeforeDelete` fails a delete; `MissingOnUpdate` makes a collection update report items as not stored (TASK-555)

## Dependencies
- Birko.Data.Localization (projitems)
- Birko.Data.Core (projitems)
- Birko.Data.Stores (projitems)
- xUnit 2.9.3, FluentAssertions 7.0.0
- Microsoft.Extensions.DependencyInjection 10.* (the DI test; the project under test does not declare it)

## Maintenance
- When adding new features to Birko.Data.Localization, add corresponding tests here
- Follow existing patterns: in-memory stores, TestEntityLocalizationContext for culture switching
