---
area: tenant-isolation
generated-at: 10f5611 (Birko.Data.Tenant; partial regen of the item-level write requirements for SH-H047)
generated-on: 2026-07-31
sources:
  - ../Birko.Data.Sync.Tenant/Models/ITenantSyncKnowledgeItem.cs
  - ../Birko.Data.Sync.Tenant/Models/TenantSyncKnowledgeItem.cs
  - ../Birko.Data.Sync.Tenant/Models/TenantSyncOptions.cs
  - ../Birko.Data.Sync.Tenant/Models/TenantSyncResult.cs
  - ../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs
  - ../Birko.Data.Sync.Tenant/TenantSyncQueue.cs
  - ../Birko.Data.Tenant/Filters/ModelByTenant.cs
  - ../Birko.Data.Tenant/Middleware/ServiceCollectionExtensions.cs
  - ../Birko.Data.Tenant/Middleware/TenantMiddleware.cs
  - ../Birko.Data.Tenant/Models/ITenant.cs
  - ../Birko.Data.Tenant/Models/ITenantContext.cs
  - ../Birko.Data.Tenant/Models/TenantContext.cs
  - ../Birko.Data.Tenant/Models/TenantIsolationMode.cs
  - ../Birko.Data.Tenant/Repositories/RepositoryServiceCollectionExtensions.cs
  - ../Birko.Data.Tenant/Stores/AsyncTenantBulkStoreWrapper.cs
  - ../Birko.Data.Tenant/Stores/AsyncTenantStoreWrapper.cs
  - ../Birko.Data.Tenant/Stores/TenantBulkStoreWrapper.cs
  - ../Birko.Data.Tenant/Stores/TenantMismatchException.cs
  - ../Birko.Data.Tenant/Stores/TenantScopeRequiredException.cs
  - ../Birko.Data.Tenant/Stores/TenantStoreExtensions.cs
  - ../Birko.Data.Tenant/Stores/TenantStoreWrapper.cs
  - ../Birko.EventBus.Tenant/Extensions/EventTenantScopeServiceCollectionExtensions.cs
  - ../Birko.EventBus.Tenant/TenantEventEnricher.cs
  - ../Birko.EventBus.Tenant/TenantEventScopeAccessor.cs
  - ../Birko.Security.AspNetCore/Extensions/TenantHeaderGuardExtensions.cs
  - ../Birko.Security.AspNetCore/Tenant/HeaderTenantResolver.cs
  - ../Birko.Security.AspNetCore/Tenant/ITenantResolver.cs
  - ../Birko.Security.AspNetCore/Tenant/SubdomainTenantResolver.cs
  - ../Birko.Security.AspNetCore/Tenant/TenantContextAdapter.cs
  - ../Birko.Security.AspNetCore/Tenant/TenantHeaderClaimGuardMiddleware.cs
  - ../Birko.Security.AspNetCore/Tenant/TenantMiddleware.cs
shaped-by: []
---

# Multi-tenant isolation across stores, HTTP and events

## Purpose

This capability keeps one tenant's data out of another tenant's reach. A tenant identity is carried
in an ambient, per-async-flow context; store wrappers then compose that identity into every read as
a predicate and check it on every write. Around that core sit the entry points that populate the
context — two independent ASP.NET Core middleware families (one in `Birko.Data.Tenant`, one in
`Birko.Security.AspNetCore`), a guard that refuses a tenant header disagreeing with the caller's
token, an event-bus bridge that captures the publishing tenant and restores it before background
dispatch, and a tenant-scoped data-sync provider. Anything that persists or reads a model
implementing `ITenant` depends on this capability; so does any host that wants a missing tenant to
be a `400` and a wrong tenant to be a `403` instead of both being a `500`.

## Requirements

### Requirement: Tenant-aware entity contract

The system SHALL treat an entity as tenant-aware when it implements `ITenant`, exposing a
non-nullable `Guid TenantGuid` and a nullable `string? TenantName`, both read/write.

#### Scenario: Store wrapper generic constraint

- **Given** a model type `T`
- **When** `TenantStoreWrapper<TStore, T>` or `AsyncTenantStoreWrapper<TStore, T>` is closed over `T`
- **Then** `T` must satisfy `where T : Birko.Data.Models.AbstractModel, ITenant`, so a model without both a Guid identity and a `TenantGuid` cannot be wrapped

#### Scenario: Sync knowledge item is tenant-aware by composition

- **Given** `ITenantSyncKnowledgeItem`
- **When** its declaration is inspected
- **Then** it adds no members of its own and derives its shape entirely from `ISyncKnowledgeItem` plus `ITenant`, and `TenantSyncKnowledgeItem` implements it with `TenantGuid` as a non-nullable `Guid`

### Requirement: Ambient tenant context backed by AsyncLocal

The system SHALL store the current tenant id, tenant name and all-tenants flag in
`AsyncLocal<T>` fields on a `TenantContext` instance, so the values flow with the async execution
context and are per-instance rather than per-process.

#### Scenario: Setting and reading the tenant

- **Given** a fresh `TenantContext`
- **When** `SetTenant(t, "Acme")` is called
- **Then** `CurrentTenantGuid` returns `t`, `CurrentTenantName` returns `"Acme"`, and `HasTenant` returns true

#### Scenario: Clearing the tenant

- **Given** a `TenantContext` with a tenant set
- **When** `ClearTenant()` is called
- **Then** both `_currentTenantGuid` and `_currentTenantName` are set to null, so `HasTenant` returns false and `CurrentTenantName` returns null

#### Scenario: Two context instances do not share state

- **Given** two separate `TenantContext` instances A and B
- **When** `SetTenant(t)` is called on A
- **Then** B still reports `HasTenant == false`, because each instance owns its own `AsyncLocal` fields

#### Scenario: Guid.Empty is a settable tenant

- **Given** a fresh `TenantContext`
- **When** `SetTenant(Guid.Empty)` is called
- **Then** `HasTenant` returns true and `CurrentTenantGuid` returns `Guid.Empty` — the value is not treated as "unset"

### Requirement: Scoped tenant execution restores the previous tenant

The system SHALL, in every `WithTenant` / `WithTenantAsync` overload, capture the previous tenant
guid and name, set the requested tenant, run the delegate, and restore the captured values in a
`finally` block — restoring even when the delegate throws.

#### Scenario: Nested scopes unwind to the outer tenant

- **Given** `SetTenant(outer)` has been called
- **When** `WithTenant(inner, null, () => { … })` completes
- **Then** during the delegate `CurrentTenantGuid` is `inner`, and after it returns `CurrentTenantGuid` is `outer` again

#### Scenario: A throwing delegate still restores

- **Given** `SetTenant(outer)` has been called
- **When** `WithTenantAsync(inner, null, () => throw new Exception())` is awaited and the exception propagates
- **Then** `CurrentTenantGuid` is `outer` and `CurrentTenantName` is the outer name

#### Scenario: Scope entered with no prior tenant

- **Given** a `TenantContext` with no tenant set
- **When** `WithTenant(t, "n", action)` returns
- **Then** `HasTenant` is false again, because the captured previous guid was null

### Requirement: Explicit all-tenants (admin) scope

The system SHALL expose an `IsAllTenantsScope` flag and four `WithAllTenants` / `WithAllTenantsAsync`
overloads; `TenantContext` sets an `AsyncLocal<bool>` to true for the duration of the delegate and
restores the previous value in a `finally` block.

#### Scenario: Flag is true only inside the scope

- **Given** a `TenantContext` where `IsAllTenantsScope` is false
- **When** `WithAllTenants(() => { … })` runs
- **Then** `IsAllTenantsScope` is true inside the delegate and false again after it returns

#### Scenario: Nested all-tenants scopes restore the previous value, not false

- **Given** code already inside `WithAllTenants`
- **When** an inner `WithAllTenants` completes
- **Then** `IsAllTenantsScope` is still true, because the inner scope restored the captured `previous` value

#### Scenario: All-tenants scope is orthogonal to the tenant value

- **Given** `SetTenant(t)` has been called
- **When** `WithAllTenants(...)` is entered
- **Then** `HasTenant` remains true and `CurrentTenantGuid` remains `t`; only `IsAllTenantsScope` changes

### Requirement: Custom ITenantContext implementations fail closed for admin scope

The system SHALL default `ITenantContext.IsAllTenantsScope` to `false` and default every
`WithAllTenants` / `WithAllTenantsAsync` overload to invoking the delegate **without** establishing
any scope, so an implementation that does not override them never grants cross-tenant access.

#### Scenario: A hand-rolled context does not open admin scope

