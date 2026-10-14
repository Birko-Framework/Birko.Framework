# Birko.Security.Grants.SQL.Tests

## Overview
xUnit + FluentAssertions tests for `Birko.Security.Grants.SQL`, against a real SQLite file per test. Kept apart from
`Birko.Security.Grants.Tests` so that suite stays free of the SQL stack and the native SQLite library.

## Project Location
`tests/Birko.Security.Grants.SQL.Tests/`

## Structure
```
Birko.Security.Grants.SQL.Tests/
├── SchemaTests.cs            — SqliteGrantsFixture + schema (every property a column, NOT NULL), round trips (nulls included),
│                               UNIQUE token hash, EnsureAsync's indexes (PRAGMA index_info) + idempotence + refusal,
│                               GrantService end to end on the SQL stores                                    (TASK-594)
├── LookupParityTests.cs      — every IResourceGrantStore lookup: SQLite rows == InMemory rows over a user × type ×
│                               resource × tenant grid, nulls included; tenant isolation on SQL               (TASK-593)
├── InvitationClaimTests.cs   — the claim across two instances (separate KeyedLocks): two users / one email, a stale
│                               redemption after a completed one (gated, deterministic), revoke vs redeem     (TASK-593)
└── PropertyUpdatePathTests.cs — values the services write via PropertyUpdate read back like entity-written ones
                                (ticks + Kind), released claims write nulls                                    (TASK-593)
```

## Test Patterns
- **The mapping is applied once per process** (a static gate). `ApplyToDatabase` writes process-wide table
  registrations, and applying it from every test in parallel would race.
- Each fixture has its own directory and database file, and in teardown clears **only that database's** connection
  pool. A process-wide `ClearAllPools` breaks parallel siblings (rule 61).
- Index columns are read through `PRAGMA index_info`, never parsed from the index SQL.
- **Lookups are called through `IResourceGrantStore`.** They are default interface methods, unreachable on the
  concrete store type.
- **The claim's race test is not the proof; the gated test is.** With the claim's `WHERE RedeemedAt IS NULL`
  dropped, `TwoUsersWithOneEmail…` failed in only 1 of 3 runs, because the re-read alone usually still leaves one
  winner. `AStaleRedemption…` parks one redemption's read on a gate while another completes, and fails 3/3 without
  the condition (measured 2026-10-14).
- Proven to fail (2026-10-14):
  - removing the Email index fails the index test;
  - removing `IsUnique()` from `TokenHash` fails the uniqueness test;
  - a row seeded into only one store fails every affected parity lookup.
