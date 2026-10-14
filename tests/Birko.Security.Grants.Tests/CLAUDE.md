# Birko.Security.Grants.Tests

## Overview
xUnit + FluentAssertions tests for `Birko.Security.Grants`. The shared project is compiled into this assembly, so
`internal` types (`KeyedLock`, `GrantPredicates`, the internal `GrantService` constructor) are reachable.

## Project Location
`tests/Birko.Security.Grants.Tests/`

## Structure
```
Birko.Security.Grants.Tests/
├── GrantsFixture.cs           — InMemoryResourceGrantStore (with CountDelay), default role table, TestDateTimeProvider, seeding helpers
├── GrantRoleTableTests.cs     — every Build() refusal, undeclared lookups refuse, immutability
├── CanAsyncTests.cs           — exact vs type-wide, null id, action union, admin grant / admin source, undeclared names
├── GrantAndRevokeTests.cs     — share / escalation rules both ways, re-grant replaces, self-revoke, owner-on-create
├── LastOwnerTests.cs          — last-owner refusal, concurrent revokes (shared lock, and no shared lock), KeyedLock cleanup
├── TenantScopeTests.cs        — tenant-bound vs untenanted grants, stamping, per-tenant last owner, listing
└── ListingTests.cs            — ListResources (AllOfType + Ids), ListGrantees (IsTypeWide)
```

## Test Patterns
- A fresh `GrantsFixture` per test class instance; its service gets a **private** `KeyedLock` so parallel test classes
  never serialise against each other. Tests of the shared lock construct services with the public constructor.
- `SeedAsync` writes grants directly, bypassing the service's rules; `SeedAdminAsync` writes the reserved `*` grant.
- **Race tests set `Store.CountDelay`.** The in-memory store completes synchronously, so without the delay two
  "concurrent" revokes never interleave and the test passes with the lock removed (measured 2026-10-14). With 50 ms
  both race tests fail against a lock-less `RevokeAsync`, and the no-shared-lock test fails with the post-write
  recount removed.