- **Given** a custom `ITenantContext` implementation that overrides nothing but the tenant members, with no tenant set
- **When** a `TenantIsolationMode.Strict` store read is performed inside `WithAllTenants(...)`
- **Then** `IsAllTenantsScope` is still false and the read throws `TenantScopeRequiredException`

### Requirement: Process-wide static tenant context for non-DI use

The system SHALL expose a `static class Tenant` holding one process-wide `TenantContext` in a
`private static readonly` field, surfaced as `Tenant.Current`, with `Set`, `Clear`, `Id`, `Name` and
`IsSet` shortcuts delegating to it.

#### Scenario: Static shortcuts delegate to the singleton

- **Given** nothing has set a tenant
- **When** `Tenant.Set(t, "Acme")` is called
- **Then** `Tenant.Id` returns `t`, `Tenant.Name` returns `"Acme"`, `Tenant.IsSet` is true, and `Tenant.Current.CurrentTenantGuid` returns `t`

#### Scenario: Wrappers silently fall back to the static singleton

- **Given** a `TenantStoreWrapper` constructed with `tenantContext: null`
- **When** it evaluates the tenant
- **Then** it uses `Models.Tenant.Current` — a different instance from any DI-scoped `ITenantContext` — with no warning or exception, so in a DI app the middleware's tenant is invisible to that wrapper and `Permissive` reads return every tenant's rows

### Requirement: Tenant isolation modes

The system SHALL define `TenantIsolationMode` with exactly two members — `Permissive = 0` (the
default in every wrapper constructor, extension and DI helper) and `Strict = 1` — and SHALL branch
on `_mode != TenantIsolationMode.Strict` / `_mode == TenantIsolationMode.Strict` at the guard sites.

#### Scenario: Default is Permissive

- **Given** `store.AsTenantAware()` called with no `mode` argument
- **When** the wrapper is constructed
- **Then** `_mode` is `TenantIsolationMode.Permissive`, so a no-tenant read returns all tenants' rows and a no-tenant create stamps `Guid.Empty`

#### Scenario: Strict is opt-in per wrapper

- **Given** `store.AsTenantAware(ctx, TenantIsolationMode.Strict)`
- **When** any read, count, filter-write or create runs with no tenant and no all-tenants scope
- **Then** the operation throws instead of operating across tenants

### Requirement: Tenant read predicate composition

The system SHALL compose the tenant predicate through `ModelByTenant<TModel>.Filter()`, which
returns `BaseFilter && (x => x.TenantGuid == TenantGuid)` via
`ExpressionParameterReplacer.AndAlso` when `TenantGuid != null`, and returns `BaseFilter` unchanged
when `TenantGuid == null`.

#### Scenario: Caller filter is conjoined with the tenant predicate

- **Given** `new ModelByTenant<M>(t, x => x.Name == "a")`
- **When** `Filter()` is called
- **Then** the result is a single expression matching rows where `Name == "a"` AND `TenantGuid == t`

#### Scenario: Null tenant yields the caller's filter verbatim

- **Given** `new ModelByTenant<M>(null, x => x.Name == "a")`
- **When** `Filter()` is called
- **Then** the returned expression is `BaseFilter` with no tenant term, so the read spans every tenant

#### Scenario: Null tenant and null base filter yields null

- **Given** `new ModelByTenant<M>(null, null)`
- **When** `Filter()` is called
- **Then** it returns null, which downstream stores interpret as read-everything

#### Scenario: Guid.Empty is filtered on, not short-circuited

- **Given** `new ModelByTenant<M>(Guid.Empty, null)`
- **When** `Filter()` is called
- **Then** the result is `x => x.TenantGuid == Guid.Empty`, matching only rows whose tenant is the zero guid, and no exception is thrown

### Requirement: Reads and counts flow through a single overridable tenant seam

The system SHALL route every read and count in all four wrappers through
`protected virtual IFilter<T> TenantFilter(Expression<Func<T, bool>>? filter)`, which calls
`EnsureTenantForStrict()` and then constructs `ModelByTenant<T>(effectiveTenant, filter)`.

#### Scenario: Read by guid is tenant-scoped

- **Given** a wrapper with tenant `t` and an entity with guid `g` belonging to tenant `u`
- **When** `Read(g)` (or `ReadAsync(g)`) is called
- **Then** the composed filter is `Guid == g && TenantGuid == t`, so the inner store returns null

#### Scenario: Count is scoped identically to read

- **Given** a wrapper with tenant `t`
- **When** `Count(x => x.Active)` is called
- **Then** the inner store receives `Active && TenantGuid == t`

#### Scenario: Bulk read with paging keeps ordering and paging arguments

- **Given** `TenantBulkStoreWrapper` with tenant `t`
- **When** `Read(filter, orderBy, limit: 10, offset: 20)` is called
- **Then** the inner store receives the tenant-composed filter and the unchanged `orderBy`, `limit` and `offset`

#### Scenario: Overriding the seam changes every read path at once

- **Given** a subclass overriding `TenantFilter`
- **When** any of `Read`, `ReadOne`, `Count`, bulk `Read`, `Update(filter, …)` or `Delete(filter)` is called
- **Then** the override is what supplies the filter, because no path builds `ModelByTenant` itself

### Requirement: All-tenants scope widens reads even when a tenant is set

The system SHALL compute the effective read tenant as
`_tenantContext.IsAllTenantsScope ? (Guid?)null : _tenantContext.CurrentTenantGuid`, so an active
all-tenants scope suppresses the tenant predicate regardless of the ambient tenant value.

#### Scenario: Admin scope inside a tenant scope reads across tenants

- **Given** `SetTenant(t)` and code inside `WithAllTenants(...)`
- **When** `Read(x => x.Active)` is called
- **Then** the effective tenant is null and the inner store receives only `Active`, returning rows from every tenant

#### Scenario: Filter-based bulk writes widen the same way

- **Given** a bulk wrapper, `SetTenant(t)`, inside `WithAllTenants(...)`
- **When** `Delete(x => x.Obsolete)` is called
- **Then** the inner store receives `x => x.Obsolete` with no tenant term, deleting matching rows in every tenant

### Requirement: Strict mode refuses unscoped reads, counts and filter-writes

The system SHALL, in `EnsureTenantForStrict()`, throw `TenantScopeRequiredException` when
`_mode == Strict && !HasTenant && !IsAllTenantsScope`, with `Operation == "read"` and
`EntityType == typeof(T).Name`, and SHALL be a no-op otherwise.

#### Scenario: Strict read with no tenant throws

- **Given** a Strict wrapper over model `Invoice` with no tenant set and no all-tenants scope
- **When** `ReadAsync()` is called
- **Then** a `TenantScopeRequiredException` is thrown whose `Operation` is `"read"`, `EntityType` is `"Invoice"`, and message names Strict isolation and suggests `ITenantContext.WithAllTenants(...)`

#### Scenario: Filter-based bulk write reports operation "read"

- **Given** a Strict bulk wrapper with no tenant set
- **When** `Update(x => x.Active, propertyUpdate)` is called
- **Then** a `TenantScopeRequiredException` with `Operation == "read"` is thrown — the filter-write path shares the read seam and does not report its own operation name

#### Scenario: Permissive never throws from the seam

- **Given** a Permissive wrapper with no tenant set
- **When** `Count()` is called
- **Then** `EnsureTenantForStrict` does nothing and the inner store receives a null filter, counting every tenant's rows

#### Scenario: All-tenants scope satisfies Strict

- **Given** a Strict wrapper with no tenant set
- **When** `ReadAsync()` is called inside `WithAllTenantsAsync(...)`
- **Then** no exception is thrown and the read spans all tenants

### Requirement: Item-level write authorization

The system SHALL resolve the persisted row each item-level `Update` or `Delete` targets — via
`ReadStoredItems` / `ReadStoredItemsAsync`, which read by `Guid` **unscoped by tenant** — and SHALL
authorize the write with `EnsureWriteAuthorized`, throwing `TenantMismatchException` when it is
refused. When a row exists under the item's `Guid`, `BelongsToCurrentTenant` is applied to **that
stored row** and the caller-supplied `item.TenantGuid` is not consulted; when no row exists, it falls
back to `BelongsToCurrentTenant(item)` as a payload-consistency check. With a tenant set the check is
`subject.TenantGuid == CurrentTenantGuid`; with no tenant set it returns
`IsAllTenantsScope || _mode != TenantIsolationMode.Strict`.

#### Scenario: Cross-tenant update is refused

- **Given** a wrapper with tenant `t` and an item whose `TenantGuid` is `u`
- **When** `Update(item)` is called
- **Then** a `TenantMismatchException` is thrown with `Operation == "update"`, `ExpectedTenantGuid == t`, `ActualTenantGuid == u`, and message `"Cannot update item: it does not belong to the current tenant"`

