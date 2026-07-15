# Birko.Data.Tenant.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.Data.Tenant` (store wrappers, filters, context).

## Scope

- `TenantContextTests` — `TenantContext` set/clear/has-tenant state transitions.
- `ModelByTenantAndWrapperTests` — the `ModelByTenant<T>` filter composition and the wrappers'
  read-path tenant filtering.
- `TenantBulkAndSaveTests` — CR-M173 (bulk wrappers materialize a lazy source exactly once, so the
  authorized set equals the persisted set) and CR-M174 (`SaveAsync`'s create branch returns the
  inner `CreateAsync` result instead of relying on `data.Guid` write-back).
- `TenantFailOpenTests` — CR-L229: the deliberate fail-open contract of `BelongsToCurrentTenant`.
  No tenant set ⇒ single + bulk Update/Delete really write across tenants (seeded via the inner
  store, mutation/removal asserted by read-back, not just NotThrow); tenant set ⇒
  `UnauthorizedAccessException` for foreign items (one foreign item poisons a bulk batch); and the
  documented fail-closed escape hatch — `BelongsToCurrentTenant` is `protected virtual`, and an
  override dispatches from both the single-item and the bulk `items.All(...)` call sites.

## Conventions

- Regular `Microsoft.NET.Sdk` csproj (`net10.0`, implicit usings, nullable). Imports the core
  projitems chain + `Birko.Data.InMemory` (inner-store test double) + `Birko.Data.Tenant`.
- Each test file declares its own private `Doc : AbstractModel, ITenant` model — the local idiom;
  there is no shared test model.
- Offline only; the ASP.NET middleware (`TenantMiddleware`) resolution paths need a host and are
  not covered here.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
