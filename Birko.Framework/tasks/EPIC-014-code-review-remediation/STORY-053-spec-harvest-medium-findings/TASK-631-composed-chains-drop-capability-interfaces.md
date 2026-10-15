---
id: TASK-631
parent: STORY-053
feature: FEATURE-014
status: done
priority: P2
assignee: ai
created: 2026-10-15
depends-on: []
blocks: []
findings: [SH-M336]
pr: null
github-issue: null
jira-key: null
---

# A composed decorator chain drops every capability outside `IAsyncBulkStore<T>`

## Context

Spawned by [[TASK-159]] (2026-10-15), which confirmed SH-M336 by reading and left it unfixed because the fix is a design
decision across every decorator, not a local edit.

`StoreWrapperBuilder.Build` takes and returns `IAsyncBulkStore<T>`, and each decorator implements only that surface.
A raw store that also implements `IAsyncAggregatableStore<T>`, a transactional interface or `ISettingsStore` loses them
once wrapped: `wrapped as IAsyncAggregatableStore<T>` is null. The caller's way out is `GetInnerStore()`, and an
operation sent there bypasses the soft-delete exclusion and the tenant scope of every decorator above it, silently.
Symbio builds its stores through the builder (`RepositoryBase.cs:36`).

## What to decide

1. **Forward** each capability through every decorator, applying the decorator's own scope (an aggregate through the
   soft-delete wrapper must exclude deleted rows; through the tenant wrapper, other tenants). That is one translation
   per capability per decorator, and a decorator that cannot scope a capability must refuse it, not forward it raw
   (rule 41).
2. Or **refuse**: make `GetInnerStore()` access to a scoped capability visible (a documented escape hatch), and say on
   the builder which capabilities the chain does not carry.

## Acceptance criteria

- [x] An aggregate through a composed chain either honours every decorator's scope or is refused, measured with a
      soft-deleted row and a second tenant's row present
- [x] The same answer for transactions and settings
- [x] Red on the current code

## Decisions (2026-10-15, the user)

Forward with scope: each decorator forwards aggregates with its own scope, and transactions and settings, which no
decorator scopes, reach the raw store; a decorator that cannot scope a capability refuses it.

## Resolution (2026-10-15)

- **Aggregation:** the seven decorators the builder composes (EventSourcing, Timestamp, Audit, Tenant, SoftDelete,
  Sluggable, Default) implement `IAsyncAggregatableStore<T>` through one helper, `StoreChain.AggregateThrough`, which
  refuses an inner store that does not aggregate. SoftDelete narrows the query's filter with `SoftDeleteFilter.CombineWithNotDeleted`
  and Tenant with its own `TenantFilter` (the seam every read uses, so Strict mode and the all-tenants scope behave as
  for reads); the other five do not scope reads and pass the query on. New `AggregateQuery<T>.WithFilter`.
- **Transaction context and settings:** `StoreChain.SetTransactionContext<T, TContext>` and `StoreChain.SetSettings<TSettings>`
  walk `IStoreWrapper.GetInnerStore()` to the store that takes them, and throw when none does. They are not forwarded
  per decorator because neither is scoped and both are generic in a type a decorator cannot name; the decorators'
  operations all reach the raw store anyway. SQL needs neither (its unit of work is ambient through the connector).
- Not changed: the localization decorator is not in the builder's chain and does not implement aggregation, so it
  refuses by absence.

**Measured** on the in-memory store through `StoreWrapperBuilder.Build` with three rows of tenant A (one soft-deleted)
and one of tenant B: the sum is 3 and the count 2 (tenant A's live rows); a caller's filter is kept and narrowed; a
Strict chain with no tenant refuses; a transaction context and settings arrive at the raw store, and an unknown context
type throws. **Red:** with the decorators reverted, the three aggregate tests fail (the chain was not aggregatable); the
transaction / settings test uses only the new `StoreChain` API, so it has no red form.

**Regression:** the 115 suites importing Birko.Data.Stores (6,566 tests), live on every provider (SQL four + TimescaleDB, MongoDB, RavenDB, ElasticSearch, InfluxDB, Cosmos emulator), green.

## Human test plan

N/A: a library contract, asserted by tests.