#### Scenario: Cross-tenant delete is refused

- **Given** a wrapper with tenant `t` and an item whose `TenantGuid` is `u`
- **When** `DeleteAsync(item)` is awaited
- **Then** a `TenantMismatchException` with `Operation == "delete"` is thrown and no delete is delegated — the inner store receives only the read that resolves the targeted row

#### Scenario: Permissive with no tenant allows any item

- **Given** a Permissive wrapper with no tenant set
- **When** `Update(item)` is called for an item belonging to tenant `u`
- **Then** the write is delegated to the inner store — cross-tenant writes are permitted in this configuration

#### Scenario: Strict with no tenant refuses any item

- **Given** a Strict wrapper with no tenant set and no all-tenants scope
- **When** `Update(item)` is called
- **Then** `BelongsToCurrentTenant` returns false and a `TenantMismatchException` is thrown with `ExpectedTenantGuid == null`

#### Scenario: All-tenants scope allows any item even under Strict

- **Given** a Strict wrapper with no tenant set, inside `WithAllTenants(...)`
- **When** `Update(item)` is called for any tenant's item
- **Then** the write is delegated to the inner store

#### Scenario: All-tenants scope does not widen item writes when a tenant is set

- **Given** a wrapper with tenant `t`, inside `WithAllTenants(...)`, and an item whose `TenantGuid` is `u`
- **When** `Update(item)` is called
- **Then** `BelongsToCurrentTenant` still compares `u` against `t` and a `TenantMismatchException` is thrown — the `IsAllTenantsScope` branch is reachable only when no tenant is set, so item-level writes do not widen the way reads do, contradicting the `TenantFilter` comment that the write guards "already special-case all-tenants scope"

#### Scenario: The guard reads the stored row, not the caller-supplied tenant

- **Given** a wrapper with tenant `t` and an item whose `Guid` identifies a row persisted under tenant `u`, with `item.TenantGuid` assigned `t` by the caller
- **When** `Update(item)` is called
- **Then** the wrapper reads the row back by `Guid`, applies `BelongsToCurrentTenant` to the persisted row, and throws `TenantMismatchException` with `ActualTenantGuid == u` — the row's real tenant, not the caller's claim; the write never reaches the inner store, which would otherwise key it on the primary field alone

#### Scenario: A payload tenant that disagrees with both the ambient tenant and the stored row is ignored

- **Given** a wrapper with tenant `t`, a row persisted under `t`, and `item.TenantGuid` assigned a third tenant `v` by the caller
- **When** `Update(item)` is called
- **Then** the write is authorized and delegated, because only the stored row's tenant is consulted — `item.TenantGuid` is not an input to the decision in either direction

#### Scenario: The stored-row read is deliberately not tenant-scoped

- **Given** a wrapper with tenant `t` resolving the row targeted by a write
- **When** `ReadStoredItems` issues the lookup
- **Then** it composes `ModelByGuid` alone with no tenant predicate, so another tenant's row is returned rather than filtered to null — a tenant-scoped read could not distinguish a foreign row from a missing one, and a missing row authorizes the write

#### Scenario: An update cannot re-home a row the caller owns

- **Given** a wrapper with tenant `t`, a row persisted under `t`, and `item.TenantGuid` assigned `u` by the caller
- **When** `Update(item)` is called
- **Then** `PreserveStoredTenant` restores `item.TenantGuid` and `item.TenantName` from the persisted row before delegation, so the row stays in `t` while the rest of the payload is applied

#### Scenario: Re-homing is permitted where cross-tenant writes are deliberate

- **Given** a wrapper with no tenant set, or one inside `WithAllTenants(...)`
- **When** `Update(item)` is called
- **Then** `PreserveStoredTenant` returns without touching the item, leaving the caller's per-item `TenantGuid` authoritative — matching `SetTenantGuidIfNeeded`'s behaviour on create in the same scopes

#### Scenario: A write targeting no persisted row falls back to the payload check

- **Given** a wrapper with tenant `t` and an item whose `Guid` matches no persisted row
- **When** `Update(item)` is called with `item.TenantGuid` set to `u`
- **Then** a `TenantMismatchException` is thrown — there is no stored row to authorize against, and the payload check remains so an upserting inner store cannot be made to create a row homed in another tenant

#### Scenario: A batch is authorized in full before any item is stamped

- **Given** a bulk wrapper with tenant `t` and a batch whose first item is owned by `t` and whose second targets a row owned by `u`
- **When** `Update(items)` is called
- **Then** every item is passed through `EnsureWriteAuthorized` before `PreserveStoredTenant` runs on any of them, so the refusal leaves no earlier item re-stamped and nothing is delegated to the inner store

#### Scenario: The bulk wrappers resolve a batch in one read

- **Given** a bulk wrapper and a batch of `n` items carrying persisted Guids
- **When** `ReadStoredItems` / `ReadStoredItemsAsync` is called
- **Then** the bulk override issues a single `ModelsByGuid` read covering the batch rather than the base class's read per item, and the base per-item path is not used

### Requirement: Create stamps the ambient tenant onto the item

The system SHALL call `SetTenantGuidIfNeeded(item)` before every create, which — when a tenant is
set, or when Permissive with no tenant — assigns `item.TenantGuid = CurrentTenantGuid ?? Guid.Empty`
and `item.TenantName = CurrentTenantName`, overwriting whatever the caller supplied.

#### Scenario: Ambient tenant overwrites a caller-supplied tenant

- **Given** a wrapper with tenant `t` and an item whose `TenantGuid` was pre-set to `u`
- **When** `Create(item)` is called
- **Then** `item.TenantGuid` is `t` and `item.TenantName` is the ambient tenant name before the inner store sees it

#### Scenario: Permissive with no tenant stamps Guid.Empty

- **Given** a Permissive wrapper with no tenant set
- **When** `CreateAsync(item)` is awaited
- **Then** `item.TenantGuid` is `Guid.Empty` and `item.TenantName` is null

#### Scenario: Strict with no tenant refuses to stamp

- **Given** a Strict wrapper over `Invoice` with no tenant set and no all-tenants scope
- **When** `Create(item)` is called
- **Then** a `TenantScopeRequiredException` is thrown with `Operation == "create"`, `EntityType == "Invoice"`, a message refusing to stamp `Guid.Empty`, and `item.TenantGuid` is left unmodified

#### Scenario: All-tenants scope leaves the caller's tenant untouched

- **Given** a wrapper with no tenant set, inside `WithAllTenants(...)`, and an item whose `TenantGuid` is `u`
- **When** `Create(item)` is called
- **Then** `SetTenantGuidIfNeeded` returns early, `item.TenantGuid` remains `u`, and `item.TenantName` is not overwritten

#### Scenario: Bulk create stamps each item lazily during enumeration

- **Given** a bulk wrapper with tenant `t` and a sequence of three items
- **When** `Create(items)` is called
- **Then** the inner store receives `data.Select(item => { SetTenantGuidIfNeeded(item); return item; })` — each item is stamped as the inner store enumerates, so a Strict no-tenant refusal surfaces only once the inner store starts enumerating

### Requirement: Bulk collection writes are authorized all-or-nothing over a single materialization

The system SHALL, in bulk `Update(IEnumerable<T>)` and `Delete(IEnumerable<T>)`, materialize the
source once as `data as IReadOnlyCollection<T> ?? data.ToList()`, resolve the batch's persisted rows
in a single `ModelsByGuid` read, and pass every item through `EnsureWriteAuthorized` — throwing
`TenantMismatchException` carrying the refused row's tenant on the first failure, before any item is
stamped or written.

#### Scenario: One foreign item rejects the whole batch

- **Given** a bulk wrapper with tenant `t` and a list of five items, one of which targets a row persisted under tenant `u`
- **When** `Update(items)` is called
- **Then** a `TenantMismatchException` with `Operation == "update"` and `ActualTenantGuid == u` is thrown and none of the five is written

#### Scenario: The authorized set equals the persisted set for a lazy source

- **Given** a lazily-generated, non-deterministic `IEnumerable<T>` passed to `DeleteAsync(data)`
- **When** the authorization check passes
- **Then** the inner store is handed the already-materialized `items` collection, not the original lazy source, so the second enumeration cannot yield a different set

#### Scenario: An already-materialized collection is not copied

- **Given** a `List<T>` (an `IReadOnlyCollection<T>`) passed to bulk `Delete`
- **When** the wrapper materializes it
- **Then** the same instance is used — no defensive copy is taken

### Requirement: Filter-based bulk Update and Delete compose the tenant predicate

The system SHALL pass `TenantFilter(filter).Filter()!` to the inner store for
`Update(filter, Action<T>)`, `Update(filter, PropertyUpdate<T>)` and `Delete(filter)` in both the
sync and async bulk wrappers.

