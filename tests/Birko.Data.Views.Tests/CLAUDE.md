# Birko.Data.Views.Tests

## Overview
Unit tests for Birko.Data.Views — view definition builder, view map registry, and view result types.

## Project Location
`tests/Birko.Data.Views.Tests/` (.csproj, xUnit + FluentAssertions)

## Components
- **TestModels.cs** — Shared test source entities and view models
- **ViewDefinitionBuilderTests.cs** — ViewDefinitionBuilder fluent API tests (column mapping, joins, filters, ordering, grouping)
- **ViewMapRegistryTests.cs** — ViewMapRegistry registration and lookup tests
- **ViewResultTests.cs** — ViewResult type tests

## Dependencies
- Birko.Data.Core (shared project import)
- Birko.Data.Stores (shared project import)
- Birko.Data.Views (shared project import)
- xUnit 2.9.3, FluentAssertions 7.0.0
