# Birko.Data.SQL.ViewModel.Tests

## Overview
Unit tests for Birko.Data.SQL.ViewModel — the SQL-backed view-model repositories.

## Project Location
`tests/Birko.Data.SQL.ViewModel.Tests/`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- `AsyncDataBaseRepositoryTests` — regression for CR-C17: the async repo is generic over TConnector,
  so a concrete `AsyncSQLiteStore` (`AsyncDataBaseBulkStore<SqLiteConnector, T>`) is accepted by the
  constructor and resolved by `DataBaseStore` (both previously failed due to the hard-coded
  `AbstractConnector` generic argument). Uses a real `AsyncSQLiteStore` for the type check (no DB
  connection is opened).
- References `Microsoft.Data.Sqlite` (SqLite connector) and the `Microsoft.AspNetCore.App` framework
  (Birko.Data.Tenant middleware).
