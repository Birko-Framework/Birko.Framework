# Store Composition Guide

## Overview

`Birko.Data.Composition` wraps a raw store with the right decorators **at runtime** based on what interfaces the entity type implements. Individual decorators (Tenant, Default, SoftDelete, Sluggable, Audit, Timestamp) live in `Birko.Data.Patterns` and `Birko.Data.Tenant`, each with a hard compile-time constraint like `where T : ISoftDeletable`. `StoreWrapperBuilder.Build<T>()` inspects `T` via reflection and applies only the decorators whose constraints `T` actually satisfies, producing a correctly ordered decorator chain.

This lets you have one composition helper that works for any entity, without writing `if (entity is ISoftDeletable) Wrap(...)` branches yourself.

## The problem

Decorators have compile-time constraints:

```csharp
public class AsyncSoftDeleteBulkStoreWrapper<TStore, T> : ...
    where T : AbstractModel, ISoftDeletable, new() { }

public class AsyncAuditBulkStoreWrapper<TStore, T> : ...
    where T : AbstractModel, IAuditable, new() { }
```

A naive `Build<T>()` with `where T : AbstractModel` cannot instantiate `AsyncSoftDeleteBulkStoreWrapper<..., T>` — the compiler rejects it because `T` is not constrained to `ISoftDeletable`. Composition solves this by closing the wrapper's generic type at runtime via reflection.

## Public API

### `StoreWrapperBuilder.Build<T>()`

```csharp
public static IAsyncBulkStore<T> Build<T>(
    IAsyncBulkStore<T> rawStore,
    IDateTimeProvider? clock = null,
    IAuditContext? auditContext = null,
    ITenantContext? tenantContext = null)
    where T : AbstractModel, new()
```

Takes the raw store plus optional contexts, returns a wrapped store. Missing contexts skip the decorators that need them (e.g. no `auditContext` → no Audit wrapper even if `T : IAuditable`).

## Decorator order (outermost → innermost)

```
┌─────────────────────────────────────┐
│ 1. Tenant (T : ITenant)              │  ← outermost, filters first
├─────────────────────────────────────┤
│ 2. Default (T : IDefault)            │
├─────────────────────────────────────┤
│ 3. SoftDelete (T : ISoftDeletable)   │
├─────────────────────────────────────┤
│ 4. Sluggable (T : ISluggable)        │
├─────────────────────────────────────┤
│ 5. Audit (T : IAuditable)            │
├─────────────────────────────────────┤
│ 6. Timestamp (T : ITimestamped)      │  ← innermost, closest to raw store
├─────────────────────────────────────┤
│ 7. Raw store                         │
└─────────────────────────────────────┘
```

**Why this order matters:**

- **Tenant first** — filters tenant-foreign rows before anything else sees them
- **SoftDelete above Sluggable** — slug uniqueness is checked among non-deleted rows only
- **Audit/Timestamp innermost** — set CreatedBy/UpdatedAt *after* all other validation passes so rejected writes don't get audit entries

## Usage

```csharp
using Birko.Data.Composition;

// Raw store (e.g. from Birko.Data.SQL)
IAsyncBulkStore<Product> rawStore = new AsyncDataBaseBulkStore<Product>(settings);

// Wrap with all applicable decorators
IAsyncBulkStore<Product> decorated = StoreWrapperBuilder.Build(
    rawStore,
    clock: new SystemDateTimeProvider(),
    auditContext: new AuditContext(currentUser),
    tenantContext: new TenantContext(tenantGuid));

// decorated is now:
//  Tenant → Default → SoftDelete → Sluggable → Audit → Timestamp → rawStore
// (assuming Product implements all those interfaces)
```

If `Product` only implements `ITimestamped` and `ISoftDeletable`, the returned chain is just `SoftDelete → Timestamp → rawStore` — the others are skipped.

## What interfaces map to which decorators

| Interface | Decorator | Required context |
|---|---|---|
| `ITenant` | `AsyncTenantBulkStoreWrapper<>` | `ITenantContext` |
| `IDefault` | `AsyncDefaultStoreWrapper<>` | — |
| `ISoftDeletable` | `AsyncSoftDeleteBulkStoreWrapper<>` | `IDateTimeProvider` |
| `ISluggable` | `AsyncSluggableBulkStoreWrapper<>` | — |
| `IAuditable` | `AsyncAuditBulkStoreWrapper<>` | `IAuditContext` |
| `ITimestamped` | `AsyncTimestampBulkStoreWrapper<>` | `IDateTimeProvider` |

## Typical use cases

1. **SaaS entities** implementing `ITenant + IAuditable + ITimestamped` — one `Build<>` call produces the full safety stack.
2. **Catalog entities** implementing `ISluggable + ISoftDeletable + ITimestamped` — slug uniqueness respects deleted state automatically.
3. **Settings entities** implementing `IDefault` — "exactly one default" is enforced on write without hand-rolled logic.
4. **Generic repository factories** — a DI registration that wraps any entity's store automatically, without per-entity code.

## Dependencies

- **Birko.Data.Patterns** — Timestamp, Audit, SoftDelete, Sluggable, Default wrappers + marker interfaces
- **Birko.Data.Tenant** — Tenant wrapper + `ITenantContext`, `ITenant`
- **Birko.Data.Stores** — `IAsyncBulkStore<T>`
- **Birko.Data.Core** — `AbstractModel`
- **Birko.Time.Abstractions** — `IDateTimeProvider`

## Design notes

- **No wrappers = no-op** — If `T` implements none of the marker interfaces, `Build<T>()` returns the raw store unchanged. Zero overhead.
- **Missing context = wrapper skipped** — `auditContext: null` skips the Audit wrapper even if `T : IAuditable`. This lets you opt out without changing the entity.
- **Single-direction composition** — Returns `IAsyncBulkStore<T>` only. Sync composition is not provided; the async path is the recommended default.
- **Reflection cost is one-shot** — The wrapping happens once at store construction, not per CRUD call. Runtime overhead of CRUD operations is only the decorator chain itself.

## See also

- [Data Patterns Guide](patterns.md) — individual decorators (SoftDelete, Audit, Timestamp, Sluggable, Default)
- [Multi-Tenancy Guide](tenant.md) — `ITenant`, `ITenantContext`, tenant filtering
- [Store Implementation Guide](store-implementation.md) — writing custom stores that integrate cleanly with these decorators