#### Scenario: Filter-based delete cannot reach another tenant's rows

- **Given** a bulk wrapper with tenant `t`
- **When** `Delete(x => x.Obsolete)` is called
- **Then** the inner store receives `x => x.Obsolete && x.TenantGuid == t`

#### Scenario: PropertyUpdate path is scoped identically to the Action path

- **Given** a bulk wrapper with tenant `t`
- **When** `UpdateAsync(x => x.Active, new PropertyUpdate<T>(…))` is awaited
- **Then** the inner store receives the tenant-composed filter and the unmodified `PropertyUpdate<T>`

#### Scenario: Permissive with no tenant affects every tenant

- **Given** a Permissive bulk wrapper with no tenant set
- **When** `Delete(x => x.Obsolete)` is called
- **Then** the inner store receives `x => x.Obsolete` alone and rows are deleted across every tenant

### Requirement: Save routes to create or update by the item's guid

The system SHALL, in `Save` / `SaveAsync`, return `Guid.Empty` for a null `data`, call
`Create`/`CreateAsync` and return its result when `data.Guid` is null or `Guid.Empty`, and otherwise
call `Update`/`UpdateAsync` and return `data.Guid ?? Guid.Empty`.

#### Scenario: New item is created and its allocated guid returned

- **Given** an item whose `Guid` is null
- **When** `SaveAsync(item)` is awaited
- **Then** `CreateAsync` runs (stamping the tenant) and the guid returned by the inner store's `CreateAsync` is returned — not read back off `data`

#### Scenario: Existing item is updated and authorized

- **Given** an item whose `Guid` is a non-empty guid and whose `TenantGuid` is `u`, with the wrapper's tenant being `t`
- **When** `Save(item)` is called
- **Then** the update path runs, `BelongsToCurrentTenant` fails, and `TenantMismatchException` is thrown

#### Scenario: Null data is a silent no-op

- **Given** `data == null`
- **When** `Save(null)` is called
- **Then** `Guid.Empty` is returned, no tenant guard runs and the inner store is not touched

### Requirement: Wrapper passthrough and inner-store access

The system SHALL delegate `Init`/`InitAsync`, `Destroy`/`DestroyAsync` and `CreateInstance` straight
to the inner store with no tenant logic, expose the inner store via the explicit
`IStoreWrapper.GetInnerStore()` and via `GetInnerStoreAs<TInner>()` (an `as` cast), and SHALL throw
`ArgumentNullException` when constructed with a null inner store.

#### Scenario: Init is not tenant-guarded

- **Given** a Strict wrapper with no tenant set
- **When** `InitAsync()` is awaited
- **Then** the inner store's `InitAsync` runs and no tenant exception is thrown

#### Scenario: Unwrapping to an unrelated type yields null

- **Given** a wrapper over a `SqlStore<Invoice>`
- **When** `GetInnerStoreAs<IAsyncBulkStore<Invoice>>()` is called on a wrapper whose inner store does not implement it
- **Then** null is returned rather than an exception

#### Scenario: Null inner store is rejected at construction

- **Given** `new AsyncTenantStoreWrapper<IAsyncStore<M>, M>(null)`
- **When** the constructor runs
- **Then** an `ArgumentNullException` naming `innerStore` is thrown

### Requirement: Bulk Read overload hides the single-result Read

The system SHALL declare the bulk `Read(filter, orderBy, limit, offset)` / `ReadAsync(...)` overloads
on `TenantBulkStoreWrapper` / `AsyncTenantBulkStoreWrapper` while the single-result
`Read(filter)` / `ReadAsync(filter)` remain declared on the base wrapper.

#### Scenario: A filter-only call on a bulk-typed reference returns a collection

- **Given** a variable typed `TenantBulkStoreWrapper<IBulkStore<M>, M>`
- **When** `Read(x => x.Active)` is written
- **Then** it binds to the derived bulk overload and returns `IEnumerable<M>`, because C# member lookup stops at the most-derived type declaring the name

#### Scenario: The no-argument bulk read is fully unscoped by delegation

- **Given** a bulk wrapper
- **When** `Read()` is called
- **Then** it delegates to `Read(null, null, null, null)`, so the tenant seam still runs and Strict still throws

### Requirement: AsTenantAware selects the bulk wrapper when the store is bulk

The system SHALL, in `TenantStoreExtensions.AsTenantAware`, test `store is IBulkStore<T>` /
`store is IAsyncBulkStore<T>` and return the bulk wrapper closed over the bulk interface, otherwise
the non-bulk wrapper, defaulting `tenantContext` to null and `mode` to `Permissive`.

#### Scenario: A bulk store gets bulk tenant scoping

- **Given** a store implementing `IAsyncBulkStore<M>` referenced as `IAsyncStore<M>`
- **When** `AsTenantAware()` is called
- **Then** an `AsyncTenantBulkStoreWrapper<IAsyncBulkStore<M>, M>` is returned, so filter-based writes are tenant-composed

#### Scenario: A non-bulk store gets the plain wrapper

- **Given** a store implementing only `IStore<M>`
- **When** `AsTenantAware(ctx, TenantIsolationMode.Strict)` is called
- **Then** a `TenantStoreWrapper<IStore<M>, M>` with `_mode == Strict` and the supplied context is returned

### Requirement: DI registration of tenant-aware repositories

The system SHALL, in `RepositoryServiceCollectionExtensions`, register the inner store and then a
repository factory that resolves `ITenantContext` via `sp.GetService<ITenantContext>() ?? Models.Tenant.Current`,
wraps the store in `TenantStoreWrapper<,>` / `AsyncTenantStoreWrapper<,>` (never the bulk wrapper),
and constructs the repository via `Activator.CreateInstance(typeof(TRepository), wrappedStore)`,
throwing `InvalidOperationException` when that yields null. Default lifetime is `Scoped`; default
mode is `Permissive`.

#### Scenario: Repository receives a tenant-wrapped store

- **Given** `services.AddTenantAsyncRepositoryScoped<MyStore, MyRepository, MyModel>(TenantIsolationMode.Strict)`
- **When** `MyRepository` is resolved
- **Then** it is constructed with an `AsyncTenantStoreWrapper<MyStore, MyModel>` built with mode `Strict` and the scoped `ITenantContext`

#### Scenario: A bulk inner store is still wrapped non-bulk

- **Given** `TStore` implements `IAsyncBulkStore<TModel>`
- **When** `AddTenantAsyncRepository<TStore, TRepository, TModel>` builds the wrapper
- **Then** it constructs `AsyncTenantStoreWrapper<TStore, TModel>`, which does not implement `IAsyncBulkStore<TModel>` — diverging from `AsTenantAware`, which would have chosen the bulk wrapper

#### Scenario: Repository construction failure is reported

- **Given** a `TRepository` with no single-argument constructor accepting the wrapped store
- **When** the factory runs
- **Then** `Activator.CreateInstance` throws `MissingMethodException` and that exception propagates out of the factory — the `?? throw new InvalidOperationException` arm is never reached, because `CreateInstance` either throws or returns an instance of `typeof(TRepository)` that the `as TRepository` cast cannot turn into null

#### Scenario: No ITenantContext registered falls back to the static singleton

- **Given** a container with no `ITenantContext` registration
- **When** the repository factory runs
- **Then** `Models.Tenant.Current` is used, so any tenant an HTTP middleware set on a different instance is invisible

### Requirement: Tenant exception types are distinguishable but backward-compatible

The system SHALL derive `TenantMismatchException` from `UnauthorizedAccessException` and
`TenantScopeRequiredException` from `InvalidOperationException`, and SHALL carry diagnostic detail in
properties rather than in the message.

#### Scenario: Existing catch clauses keep working

- **Given** host code with `catch (UnauthorizedAccessException)` mapping to 403 and `catch (InvalidOperationException)` mapping to 500
- **When** a tenant mismatch and a missing-tenant refusal occur
- **Then** the mismatch is caught as `UnauthorizedAccessException` and the missing-tenant refusal as `InvalidOperationException`

#### Scenario: Tenant ids travel in properties, not the message

- **Given** a `TenantMismatchException("update", "Invoice", t, u)`
- **When** `Message` is read
- **Then** it is exactly `"Cannot update item: it does not belong to the current tenant"` with no guids in it, while `ExpectedTenantGuid`, `ActualTenantGuid`, `Operation` and `EntityType` expose the detail

#### Scenario: Scope-required exception exposes the refused operation

- **Given** a `TenantScopeRequiredException`
- **When** its properties are read
- **Then** `Operation` is `"read"` or `"create"` and `EntityType` is the entity type name or null

