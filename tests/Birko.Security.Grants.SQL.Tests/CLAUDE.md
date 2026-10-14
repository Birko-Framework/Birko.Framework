# Birko.Security.Grants.SQL.Tests

## Overview
xUnit + FluentAssertions tests for `Birko.Security.Grants.SQL`, against a real SQLite file per test. Kept apart from
`Birko.Security.Grants.Tests` so that suite stays free of the SQL stack and the native SQLite library.

## Project Location
`tests/Birko.Security.Grants.SQL.Tests/`

## Structure
```
Birko.Security.Grants.SQL.Tests/
└── SchemaTests.cs   — SqliteGrantsFixture + schema (every property a column, NOT NULL), round trips (nulls included),
                        UNIQUE token hash, EnsureAsync's indexes (PRAGMA index_info) + idempotence + refusal,
                        GrantService end to end on the SQL stores
```

## Test Patterns
- **The mapping is applied once per process** (a static gate). `ApplyToDatabase` writes process-wide table
  registrations, and applying it from every test in parallel would race.
- Each fixture has its own directory and database file, and in teardown clears **only that database's** connection
  pool. A process-wide `ClearAllPools` breaks parallel siblings (rule 61).
- Index columns are read through `PRAGMA index_info`, never parsed from the index SQL.
- Proven to fail: removing the Email index fails the index test, and removing `IsUnique()` from `TokenHash` fails
  the uniqueness test (2026-10-14).
