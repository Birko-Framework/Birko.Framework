# Birko.Data.CosmosDB.Tests

## Overview
Unit tests for Birko.Data.CosmosDB — stores, repositories, UnitOfWork, and index management.

## Project Location
`C:\Source\Birko.Data.CosmosDB.Tests\`

## Test Framework
xUnit + FluentAssertions + Moq

## Dependencies
- Birko.Data.CosmosDB (via .projitems import)
- Birko.Data.Core, Birko.Data.Stores, Birko.Data.Repositories
- Birko.Data.Patterns (UnitOfWork, IndexManagement)

## Structure
- `CosmosDBStoreTests.cs` — Sync store tests
- `AsyncCosmosDBStoreTests.cs` — Async store tests