### Requirement: Birko.Data.Tenant HTTP tenant resolution order

The system SHALL, in `Birko.Data.Tenant.Middleware.TenantMiddleware.ResolveTenantGuid`, try in order:
the configured header (default `X-Tenant-Id`), the configured query-string key, the configured route
key, then `CustomTenantResolver` — returning the first value that `Guid.TryParse` accepts, and null
when nothing resolves. Each source is skipped when its configured key is null or empty.

#### Scenario: Header wins over query string

- **Given** `TenantQueryStringKey = "tenant"`, a request with header `X-Tenant-Id: {a}` and query `?tenant={b}`
- **When** the middleware runs
- **Then** tenant `a` is set

#### Scenario: An unparseable header falls through to later sources

- **Given** header `X-Tenant-Id: not-a-guid` and `TenantQueryStringKey = "tenant"` with `?tenant={b}`
- **When** the middleware runs
- **Then** tenant `b` is set, because the header branch only returns on a successful `Guid.TryParse`

#### Scenario: Route value must be a string

- **Given** `TenantRouteKey = "tenantId"` whose route value is a `Guid` object rather than a string
- **When** the middleware runs
- **Then** the `is string routeValue` pattern fails and resolution continues to `CustomTenantResolver`

#### Scenario: Query-string and route sources are opt-in

- **Given** default options (`TenantQueryStringKey` and `TenantRouteKey` are null)
- **When** a request arrives with `?TenantId=…`
- **Then** neither source is consulted and only the header and custom resolver can produce a tenant

### Requirement: Birko.Data.Tenant middleware sets HttpContext.Items and can require a tenant

The system SHALL, when a tenant resolves, call `SetTenant(guid, ResolveTenantName(...))` and store
the guid in `context.Items[options.TenantContextKey]` (default key `"TenantId"`); when no tenant
resolves and `RequireTenant` is true, SHALL short-circuit with HTTP `401`, content type
`application/json`, and a serialized `{ error = "Tenant Required", message = … }` body; and when
`RequireTenant` is false SHALL continue the pipeline with no tenant.

#### Scenario: Missing tenant with RequireTenant returns 401

- **Given** `RequireTenant = true` and a request with no tenant source
- **When** the middleware runs
- **Then** the response status is `401`, the content type is `application/json`, the body is the serialized error object using the injected `ISerializer` (defaulting to `SystemJsonSerializer`), and `_next` is never invoked

#### Scenario: Custom required-message is used verbatim

- **Given** `RequireTenant = true` and `TenantRequiredMessage = "Send X-Tenant-Id"`
- **When** the 401 is written
- **Then** the `message` field is `"Send X-Tenant-Id"`; with the option null it is `"A valid tenant identifier is required"`

#### Scenario: Tenant name resolution order

- **Given** `TenantNameHeaderName = "X-Tenant-Name"` present on the request and a `CustomTenantNameResolver` also configured
- **When** the name is resolved
- **Then** the header value is returned and the custom resolver is not called; with the header absent the custom resolver is called with the context and resolved guid; with neither, the name is null

#### Scenario: Missing tenant without RequireTenant proceeds unscoped

- **Given** default options (`RequireTenant = false`) and a request with no tenant source
- **When** the middleware runs
- **Then** `SetTenant` is never called, `HttpContext.Items` is untouched, and the pipeline continues — a Permissive store downstream then reads across all tenants

### Requirement: Birko.Data.Tenant middleware clears the tenant only on a normal completion

The system SHALL call `_tenantContext.ClearTenant()` unconditionally on the statement after
`await _next(context)`, without a `try`/`finally` and without checking whether this middleware set a
tenant.

#### Scenario: A downstream exception leaves the tenant set

- **Given** a request that resolves tenant `t` and a downstream middleware that throws
- **When** the exception propagates out of `InvokeAsync`
- **Then** `ClearTenant()` is never reached and the context still reports `HasTenant == true` — a leak when `ITenantContext` is registered as a singleton or is `Tenant.Current`

#### Scenario: A request with no tenant still clears

- **Given** `RequireTenant = false`, a request with no tenant source, and an outer flow that had already set tenant `t` on the same context instance
- **When** the middleware completes
- **Then** `ClearTenant()` runs anyway and tenant `t` is discarded

### Requirement: UseTenantMiddleware resolves ITenantContext from the application root provider

The system SHALL, in `TenantMiddlewareExtensions.UseTenantMiddleware`, build the options, resolve
`ITenantContext` from `builder.ApplicationServices`, throw `InvalidOperationException` naming
`AddTenantContext()` when it is not registered, and pass the resolved instance and options as
constructor arguments to `UseMiddleware<TenantMiddleware>`.

#### Scenario: Unregistered context fails at startup

- **Given** an app that never called `AddTenantContext`
- **When** `UseTenantMiddleware()` runs
- **Then** an `InvalidOperationException` is thrown whose message names `ITenantContext` and instructs the caller to call `services.AddTenantContext()`

#### Scenario: The middleware captures one context instance for the app's lifetime

- **Given** `AddTenantContextScoped()` and `UseTenantMiddleware()`
- **When** requests are served
- **Then** the middleware holds the instance resolved once from the root provider, while stores injected per request receive the request-scoped instance — two different objects, so the middleware's `SetTenant` is not observed by those stores

### Requirement: Tenant context DI lifetimes

The system SHALL register `ITenantContext` → `TenantContext` through `services.Add` with the
requested `ServiceLifetime` (default `Scoped`) in `AddTenantContext`, and offer
`AddTenantContext<T>` for a custom implementation plus `AddTenantContextSingleton`,
`AddTenantContextScoped` and `AddTenantContextTransient` shortcuts.

#### Scenario: Default lifetime is scoped

- **Given** `services.AddTenantContext()`
- **When** the descriptor is inspected
- **Then** its `ServiceType` is `ITenantContext`, `ImplementationType` is `TenantContext` and `Lifetime` is `Scoped`

#### Scenario: Registration is additive, not idempotent

- **Given** `AddTenantContextSingleton()` followed by `AddTenantContextScoped()`
- **When** `ITenantContext` is resolved
- **Then** two descriptors exist (plain `Add`, not `TryAdd`), and the last registration wins for a single resolution

### Requirement: Birko.Security.AspNetCore tenant resolver abstraction

The system SHALL define `ITenantResolver.ResolveAsync(HttpContext, CancellationToken)` returning
`Task<TenantInfo?>`, where `TenantInfo` is a sealed record of `Guid TenantGuid`,
`string? TenantName = null` and `Dictionary<string, string>? Metadata = null`.

#### Scenario: A resolver reports "no tenant" as null

- **Given** any `ITenantResolver`
- **When** the request carries no usable tenant signal
- **Then** the resolver returns null rather than a `TenantInfo` with a zero guid

### Requirement: Header-based tenant resolution

The system SHALL, in `HeaderTenantResolver`, read the first value of `X-Tenant-Id`, return null when
it is absent or fails `Guid.TryParse`, and otherwise return a `TenantInfo` with that guid and the
first value of `X-Tenant-Name` as the name.

#### Scenario: Valid header resolves

- **Given** headers `X-Tenant-Id: {t}` and `X-Tenant-Name: Acme`
- **When** `ResolveAsync` is awaited
- **Then** `TenantInfo(t, "Acme")` is returned

#### Scenario: Malformed header resolves to nothing

- **Given** header `X-Tenant-Id: 12345`
- **When** `ResolveAsync` is awaited
- **Then** null is returned and no exception is thrown

#### Scenario: A zero guid header is accepted as a tenant

- **Given** header `X-Tenant-Id: 00000000-0000-0000-0000-000000000000`
- **When** `ResolveAsync` is awaited
- **Then** `TenantInfo(Guid.Empty, …)` is returned — parsing succeeds and no zero-check is applied

#### Scenario: Name header without id resolves to nothing

- **Given** only `X-Tenant-Name: Acme`
- **When** `ResolveAsync` is awaited
- **Then** null is returned, because the id gate is evaluated first

### Requirement: Subdomain-based tenant resolution

The system SHALL, in `SubdomainTenantResolver`, extract a subdomain from `Request.Host.Host` and
delegate to the caller-supplied `lookupAsync(subdomain, ct)`, returning null without calling the
lookup when the host is empty, when a configured `baseDomain` is not an `OrdinalIgnoreCase` suffix of
the form `"." + baseDomain`, when no dot precedes position 1 in base-domain-less mode, or when the
extracted subdomain is empty.

#### Scenario: Base-domain mode strips the suffix

- **Given** `baseDomain = "example.com"` and host `tenant1.example.com`
- **When** `ResolveAsync` is awaited
- **Then** `lookupAsync("tenant1", ct)` is called and its result returned

