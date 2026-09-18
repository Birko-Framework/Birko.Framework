# Birko.Data.CosmosDB.ViewModel.Tests

## Overview
Unit tests for Birko.Data.CosmosDB.ViewModel — the ViewModel repositories over the CosmosDB store.

## Project Location
`C:\Source\Birko\Framework.Tests\Birko.Data.CosmosDB.ViewModel.Tests\`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- **No live Cosmos / emulator.** Only the offline-testable repository logic is covered.
- `CosmosDBRepositoryUnwrapTests` — CR-L108: the `AsyncCosmosDBRepository(IAsyncStore)` constructor's
  `IsStoreOfType` guard (accept a plain or wrapped `AsyncCosmosDBStore`, reject a foreign store), the
  unwrapping `CosmosStore` getter (a fake `IStoreWrapper` unwraps where the direct cast is null), and the
  null-store `IsHealthy` no-op. CRUD / aggregation / index-manager behavior stays emulator-tier.
