# Birko.Data.Migrations.RavenDB.Tests

## Overview
Unit tests for Birko.Data.Migrations.RavenDB — the RavenDB platform provider for the
platform-agnostic migration framework.

## Project Location
`tests/Birko.Data.Migrations.RavenDB.Tests/`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- **Compile guard.** Building this project against the shared `Birko.Data.Migrations.RavenDB`
  projitems compile-verifies the LINQ usings that were missing (CR-C10 `RavenDBDataMigrator`,
  CR-C11 `RavenMigrationRunner`) — without them the shared project would not build here.
- **No live server.** `CountDocuments` (CR-C12) and the other query/update paths require a running
  RavenDB instance and are intentionally not exercised. `RavenDBDataMigrator.CopyData` (CR-C13) now
  throws `NotSupportedException` before opening any session, so it is testable offline.
- Brings `RavenDB.Client` transitively via the base `Birko.Data.RavenDB` projitems.