#### Scenario: Base domain is normalized by trimming leading dots

- **Given** the resolver constructed with `baseDomain = ".example.com"`
- **When** host `tenant1.example.com` is resolved
- **Then** the suffix test uses `".example.com"` once, so the subdomain is `"tenant1"`

#### Scenario: Apex domain resolves to nothing

- **Given** `baseDomain = "example.com"` and host `example.com`
- **When** `ResolveAsync` is awaited
- **Then** the `EndsWith(".example.com")` test fails and null is returned

#### Scenario: A foreign host is rejected

- **Given** `baseDomain = "example.com"` and host `tenant1.evil.com`
- **When** `ResolveAsync` is awaited
- **Then** null is returned and the lookup is never called

#### Scenario: Multi-level subdomain is passed through whole

- **Given** `baseDomain = "example.com"` and host `a.b.example.com`
- **When** `ResolveAsync` is awaited
- **Then** `lookupAsync("a.b", ct)` is called

#### Scenario: Base-domain-less mode takes the first label

- **Given** no `baseDomain` and host `tenant1.example.com`
- **When** `ResolveAsync` is awaited
- **Then** `lookupAsync("tenant1", ct)` is called; with host `localhost` (no dot) or a host starting with `.`, null is returned

### Requirement: Birko.Security.AspNetCore tenant middleware always clears in a finally

The system SHALL, in `Birko.Security.AspNetCore.TenantMiddleware`, receive `ITenantResolver` and
`ITenantContext` as `InvokeAsync` parameters (resolved per request), set the tenant when the resolver
returns non-null, invoke the rest of the pipeline in a `try`, and call `ClearTenant()` in the
`finally`.

#### Scenario: The tenant is cleared even when the pipeline throws

- **Given** a request that resolves tenant `t` and a downstream throw
- **When** the exception propagates
- **Then** `ClearTenant()` has run — diverging from `Birko.Data.Tenant.Middleware.TenantMiddleware`, which leaks in the same situation

#### Scenario: Per-request injection sees the scoped context

- **Given** `ITenantContext` registered scoped
- **When** `InvokeAsync` runs
- **Then** the container supplies the request-scoped instance as a method parameter, so downstream request-scoped consumers observe the tenant it sets

#### Scenario: There is no require-tenant option

- **Given** a request whose resolver returns null
- **When** the middleware runs
- **Then** the pipeline continues with no tenant and no `401` — this middleware has no `RequireTenant` equivalent

#### Scenario: Resolution honours request abortion

- **Given** a client that disconnects
- **When** `ResolveAsync(context, context.RequestAborted)` is awaited
- **Then** the resolver receives the request's cancellation token

### Requirement: The ASP.NET tenant-context adapter narrows the Birko contract

The system SHALL define a separate `Birko.Security.AspNetCore.ITenantContext` exposing only
`CurrentTenantGuid`, `CurrentTenantName`, `HasTenant`, `SetTenant` and `ClearTenant`, and SHALL
implement it in `TenantContextAdapter` by delegating each member to a wrapped
`Birko.Data.Tenant.Models.ITenantContext`.

#### Scenario: Delegation is one-to-one

- **Given** a `TenantContextAdapter` over a `TenantContext`
- **When** `SetTenant(t, "Acme")` is called on the adapter
- **Then** the underlying Birko context reports `CurrentTenantGuid == t`, and the adapter's getters read straight through

#### Scenario: Admin scope is not expressible through the adapter

- **Given** code holding only a `Birko.Security.AspNetCore.ITenantContext`
- **When** it looks for a cross-tenant scope
- **Then** no `IsAllTenantsScope`, `WithAllTenants` or `WithTenant` member exists on that interface, so the deliberate-cross-tenant path is unreachable without reaching for the Birko interface

### Requirement: X-Tenant-Id must agree with the caller's tenant claim

The system SHALL, in `TenantHeaderClaimGuardMiddleware`, compare the `X-Tenant-Id` header to
`currentUser.TenantGuid ?? Guid.Empty` and, on disagreement, short-circuit with HTTP `403`, content
type `application/json` and the exact body
`{"Error":"The X-Tenant-Id header does not match the tenant this session was issued for.","Code":"Tenant.HeaderClaimMismatch"}`.

#### Scenario: Header naming another tenant is refused

- **Given** an authenticated non-wildcard caller whose claim tenant is `t` and header `X-Tenant-Id: {u}`
- **When** the middleware runs
- **Then** the response is `403` with code `Tenant.HeaderClaimMismatch` and `_next` is never invoked

#### Scenario: A system-scope token cannot name a real tenant

- **Given** an authenticated non-wildcard caller with no `tenant_id` claim (so the claim resolves to `Guid.Empty`) and header `X-Tenant-Id: {u}`
- **When** the middleware runs
- **Then** the request is refused with `403`

#### Scenario: Matching header passes

- **Given** claim tenant `t` and header `X-Tenant-Id: {t}`
- **When** the middleware runs
- **Then** the pipeline continues unchanged

### Requirement: Deliberate pass-throughs of the header/claim guard

The system SHALL invoke `_next` without comparison when
`BirkoSecurityOptions.RequireTenantHeaderMatchesClaim` is false, when the header is absent or
whitespace, when `currentUser.IsAuthenticated` is false, when wildcard permissions are enabled and
the caller's permissions contain `"*"`, or when the header value fails `Guid.TryParse`.

#### Scenario: No header means the claim is the only source

- **Given** an authenticated caller sending no `X-Tenant-Id`
- **When** the middleware runs
- **Then** the request proceeds — required so header-less transports such as server-sent events keep working

#### Scenario: Anonymous endpoints are not gated

- **Given** an unauthenticated request carrying `X-Tenant-Id: {u}`
- **When** the middleware runs
- **Then** the request proceeds, since there is no claim to compare against

#### Scenario: Wildcard holders may address any tenant

- **Given** `WildcardPermissionEnabled` and a caller whose permissions contain `"*"`, with header `X-Tenant-Id: {u}` and claim tenant `t`
- **When** the middleware runs
- **Then** the request proceeds

#### Scenario: An unparseable header is treated as absent

- **Given** an authenticated caller with claim tenant `t` and header `X-Tenant-Id: nonsense`
- **When** the middleware runs
- **Then** the request proceeds without a 403, on the grounds that `HeaderTenantResolver` would resolve nothing from it

#### Scenario: Opting out makes the middleware a pass-through

- **Given** `RequireTenantHeaderMatchesClaim = false`
- **When** any request arrives, matching or not
- **Then** `_next` is invoked immediately and no header is read

### Requirement: Header-guard wiring

The system SHALL expose `UseBirkoTenantHeaderGuard(this IApplicationBuilder)` as
`app.UseMiddleware<TenantHeaderClaimGuardMiddleware>()`, safe to call unconditionally.

#### Scenario: Registration is a single call

- **Given** an app builder
- **When** `UseBirkoTenantHeaderGuard()` is called
- **Then** `TenantHeaderClaimGuardMiddleware` is added to the pipeline at that position, and with the option disabled it behaves as a pass-through rather than needing conditional registration

### Requirement: Published events capture the ambient tenant

The system SHALL, in `TenantEventEnricher.EnrichAsync`, assign
`context.TenantGuid = _tenantContext.CurrentTenantGuid` when `context is not null` and
`_tenantContext.HasTenant`, and otherwise leave `context.TenantGuid` untouched. The constructor
SHALL throw `ArgumentNullException` for a null tenant context.

#### Scenario: The publishing tenant is stamped

- **Given** ambient tenant `t` and an `EventContext` with a null `TenantGuid`
- **When** `EnrichAsync(@event, context)` is awaited
- **Then** `context.TenantGuid` is `t`

#### Scenario: No ambient tenant leaves the context null

- **Given** no tenant in scope
- **When** `EnrichAsync` is awaited on a context whose `TenantGuid` is null
- **Then** it stays null — a genuine system / cross-tenant event

#### Scenario: An existing value is overwritten by the ambient tenant

- **Given** a context whose `TenantGuid` was already `u`, and ambient tenant `t`
- **When** `EnrichAsync` is awaited
- **Then** `context.TenantGuid` becomes `t`; the guard only prevents overwriting with null, not overwriting a different tenant

#### Scenario: A null context is tolerated

- **Given** `context == null`
- **When** `EnrichAsync(@event, null)` is awaited
- **Then** it completes without throwing

### Requirement: Background event dispatch restores the publishing tenant

The system SHALL, in `TenantEventScopeAccessor.RunWithScopeAsync`, throw `ArgumentNullException` for
a null `body`, run the body inside `WithTenantAsync(tenant, null, body)` when
`context?.TenantGuid is Guid tenant`, and inside `WithAllTenantsAsync(body)` when the context is null
or its `TenantGuid` is null.

