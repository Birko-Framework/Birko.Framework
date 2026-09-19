# Birko.Data.Migrations.MongoDB.Tests

## Overview
Tests for Birko.Data.Migrations.MongoDB — the MongoDB provider for the platform-agnostic migrations.

## Project Location
`tests/Birko.Data.Migrations.MongoDB.Tests/`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- **Compile guard.** Building this project verifies the CR-C09 session-threading edits compile
  against the real MongoDB.Driver overloads (schema builder + data migrator now pass an optional
  IClientSessionHandle to every operation so migrations join the runner transaction).
- **No live server.** Transactional behavior needs a MongoDB replica set and is not exercised.
- Brings MongoDB.Driver transitively via the base Birko.Data.MongoDB projitems.
