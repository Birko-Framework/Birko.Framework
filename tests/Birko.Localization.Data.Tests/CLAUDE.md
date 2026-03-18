# Birko.Localization.Data.Tests

## Overview
Unit tests for the Birko.Localization.Data project.

## Project Location
- **Path:** `C:\Source\Birko.Localization.Data.Tests\`
- **Type:** Test Project (.csproj, net10.0)
- **Framework:** xUnit 2.9.3, FluentAssertions 7.0.0

## Test Classes
- **DatabaseTranslationProviderTests** — CRUD, culture lookup, async methods, constructor validation
- **TranslationModelTests** — CopyTo, default values, property mapping
- **TranslationFilterTests** — ByCulture, ByKeyAndCulture, ByNamespaceAndCulture, no-filter
- **NamespaceScopingTests** — Namespace isolation, different namespaces same key, no-namespace returns all
- **CacheTests** — Cache hit, invalidation (single/all), no-cache mode

## Test Infrastructure
- **TestTranslationStore** — In-memory `AbstractAsyncBulkStore<TranslationModel>` implementation for testing

## Running Tests
```bash
dotnet test Birko.Localization.Data.Tests/
```