#### Scenario: A tenant-attributed event dispatches inside that tenant

- **Given** an `EventContext` with `TenantGuid == t`
- **When** `RunWithScopeAsync(context, handler)` is awaited
- **Then** the handler runs with `CurrentTenantGuid == t`, so a Strict tenant-scoped repository inside it works

#### Scenario: Guid.Empty scopes rather than widens

- **Given** an `EventContext` with `TenantGuid == Guid.Empty`
- **When** the body is dispatched
- **Then** it runs inside `WithTenantAsync(Guid.Empty, …)` — scoped to the zero tenant, not across all tenants

#### Scenario: A tenant-less event dispatches cross-tenant

- **Given** an `EventContext` with a null `TenantGuid`
- **When** the body is dispatched
- **Then** it runs inside `WithAllTenantsAsync`, so `IsAllTenantsScope` is true and Strict operations are permitted across tenants

#### Scenario: A null event context is treated as tenant-less

- **Given** `context == null`
- **When** `RunWithScopeAsync(null, body)` is awaited
- **Then** the `?.` short-circuits and the body runs inside `WithAllTenantsAsync`

### Requirement: Event/tenant bridge registration registers both halves as singletons

The system SHALL, in `AddEventTenantScope`, throw `ArgumentNullException` for a null service
collection and register `IEventScopeAccessor` → `TenantEventScopeAccessor` and `IEventEnricher` →
`TenantEventEnricher` as singleton **instances** over one `ITenantContext`; the parameterless
overload SHALL use `Birko.Data.Tenant.Models.Tenant.Current`.

#### Scenario: Both sides share one context instance

- **Given** `services.AddEventTenantScope(myContext)`
- **When** the descriptors are inspected
- **Then** both singletons hold instances constructed over `myContext`, so what the enricher captures is what the accessor restores

#### Scenario: The default overload binds the static singleton

- **Given** `services.AddEventTenantScope()`
- **When** the bridge is used
- **Then** it observes `Tenant.Current`, which only matches store/repository behaviour if the app's `ITenantContext` registration is that same instance

### Requirement: Tenant-scoped sync options resolution

The system SHALL, in `TenantSyncProvider.ApplyTenantContext`, fill `TenantSyncOptions.TenantGuid`
from the ambient context only when the caller left it null and `HasTenant` is true, and SHALL
otherwise wrap a plain `SyncOptions` into a new `TenantSyncOptions` copying every option field and
setting `TenantGuid` to the ambient tenant (or null when none is set).

#### Scenario: An explicit tenant on the options wins

- **Given** `TenantSyncOptions { TenantGuid = u }` and ambient tenant `t`
- **When** `SyncAsync(options)` runs
- **Then** the knowledge store is queried with `u`

#### Scenario: A plain SyncOptions is promoted

- **Given** a plain `SyncOptions` and ambient tenant `t`
- **When** `PreviewAsync(options)` runs
- **Then** a new `TenantSyncOptions` with `TenantGuid == t` is used and the caller's instance is not modified

#### Scenario: A caller-supplied TenantSyncOptions is mutated in place

- **Given** a `TenantSyncOptions` with a null `TenantGuid` and ambient tenant `t`
- **When** `SyncAsync(options)` runs
- **Then** the same instance is returned with `TenantGuid` set to `t`, so the caller's object retains `t` after the call

#### Scenario: Knowledge is keyed by scope and tenant

- **Given** effective tenant `t` and scope `"invoices"`
- **When** the provider loads knowledge and last-sync time
- **Then** `GetKnowledgeAsync("invoices", t, ct)` and `GetLastSyncTimeAsync("invoices", t, ct)` are called, and on completion `SetLastSyncTimeAsync("invoices", t, now, ct)`

### Requirement: Tenant sync filters writes only, via the save predicates

The system SHALL, in `ApplyTenantFiltering`, wrap `CanSaveToLocal` and `CanSaveToRemote` with a
reflection-based `BelongsToTenant` check (conjoined with any existing predicate) only when
`HasTenant` is true and the entity type declares a `TenantGuid` property — and SHALL leave
`LocalFetchPredicate` and `RemoteFetchPredicate` untouched.

#### Scenario: Cross-tenant items are not saved

- **Given** ambient tenant `t` and a remote item whose `TenantGuid` is `u`, resolved to `SyncAction.Create` in the Download direction
- **When** the create branch consults `CanSaveToLocal`
- **Then** it returns false and `progress.SkippedItems` is incremented instead of the item being written locally

#### Scenario: A caller's predicate is preserved as a conjunction

- **Given** a caller-supplied `CanSaveToRemote` and ambient tenant `t`
- **When** the wrapped predicate runs
- **Then** the item must both belong to `t` and satisfy the caller's predicate

#### Scenario: Both stores are read across all tenants

- **Given** ambient tenant `t` and no `LocalFetchPredicate`/`RemoteFetchPredicate`
- **When** `GetAllItemsAsync` runs
- **Then** `store.ReadAsync(ct)` is called with no tenant predicate, so every tenant's items enter `localDict`/`remoteDict` and are compared, previewed and counted

#### Scenario: An entity type with no TenantGuid property is never filtered

- **Given** a model without a `TenantGuid` property
- **When** `ApplyTenantFiltering` runs
- **Then** the save predicates are left as supplied, and `BelongsToTenant` (if reached) returns true

#### Scenario: A TenantGuid that is not a Guid excludes the item

- **Given** a model declaring `TenantGuid` whose value on an instance is null or not a `Guid`
- **When** `BelongsToTenant(item, t)` runs
- **Then** it returns false, excluding the item — the allow-all path is reserved for types with no such property at all

#### Scenario: Deletes bypass the save predicates entirely

- **Given** ambient tenant `t` and an item belonging to tenant `u` resolved to `SyncAction.Delete`
- **When** the delete branch runs
- **Then** `_localStore.DeleteAsync` / `_remoteStore.DeleteAsync` is called with no `CanSaveToLocal`/`CanSaveToRemote` consultation, so another tenant's row is deleted

#### Scenario: No ambient tenant disables tenant filtering completely

- **Given** `HasTenant == false`
- **When** `ApplyTenantFiltering` runs
- **Then** no wrapping happens and every item on both sides is eligible for save, regardless of its `TenantGuid`

### Requirement: Sync knowledge items are stamped with the effective tenant

The system SHALL, in `CreateKnowledgeItem`, compute the tenant as
`GetTenantGuid(options) ?? (HasTenant ? CurrentTenantGuid : Guid.Empty)`, store it as
`TenantGuid ?? Guid.Empty` on a `TenantSyncKnowledgeItem`, and record `IsLocalDeleted` /
`IsRemoteDeleted` only as `hasKnowledge && item == null`.

#### Scenario: Options tenant takes precedence over the ambient one

- **Given** `TenantSyncOptions { TenantGuid = u }` and ambient tenant `t`
- **When** a knowledge item is created
- **Then** its `TenantGuid` is `u`

#### Scenario: No tenant anywhere stamps the zero guid

- **Given** a plain `SyncOptions` promoted with no ambient tenant
- **When** a knowledge item is created
- **Then** its `TenantGuid` is `Guid.Empty`

#### Scenario: A first-seen one-sided item is not marked deleted

- **Given** an item present locally, absent remotely, with no prior knowledge entry
- **When** the knowledge item is created
- **Then** `IsRemoteDeleted` is false, because `hasKnowledge` is false — absence alone is not evidence of deletion

#### Scenario: A previously-synced item now absent is marked deleted

- **Given** an item with a prior knowledge entry, now absent remotely
- **When** the knowledge item is created
- **Then** `IsRemoteDeleted` is true, which later drives a `SyncAction.Delete` on the local side

### Requirement: Deterministic version hashing and cached timestamp reflection

The system SHALL resolve the `UpdatedAt` property once per closed generic type into a
`static readonly PropertyInfo?`, accepting both `DateTime` and `DateTime?` and caching null
otherwise; `GetVersionHash` SHALL return null for a null entity, the round-trip `"O"` string of
`UpdatedAt` when present, otherwise the uppercase hex SHA-256 of the entity's
`System.Text.Json` serialization, and `string.Empty` if that serialization or hashing throws.

#### Scenario: Timestamped entity hashes to its timestamp

- **Given** an entity with `UpdatedAt` set
- **When** `GetVersionHash(entity)` is called
- **Then** the result is `UpdatedAt.ToString("O")`

#### Scenario: Untimestamped entity hashes deterministically

- **Given** two entities with no `UpdatedAt` property but identical serialized state
- **When** `GetVersionHash` is called on each
- **Then** the two hashes are equal, so repeated syncs compare stable versions

#### Scenario: An unserializable entity degrades to an empty version

- **Given** an entity whose JSON serialization throws (e.g. a reference cycle)
- **When** `GetVersionHash` is called
- **Then** `string.Empty` is returned and no exception escapes

#### Scenario: A non-DateTime UpdatedAt is ignored

- **Given** a model with a `string UpdatedAt` property
- **When** `GetUpdatedAt` is called
- **Then** the cached `PropertyInfo` is null and the method returns null, sending version hashing down the content-hash path

### Requirement: Preview propagates failures while Sync collects them

The system SHALL let any exception escape `ExecutePreviewAsync` unchanged, and SHALL, in
`ExecuteSyncAsync`, catch per-item exceptions into `result.Errors` (incrementing
`progress.Errors`) and catch any outer exception into a single `SyncError { Message = "Sync failed" }`
with `result.Success = false`, `EndTime`/`Duration` filled and progress reported as
`SyncPhase.Failed`.

#### Scenario: A store read failure surfaces from Preview

- **Given** a local store whose `ReadAsync` throws
- **When** `PreviewAsync` is awaited
- **Then** the original exception propagates to the caller — it is not reported as a conflict on a partial preview

#### Scenario: A store read failure is swallowed into the result by Sync

- **Given** the same failing store
- **When** `SyncAsync` is awaited
- **Then** it returns a `SyncResult` with `Success == false` and one `SyncError` whose `Message` is `"Sync failed"` and whose `Exception` is the original

#### Scenario: One failing item does not abort the run

- **Given** a batch where one item's write throws
- **When** the sync continues
- **Then** a `SyncError` with that item's `ItemGuid` and the action name is recorded, `progress.Errors` is incremented, and the remaining items are still processed

#### Scenario: Duplicate entity guids abort differently per entry point

- **Given** two items on the same side sharing a guid (e.g. both `Guid.Empty`)
- **When** `localItems.ToDictionary(GetGuid)` runs
- **Then** an `ArgumentException` is thrown, which propagates out of `PreviewAsync` but is converted into a `"Sync failed"` `SyncError` by `SyncAsync`

### Requirement: Sync direction, cancellation and batch reporting

The system SHALL compute `effectiveDirection = isInitialSync ? SyncDirection.Download : options.Direction`
into a local variable, report it as `result.Direction`, check
`options.CancellationToken.IsCancellationRequested` at both the batch loop and the item loop, and
invoke `OnBatchCompleted` after each batch with `BatchNumber = (i / BatchSize) + 1`,
`Processed = batchGuids.Count` and `Errors = result.Errors.Skip(result.Errors.Count - progress.Errors)`.

#### Scenario: Initial sync downloads regardless of the requested direction

- **Given** no recorded last-sync time and `Direction = Upload`
- **When** `SyncAsync` runs
- **Then** `result.IsInitialSync` is true, `result.Direction` is `Download`, and creates are applied to the local store

#### Scenario: The caller's options object is not mutated for direction

- **Given** `options.Direction == Upload` and an initial sync
- **When** the run completes
- **Then** `options.Direction` is still `Upload`; only the local `effectiveDirection` and `result.Direction` reflect the override

#### Scenario: Cancellation stops the outer batch loop promptly

- **Given** a token cancelled mid-batch
- **When** the current batch's item loop breaks
- **Then** the enclosing `for` also breaks on its next iteration check, so no further batches are iterated

#### Scenario: Batch error reporting repeats earlier batches' errors

- **Given** one error in batch 1 and one error in batch 2
- **When** `OnBatchCompleted` fires for batch 2
- **Then** `progress.Errors` is 2, so `Skip(Count - 2)` yields both errors — batch 2's callback reports batch 1's error as well

#### Scenario: Conflict resolution failures reach the per-item handler

- **Given** a conflict whose resolution write throws
- **When** `ApplyConflictResolutionAsync` propagates
- **Then** the caller's per-item `catch` records a `SyncError` and `result.Success` becomes false

### Requirement: Sync conflict determination and resolution

The system SHALL determine an action per item from existence on each side, the knowledge entry's
`IsLocalDeleted`/`IsRemoteDeleted` flags and `options.Direction`, and SHALL resolve a `Conflict`
through `options.OnConflict`, then `CustomConflictResolver` when the policy is `Custom`, then a
policy switch defaulting to `ConflictResolution.UseLocal`.

#### Scenario: Bidirectional presence on both sides consults the policy

- **Given** an item present locally and remotely, `Direction = Bidirectional`, `ConflictPolicy = NewestWins`, and both sides carrying `UpdatedAt`
- **When** the action is determined
- **Then** it is `SyncAction.Update` with the winner being the side whose `UpdatedAt` is greater, and ties resolve to `"remote"` (the `>` comparison is strict)

#### Scenario: Local edit versus remote delete is a conflict

- **Given** an item present locally, absent remotely, with a knowledge entry whose `IsRemoteDeleted` is true, in `Bidirectional`
- **When** the action is determined
- **Then** it is `SyncAction.Conflict` with reason `"Modified locally but deleted remotely"`

#### Scenario: Missing timestamps default to local

- **Given** `NewestWins` and entities with no resolvable `UpdatedAt`
- **When** `GetNewest` / `GetNewestConflictResolution` runs
- **Then** `"local"` / `ConflictResolution.UseLocal` is returned

#### Scenario: A Custom policy with no resolver falls back to UseLocal

- **Given** `ConflictPolicy = Custom` and `CustomConflictResolver == null`
- **When** `ResolveConflict` runs
- **Then** the policy switch's default arm returns `ConflictResolution.UseLocal`

#### Scenario: Download-only never writes to the remote

- **Given** `Direction = Download` and an item present only locally with no remote-delete knowledge
- **When** the action is determined
- **Then** it is `SyncAction.Skip` — the local-only create is not uploaded

### Requirement: Tenant-scoped sync queue keys

The system SHALL, in `TenantSyncQueue`, default its context to `Tenant.Current`, override
`GetQueueKey(string scope)` to return `"{scope}_{CurrentTenantGuid}"` when a tenant is in scope and
the base key otherwise, and expose an explicit-tenant `EnqueueAsync(scope, tenantGuid, op, ct)` and
`GetQueueLength(scope, tenantGuid)` that resolve `tenantGuid ?? (HasTenantContext ? CurrentTenantGuid : null)`
and key via the base two-argument `GetQueueKey`.

#### Scenario: Two tenants queue independently for the same scope

- **Given** tenant `t` and tenant `u` both enqueuing scope `"invoices"`
- **When** the keys are computed
- **Then** they are `"invoices_{t}"` and `"invoices_{u}"`, so each tenant's pending operations are queued and counted under its own key — but execution still serializes across tenants, because `EnqueueWithKeyAsync` awaits the one inherited `SemaphoreSlim` sized by `maxConcurrentSyncs` (default 1) that every key shares

#### Scenario: No tenant falls back to the bare scope key

- **Given** no tenant in scope
- **When** `GetQueueKey("invoices")` runs
- **Then** it returns `"invoices"` (the base implementation)

#### Scenario: An explicit tenant overrides the ambient one

- **Given** ambient tenant `t`
- **When** `EnqueueAsync("invoices", u, op)` is awaited
- **Then** the key is `"invoices_{u}"`

#### Scenario: A null explicit tenant with no ambient tenant yields the bare scope

- **Given** no tenant in scope
- **When** `GetQueueLength("invoices", null)` is called
- **Then** `GetEffectiveTenantGuid` returns null, the base `GetQueueKey(scope, null)` returns `"invoices"`, and the length of that queue is reported (0 when absent)

### Requirement: Tenant-aware sync setup helpers

The system SHALL expose `TenantSyncProviderExtensions.CreateTenantSync` and `WithTenantSync` to build
a `TenantSyncProvider<TStore, T>` over a local store, a remote store, an `ISyncKnowledgeStore` and an
optional `ITenantContext`, and SHALL require the entity type to expose a `Guid` property.

#### Scenario: A model without a Guid property is rejected at construction

- **Given** a model type with no `Guid` property
- **When** `new TenantSyncProvider<TStore, T>(...)` runs
- **Then** an `InvalidOperationException` is thrown stating the type must have a `Guid` property

#### Scenario: Null stores are rejected

- **Given** a null local store, remote store or knowledge store
- **When** the constructor runs
- **Then** an `ArgumentNullException` naming that parameter is thrown

#### Scenario: Omitted context defaults to the static singleton

- **Given** `CreateTenantSync(knowledgeStore)` with no `tenantContext`
- **When** the provider resolves the tenant
- **Then** it reads `Data.Tenant.Models.Tenant.Current`
