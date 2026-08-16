---
area: store-decorator-composition
generated-at: f3ac6755e788bc3e4693d27d37c583d67532a816
generated-on: 2026-07-30
sources:
  - ../Birko.Data.Composition/StoreWrapperBuilder.cs
  - ../Birko.Data.Patterns/Concurrency/AsyncVersionedStoreWrapper.cs
  - ../Birko.Data.Patterns/Concurrency/ConcurrentUpdateException.cs
  - ../Birko.Data.Patterns/Concurrency/IVersioned.cs
  - ../Birko.Data.Patterns/Concurrency/VersionedStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/AsyncAuditBulkStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/AsyncAuditStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/AsyncDefaultStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/AsyncSluggableBulkStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/AsyncSluggableStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/AsyncSoftDeleteBulkStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/AsyncSoftDeleteStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/AsyncTimestampBulkStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/AsyncTimestampStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/AuditBulkStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/AuditStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/DefaultStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/SlugGenerator.cs
  - ../Birko.Data.Patterns/Decorators/SluggableBulkStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/SluggableStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/SoftDeleteBulkStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/SoftDeleteFilter.cs
  - ../Birko.Data.Patterns/Decorators/SoftDeleteStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/TimestampBulkStoreWrapper.cs
  - ../Birko.Data.Patterns/Decorators/TimestampStoreWrapper.cs
  - ../Birko.Data.Patterns/Models/IAuditContext.cs
  - ../Birko.Data.Patterns/Models/IAuditable.cs
  - ../Birko.Data.Patterns/Models/ISluggable.cs
  - ../Birko.Data.Patterns/Models/ISoftDeletable.cs
  - ../Birko.Time.Abstractions/Core/IDateTimeProvider.cs
  - ../Birko.Time.Abstractions/Providers/SystemDateTimeProvider.cs
  - ../Birko.Time.Abstractions/Providers/TestDateTimeProvider.cs
source-commits:   # sibling baselines. RECONSTRUCTED 2026-08-16 from generated-on, not
                  # recorded at regen time -- see .map.yml § BASELINE AMNESTY.
  ../Birko.Data.Composition: dfdb6fb
  ../Birko.Data.Patterns: 4663f80
  ../Birko.Time.Abstractions: 47fa6a9
shaped-by: []
---

# Store decorator wrappers and runtime composition

## Purpose

Cross-cutting persistence concerns — timestamping, who-changed-what auditing, soft deletion, URL slug
generation, single-default enforcement, and optimistic concurrency — are implemented once as *decorators*
that wrap any store rather than being re-implemented per storage backend. Each decorator implements the
same store interface it wraps (`IStore<T>` / `IAsyncStore<T>` / `IBulkStore<T>` / `IAsyncBulkStore<T>`),
delegates to an inner store, and adds its own behaviour before or after delegating. `StoreWrapperBuilder`
assembles a chain of these decorators at runtime by probing which marker interfaces the entity type
implements, because the marker checks cannot be expressed as C# generic constraints. Anything that
persists a Birko entity — repositories, services, tenant-scoped stores — sits on top of whatever chain
the builder produced, so the decorators' ordering and their sync/async differences are directly visible
in what gets written to storage.

## Requirements

### Requirement: Interface-probed chain assembly

The system SHALL, in `StoreWrapperBuilder.Build<T>`, wrap a raw `IAsyncBulkStore<T>` with one decorator per
marker interface that `typeof(T)` implements, applying them innermost-to-outermost in the fixed order
EventSourcing, Timestamp, Audit, Tenant, SoftDelete, Sluggable, Default — so the resulting chain reads
outermost-to-innermost as Default, Sluggable, SoftDelete, Tenant, Audit, Timestamp, EventSourcing, raw store.

#### Scenario: Entity implementing no marker interface

- **Given** an entity type `T : AbstractModel, new()` that implements none of `IEventSourced`, `ITimestamped`, `IAuditable`, `ITenant`, `ISoftDeletable`, `ISluggable`, `IDefault`
- **When** `StoreWrapperBuilder.Build(rawStore)` is called
- **Then** the raw store instance itself is returned, with no decorator applied

#### Scenario: Entity implementing every marker interface with every context supplied

- **Given** `T` implements `IEventSourced`, `ITimestamped`, `IAuditable`, `ITenant`, `ISoftDeletable`, `ISluggable` and `IDefault`, and a clock, `IAuditContext`, `ITenantContext` and `IAsyncEventStore` are all passed
- **When** `Build` is called
- **Then** the returned store is an `AsyncDefaultStoreWrapper<IAsyncBulkStore<T>, T>` whose inner store is an `AsyncSluggableBulkStoreWrapper`, then `AsyncSoftDeleteBulkStoreWrapper`, then `AsyncTenantBulkStoreWrapper`, then `AsyncAuditBulkStoreWrapper`, then `AsyncTimestampBulkStoreWrapper`, then `AsyncEventSourcingBulkStoreWrapper`, then the raw store

#### Scenario: Timestamp applied without any explicit context

- **Given** `T` implements only `ITimestamped` and `Build` is called with no `clock`, no `auditContext`, no `tenantContext` and no `eventStore`
- **When** `Build` returns
- **Then** exactly one decorator — `AsyncTimestampBulkStoreWrapper<IAsyncBulkStore<T>, T>` — wraps the raw store

### Requirement: Context-gated decorators

The system SHALL apply the Audit, Tenant and EventSourcing decorators only when BOTH the marker interface is
implemented AND the corresponding context argument (`auditContext`, `tenantContext`, `eventStore`) is
non-null, and SHALL apply the Timestamp, SoftDelete, Sluggable and Default decorators on the marker interface
alone with no context precondition.

#### Scenario: Auditable entity with no audit context

- **Given** `T` implements `IAuditable` but `auditContext` is left null
- **When** `Build` is called
- **Then** no `AsyncAuditBulkStoreWrapper` is present in the chain and `CreatedBy`/`UpdatedBy` are never stamped

#### Scenario: Tenant entity with no tenant context

- **Given** `T` implements `ITenant` but `tenantContext` is null
- **When** `Build` is called
- **Then** no tenant decorator is applied and writes are not tenant-scoped, regardless of the `tenantMode` argument

#### Scenario: Event-sourced entity with no event store

- **Given** `T` implements `IEventSourced` but `eventStore` is null
- **When** `Build` is called
- **Then** no `AsyncEventSourcingBulkStoreWrapper` is applied and no events are recorded

### Requirement: Ordering consequences of the assembled chain

The system SHALL place the uniqueness-enforcing decorators (Default, Sluggable) and SoftDelete OUTSIDE the
Tenant decorator, and the Tenant decorator OUTSIDE the enriching decorators (Audit, Timestamp,
EventSourcing) — so that the probe and corrective queries the outer decorators issue against their own
inner store pass through the tenant filter and the soft-delete filter, while a tenant guard rejection
occurs before any event is recorded.

#### Scenario: Single-default uniqueness is scoped per tenant

- **Given** a chain built for a `T` implementing `IDefault` and `ITenant` with a tenant context in scope, and rows with `IsDefault = true` existing in two different tenants
- **When** an entity with `IsDefault = true` is created
- **Then** `AsyncDefaultStoreWrapper.UnsetOtherDefaultsAsync` reads `e => e.IsDefault` through the inner tenant decorator, so only the current tenant's other defaults are cleared and the sibling tenant's default row is left untouched

#### Scenario: Slug uniqueness ignores soft-deleted rows

- **Given** a chain built for a `T` implementing `ISluggable` and `ISoftDeletable`, and an existing row with `Slug = "widget"` whose `DeletedAt` is non-null
- **When** a new entity resolving to base slug `widget` is created
- **Then** the Sluggable wrapper's `_innerStore.Read(entity => entity.Slug == slug)` passes through the SoftDelete wrapper, which appends `DeletedAt == null`, so the deleted row is not seen and the new entity keeps the slug `widget` with no numeric suffix

#### Scenario: Corrective default-clearing writes pass through the inner enrichers

- **Given** a chain of Default → Sluggable → SoftDelete → Timestamp → raw, and two existing rows with `IsDefault = true`
- **When** a third entity with `IsDefault = true` is created
- **Then** `UnsetOtherDefaultsAsync` calls `_innerStore.UpdateAsync(toUpdate, storeDelegate: null, ct)` on the Sluggable wrapper, so the two demoted sibling rows additionally have their slugs re-resolved by the Sluggable wrapper and their `UpdatedAt`/`PrevUpdatedAt` re-stamped by the Timestamp wrapper

### Requirement: Clock defaulting in the builder

The system SHALL, when `StoreWrapperBuilder.Build` is called with a null `clock`, substitute a newly
constructed `SystemDateTimeProvider` and pass that same instance to every clock-consuming decorator in the
chain.

#### Scenario: Null clock supplied

- **Given** `T` implements `ITimestamped` and `ISoftDeletable` and `clock` is null
- **When** `Build` is called
- **Then** both the Timestamp and SoftDelete decorators receive the same `SystemDateTimeProvider` instance, whose `UtcNow` returns `DateTime.UtcNow`

#### Scenario: Test clock supplied

- **Given** a `TestDateTimeProvider` constructed with no argument (so `UtcNow` is `2026-01-01T12:00:00Z`) is passed as `clock`
- **When** an entity is created through the chain
- **Then** `CreatedAt` and `UpdatedAt` are `2026-01-01T12:00:00Z`, and after `Advance(TimeSpan.FromHours(1))` a subsequent update stamps `UpdatedAt` as `2026-01-01T13:00:00Z`

### Requirement: Tenant wrapper substitution

The system SHALL use a caller-supplied `tenantWrapperFactory` in place of the built-in tenant decorator when
that delegate is non-null, and otherwise construct `AsyncTenantBulkStoreWrapper<,>` with the supplied
`tenantMode`, defaulting `tenantMode` to `TenantIsolationMode.Permissive`.

#### Scenario: Custom fail-closed tenant wrapper injected

- **Given** `T` implements `ITenant`, a non-null `tenantContext`, and a `tenantWrapperFactory` returning a custom wrapper
- **When** `Build` is called
- **Then** the factory is invoked with the current inner store and the tenant context, its return value is used as the tenant link in the chain, and `tenantMode` is ignored

#### Scenario: Default isolation mode

- **Given** `T` implements `ITenant`, a non-null `tenantContext`, and no `tenantMode` argument
- **When** `Build` is called
- **Then** `AsyncTenantBulkStoreWrapper` is constructed with `TenantIsolationMode.Permissive`

### Requirement: Reflection-based decorator construction

The system SHALL construct each decorator by closing its open generic type over `(IAsyncBulkStore<T>,
typeof(T))` and invoking `Activator.CreateInstance` with the current inner store as the first constructor
argument followed by the decorator's context arguments, casting the result back to `IAsyncBulkStore<T>`.

#### Scenario: Decorator closed over the interface, not the concrete store type

- **Given** a raw store whose concrete type is `AsyncInMemoryStore<T>`
- **When** `Build` wraps it with the Timestamp decorator
- **Then** the constructed type is `AsyncTimestampBulkStoreWrapper<IAsyncBulkStore<T>, T>` — the `TStore` type argument is the interface, not `AsyncInMemoryStore<T>`, so the decorator can only reach interface members of the inner store

#### Scenario: Event sourcing decorator receives a null positional argument

- **Given** `T` implements `IEventSourced` and an `eventStore` is supplied
- **When** the EventSourcing decorator is constructed
- **Then** `Activator.CreateInstance` is called with the argument list `(store, eventStore, null, effectiveClock)` — the third argument is passed as a literal null

### Requirement: Builder scope limits

The system SHALL expose composition through `StoreWrapperBuilder` only for async bulk stores
(`IAsyncBulkStore<T>` in, `IAsyncBulkStore<T>` out) with `T` constrained to `AbstractModel, new()`, and
SHALL NOT include the optimistic-concurrency decorators in any generated chain.

#### Scenario: No synchronous composition entry point

- **Given** a consumer holding an `IBulkStore<T>` and wanting the sync `TimestampBulkStoreWrapper`, `AuditBulkStoreWrapper`, `SoftDeleteBulkStoreWrapper`, `SluggableBulkStoreWrapper` and `DefaultStoreWrapper` chain
- **When** they look for a builder overload accepting `IBulkStore<T>`
- **Then** none exists — the sync decorators must be composed by hand, in whatever order the consumer chooses

#### Scenario: Versioned entity gets no concurrency decorator

- **Given** `T` implements `IVersioned`
- **When** `StoreWrapperBuilder.Build` is called
- **Then** neither `VersionedStoreWrapper<T>` nor `AsyncVersionedStoreWrapper<T>` is added, so optimistic concurrency is not enforced unless the consumer wraps the store itself

### Requirement: Inner-store exposure

The system SHALL let callers reach the immediately wrapped store — and only that one level — via
`IStoreWrapper.GetInnerStore()` on every decorator, and additionally via
`IStoreWrapper<T>.GetInnerStoreAs<TInner>()` on the Audit, Timestamp, SoftDelete, Sluggable and Default
decorators, which returns `_innerStore as TInner` (null when the cast fails).

#### Scenario: Reaching the raw store through a multi-level chain

- **Given** a chain Default → Sluggable → SoftDelete → raw store
- **When** `GetInnerStore()` is called on the outermost `AsyncDefaultStoreWrapper`
- **Then** the `AsyncSluggableBulkStoreWrapper` is returned, not the raw store — reaching the raw store requires walking the chain one `GetInnerStore()` call at a time

#### Scenario: Typed access to a mismatched inner store

- **Given** a `TimestampStoreWrapper` whose inner store is an `InMemoryStore<T>`
- **When** `GetInnerStoreAs<DataBaseStore<DB, T>>()` is called
- **Then** null is returned rather than an exception being thrown

#### Scenario: Concurrency decorators expose only the non-generic accessor

- **Given** an `AsyncVersionedStoreWrapper<T>` or `VersionedStoreWrapper<T>`
- **When** the caller attempts `GetInnerStoreAs<TInner>()`
- **Then** the member does not exist — these two decorators implement only the non-generic `IStoreWrapper`, unlike every decorator in `Birko.Data.Patterns.Decorators`, which implements `IStoreWrapper<T>`

### Requirement: Constructor argument validation

The system SHALL throw `ArgumentNullException` from a decorator's constructor when the inner store is null,
and likewise when a required context (`IAuditContext` for the Audit decorators, `IDateTimeProvider` for the
Timestamp and SoftDelete decorators) is null. Because `StoreWrapperBuilder` constructs every decorator through
`Activator.CreateInstance`, that `ArgumentNullException` is observable as such only on direct construction: a
caller of `Build` receives it wrapped in a `TargetInvocationException`, so the parameter name is not on the
top-level exception.

#### Scenario: Null inner store

- **Given** a call to `new TimestampStoreWrapper<IStore<T>, T>(null, clock)`
- **When** the constructor runs
- **Then** an `ArgumentNullException` naming `innerStore` is thrown

#### Scenario: Null clock

- **Given** a call to `new AsyncSoftDeleteStoreWrapper<IAsyncStore<T>, T>(inner, null)`
- **When** the constructor runs
- **Then** an `ArgumentNullException` naming `clock` is thrown

#### Scenario: Null audit context

- **Given** a call to `new AuditStoreWrapper<IStore<T>, T>(inner, null)`
- **When** the constructor runs
- **Then** an `ArgumentNullException` naming `auditContext` is thrown

#### Scenario: Sluggable and Default decorators take no context

- **Given** a call to `new SluggableStoreWrapper<IStore<T>, T>(inner)` or `new DefaultStoreWrapper<IBulkStore<T>, T>(inner)`
- **When** the constructor runs
- **Then** only the inner store is validated — these decorators have no second constructor parameter

#### Scenario: The same validation reached through the builder

- **Given** `T` implements `ITimestamped` and `StoreWrapperBuilder.Build<T>(null)` is called
- **When** `Wrap` invokes `Activator.CreateInstance` for `AsyncTimestampBulkStoreWrapper<IAsyncBulkStore<T>, T>` with the null store as the first constructor argument
- **Then** the constructor's `ArgumentNullException` naming `innerStore` is raised but reaches the caller of `Build` as the `InnerException` of a `TargetInvocationException` — a `catch (ArgumentNullException)` around `Build` does not match

### Requirement: Timestamp stamping on single-entity writes

The system SHALL, on create, set `CreatedAt` and `UpdatedAt` to a single reading of `_clock.UtcNow` and set
`PrevUpdatedAt` to null; and on update, SHALL copy the entity's current `UpdatedAt` into `PrevUpdatedAt`
before overwriting `UpdatedAt` with `_clock.UtcNow`. Deletes SHALL pass through unstamped.

#### Scenario: Create overwrites caller-supplied timestamps

- **Given** an entity whose `CreatedAt` is preset to `2020-01-01` and `PrevUpdatedAt` to a non-null value
- **When** `TimestampStoreWrapper.Create(data)` is called with a clock reading `2026-05-05T08:00:00Z`
- **Then** `CreatedAt` and `UpdatedAt` are both `2026-05-05T08:00:00Z`, `PrevUpdatedAt` is null, and the mutated entity is what reaches the inner store

#### Scenario: Update chains the previous timestamp

- **Given** an entity whose `UpdatedAt` is `2026-05-05T08:00:00Z`
- **When** `AsyncTimestampStoreWrapper.UpdateAsync(data)` is called with a clock reading `2026-05-05T09:00:00Z`
- **Then** `PrevUpdatedAt` becomes `2026-05-05T08:00:00Z` and `UpdatedAt` becomes `2026-05-05T09:00:00Z`

#### Scenario: Delete is not timestamped

- **Given** any `ITimestamped` entity
- **When** `TimestampStoreWrapper.Delete(data)` is called
- **Then** the call is forwarded to `_innerStore.Delete(data)` with no timestamp mutation

### Requirement: Timestamp stamping on bulk and filter-based writes

The system SHALL read `_clock.UtcNow` once per bulk or filter-based call and apply that single value to
every affected entity, SHALL apply the caller's `Action<T>` BEFORE computing `PrevUpdatedAt`/`UpdatedAt`, and
SHALL — on the `PropertyUpdate<T>` overload only — append an `UpdatedAt` assignment without maintaining
`PrevUpdatedAt`.

#### Scenario: One clock reading for a whole batch

- **Given** a collection of 100 entities
- **When** `TimestampBulkStoreWrapper.Create(items)` is called
- **Then** all 100 entities receive an identical `CreatedAt` and `UpdatedAt`, taken from one `_clock.UtcNow` read before the projection

#### Scenario: Caller action runs before the timestamp is computed

- **Given** a caller action `item => item.UpdatedAt = DateTime.MinValue`
- **When** `TimestampBulkStoreWrapper.Update(filter, action)` is invoked
- **Then** the action runs first, so `PrevUpdatedAt` captures `DateTime.MinValue` (the value the action just wrote) rather than the value that was in storage, and `UpdatedAt` is then set to the batch `now`

#### Scenario: PropertyUpdate path leaves PrevUpdatedAt untouched

- **Given** a `PropertyUpdate<T>` setting only a business field
- **When** `AsyncTimestampBulkStoreWrapper.UpdateAsync(filter, updates)` is called
- **Then** `updates.Set(x => x.UpdatedAt, _clock.UtcNow)` is appended and forwarded, and no `PrevUpdatedAt` assignment is added — unlike the `Action<T>` overload, the previous-timestamp chain is not maintained on this path

#### Scenario: Bulk projection is lazily evaluated

- **Given** `TimestampBulkStoreWrapper.Update(items)` where `items` is a lazily enumerable source
- **When** the inner store enumerates the projected sequence twice
- **Then** the mutation lambda runs twice per entity, so on the second pass `PrevUpdatedAt` is set to the `UpdatedAt` written by the first pass and both fields end up equal to the batch `now`

### Requirement: Audit stamping

The system SHALL set both `CreatedBy` and `UpdatedBy` to `_auditContext.CurrentUserId` on create, set only
`UpdatedBy` on update (leaving `CreatedBy` as-is), and SHALL NOT stamp anything on delete.

#### Scenario: Create stamps both audit fields

- **Given** an `IAuditContext` whose `CurrentUserId` is a known Guid
- **When** `AuditStoreWrapper.Create(data)` is called
- **Then** `data.CreatedBy` and `data.UpdatedBy` are both that Guid

#### Scenario: Update preserves the creator

- **Given** a stored entity whose `CreatedBy` is user A, and an audit context whose `CurrentUserId` is user B
- **When** `AsyncAuditStoreWrapper.UpdateAsync(data)` is called
- **Then** `UpdatedBy` becomes user B and `CreatedBy` is left untouched

#### Scenario: Unauthenticated context stamps null

- **Given** an `IAuditContext` whose `CurrentUserId` is null
- **When** an entity is created through the audit decorator
- **Then** `CreatedBy` and `UpdatedBy` are both set to null, overwriting any value the caller had assigned

#### Scenario: Delete is not audited

- **Given** any `IAuditable` entity
- **When** `AuditBulkStoreWrapper.Delete(data)` or `Delete(filter)` is called
- **Then** the call is forwarded unchanged and no audit field is written

### Requirement: Audit stamping overrides caller-supplied audit values on filter-based updates

The system SHALL, on the `Action<T>` filter-update overload, invoke the caller's action first and then
assign `UpdatedBy` — so the decorator's value wins; and SHALL, on the `PropertyUpdate<T>` overload, mutate
the caller's `PropertyUpdate<T>` instance in place by appending an `UpdatedBy` assignment before forwarding it.

#### Scenario: Caller action cannot set UpdatedBy

- **Given** a caller action `item => item.UpdatedBy = someOtherUser`
- **When** `AuditBulkStoreWrapper.Update(filter, action)` is called with an audit context for `currentUser`
- **Then** the persisted `UpdatedBy` is `currentUser`, because the decorator's assignment runs after the action

#### Scenario: The caller's PropertyUpdate instance is mutated

- **Given** a `PropertyUpdate<T> updates` object held by the caller
- **When** `AsyncAuditBulkStoreWrapper.UpdateAsync(filter, updates)` is called
- **Then** `updates.Set(x => x.UpdatedBy, ...)` has appended an assignment to that same instance, so reusing `updates` for a second call carries the previous `UpdatedBy` assignment (and, when a Timestamp decorator is also in the chain, an additional `UpdatedAt` assignment) into the second call

### Requirement: Soft-delete read filtering

The system SHALL exclude entities whose `DeletedAt` is non-null from every read and count path: filter-based
reads and counts by appending `x.DeletedAt == null` via `SoftDeleteFilter.CombineWithNotDeleted`, and
read-by-Guid by fetching the entity and returning null when its `DeletedAt` is non-null.

#### Scenario: Filter-based read of a deleted row

- **Given** a row with `Name == "widget"` and a non-null `DeletedAt`
- **When** `SoftDeleteStoreWrapper.Read(x => x.Name == "widget")` is called
- **Then** the inner store receives the predicate `x => x.Name == "widget" && x.DeletedAt == null` and no entity is returned

#### Scenario: Null filter becomes a not-deleted-only predicate

- **Given** a call to `SoftDeleteBulkStoreWrapper.Read()` (which delegates to `Read(null, null, null, null)`)
- **When** the filter reaches `SoftDeleteFilter.CombineWithNotDeleted(null)`
- **Then** `ExpressionParameterReplacer.AndAlso` returns the right-hand lambda unchanged, so the inner store receives exactly `x => x.DeletedAt == null` instead of a null filter

#### Scenario: Read by Guid of a deleted row still hits storage

- **Given** a soft-deleted row with a known Guid
- **When** `AsyncSoftDeleteStoreWrapper.ReadAsync(guid)` is called
- **Then** the inner store is queried and returns the full entity, which the decorator then discards, returning null — the not-deleted condition is not pushed down on this path

#### Scenario: Count excludes deleted rows

- **Given** three active rows and two soft-deleted rows
- **When** `SoftDeleteStoreWrapper.Count()` is called
- **Then** the inner count is taken over `x => x.DeletedAt == null` and 3 is returned

#### Scenario: Combined predicates share one parameter

- **Given** any user filter combined with the not-deleted condition
- **When** the combination is built
- **Then** it is built with `ExpressionParameterReplacer.AndAlso` (parameter rewriting) rather than `Expression.Invoke`, so the resulting tree contains no `InvocationExpression` and remains translatable by the backend filter parsers

### Requirement: Soft delete converts deletes into updates

The system SHALL, on every delete path of the soft-delete decorators, set `DeletedAt` to `_clock.UtcNow` and
issue an UPDATE against the inner store instead of a delete — using the inner single-entity update for
`Delete(T)`, the inner bulk update for `Delete(IEnumerable<T>)`, and the inner
filter-plus-`Action<T>` update for `Delete(filter)`.

#### Scenario: Entity delete becomes an update

- **Given** an active entity and a clock reading `2026-06-01T00:00:00Z`
- **When** `SoftDeleteStoreWrapper.Delete(data)` is called
- **Then** `data.DeletedAt` is `2026-06-01T00:00:00Z` and `_innerStore.Update(data)` is invoked — with no `storeDelegate` forwarded — and no row is physically removed

#### Scenario: Filter delete stamps only rows that are not already deleted

- **Given** two active rows and one already-deleted row all matching `x => x.CategoryId == c`
- **When** `AsyncSoftDeleteBulkStoreWrapper.DeleteAsync(x => x.CategoryId == c)` is called
- **Then** the inner filter-update predicate is `x => x.CategoryId == c && x.DeletedAt == null`, so only the two active rows are stamped and the already-deleted row keeps its original `DeletedAt`

#### Scenario: Entity-collection delete re-stamps already-deleted rows

- **Given** a collection containing an entity whose `DeletedAt` is already `2026-01-01`
- **When** `SoftDeleteBulkStoreWrapper.Delete(items)` is called with a clock reading `2026-06-01`
- **Then** that entity's `DeletedAt` is overwritten with `2026-06-01`, because the entity-based delete paths apply no not-deleted guard

#### Scenario: One clock reading per bulk delete

- **Given** a collection of entities
- **When** `SoftDeleteBulkStoreWrapper.Delete(items)` or `Delete(filter)` is called
- **Then** `_clock.UtcNow` is read once and the same instant is written to every affected row

### Requirement: Soft-delete write paths do not guard against deleted state

The system SHALL force `DeletedAt` to null on every create path, and SHALL forward entity-based updates to
the inner store without any not-deleted check — so an update can be applied to, or can resurrect, a
soft-deleted row.

#### Scenario: Create clears any deleted marker

- **Given** an entity object whose `DeletedAt` was set to a non-null value
- **When** `AsyncSoftDeleteStoreWrapper.CreateAsync(data)` is called
- **Then** `DeletedAt` is reset to null before the inner create

#### Scenario: Updating a soft-deleted entity succeeds

- **Given** a soft-deleted row loaded out-of-band (the decorator's own reads cannot return it)
- **When** `SoftDeleteStoreWrapper.Update(data)` is called
- **Then** the update is forwarded unchanged, and if `data.DeletedAt` was set to null the row becomes visible to reads again

#### Scenario: Filter-based update excludes deleted rows

- **Given** an already-deleted row matching the caller's filter
- **When** `SoftDeleteBulkStoreWrapper.Update(filter, updateAction)` or `Update(filter, updates)` is called
- **Then** the predicate is combined with `DeletedAt == null` and the deleted row is not modified — unlike the entity-based update path

#### Scenario: No escape hatch for reading deleted rows

- **Given** a soft-delete decorator wrapping a store
- **When** a caller needs to list soft-deleted rows
- **Then** no member of the decorator can return them — the not-deleted condition is unconditional on all read/count paths and the caller must reach the inner store via `GetInnerStore()`

### Requirement: Slug normalization

The system SHALL normalize slug source text in `SlugGenerator.Normalize` by lowercasing with the invariant
culture, stripping Unicode non-spacing marks, replacing each whitespace character, em dash, en dash,
underscore or forward slash with a hyphen, deleting every remaining character outside `[a-z0-9-]`,
collapsing runs of two or more hyphens into one, and trimming leading and trailing hyphens; and SHALL
return `string.Empty` for a null, empty or whitespace-only input.

#### Scenario: Accented multi-word title

- **Given** the input `"Príklad — Ťažký Názov"`
- **When** `Normalize` is called
- **Then** `"priklad-tazky-nazov"` is returned

#### Scenario: Punctuation is deleted, not hyphenated

- **Given** the input `"Widget v2.0 (new!)"`
- **When** `Normalize` is called
- **Then** the `.`, `(`, `)` and `!` characters are removed rather than converted to hyphens, yielding `"widget-v20-new"`

#### Scenario: Underscores and slashes become hyphens

- **Given** the input `"a_b/c d"`
- **When** `Normalize` is called
- **Then** `"a-b-c-d"` is returned

#### Scenario: Non-Latin script normalizes to empty

- **Given** the input `"Пример"` or `"製品"`
- **When** `Normalize` is called
- **Then** every character is stripped by the `[^a-z0-9\-]` filter and `string.Empty` is returned

#### Scenario: Whitespace-only input

- **Given** the input `"   "` or null
- **When** `Normalize` is called
- **Then** `string.Empty` is returned

### Requirement: Slug uniqueness suffixing

The system SHALL, in `SlugGenerator.EnsureUnique` / `EnsureUniqueAsync`, substitute the `fallback` value
(default `"item"`) when the base slug is empty, then probe the caller-supplied `isSlugTaken` predicate and,
while it reports the candidate taken, append an incrementing numeric suffix that starts at `2`.

#### Scenario: First collision produces suffix 2

- **Given** `"widget"` is already taken and `"widget-2"` is free
- **When** `EnsureUnique("widget", isTaken)` is called
- **Then** `"widget-2"` is returned — the suffix counter is incremented before first use, so `"widget-1"` is never produced

#### Scenario: Repeated collisions walk upward

- **Given** `"widget"`, `"widget-2"` and `"widget-3"` are all taken
- **When** `EnsureUnique("widget", isTaken)` is called
- **Then** `"widget-4"` is returned

#### Scenario: Empty base slug falls back

- **Given** a base slug of `string.Empty` and nothing taken
- **When** `EnsureUnique("", isTaken)` is called with the default fallback
- **Then** `"item"` is returned

#### Scenario: Suffixed candidates are not re-normalized

- **Given** a fallback that is taken
- **When** the suffix is appended
- **Then** the candidate is composed as `$"{baseSlug}-{suffix}"` directly, without a further `Normalize` pass

### Requirement: Slug resolution on writes

The system SHALL, before every single-entity create and update, choose the slug source as the entity's own
`Slug` when it is not null-or-whitespace and otherwise `GetSlugSource()`, normalize it, and resolve
uniqueness by reading the inner store with the predicate `entity => entity.Slug == slug`, treating a
candidate as taken only when a row exists AND its `Guid` differs from the excluded id (null on create, the
entity's own `Guid` on update).

#### Scenario: Slug derived from the source when unset

- **Given** an entity with `Slug` null and `GetSlugSource()` returning `"Wireless Mouse"`
- **When** `SluggableStoreWrapper.Create(data)` is called and no row holds that slug
- **Then** `data.Slug` is set to `"wireless-mouse"` before the inner create

#### Scenario: Explicit slug wins over the source

- **Given** an entity with `Slug = "Custom Slug"` and `GetSlugSource()` returning `"Wireless Mouse"`
- **When** the entity is created
- **Then** the slug is normalized from the explicit value to `"custom-slug"`; `GetSlugSource()` is not consulted

#### Scenario: An entity keeps its own slug on update

- **Given** a stored entity with `Slug = "widget"` and the same Guid
- **When** `AsyncSluggableStoreWrapper.UpdateAsync(data)` is called with `data.Slug` still `"widget"`
- **Then** the probe finds the row but its `Guid` equals `excludeId`, so the candidate is not considered taken and the slug stays `"widget"` with no suffix

#### Scenario: Unresolvable source falls back

- **Given** an entity with `Slug` null and `GetSlugSource()` returning null
- **When** the entity is created
- **Then** the base slug normalizes to empty and the resolved slug is `"item"` (or `"item-2"`, `"item-3"`, … if already taken)

### Requirement: Slug batch uniqueness tracking

The system SHALL materialize a bulk slug write's source collection exactly once before mutating it, resolve
each item's slug sequentially against the shared inner store, and — for `AsyncSluggableBulkStoreWrapper`
create and update, and `SluggableBulkStoreWrapper` create — additionally treat a slug already claimed
earlier in the same batch as taken using an `OrdinalIgnoreCase` set. `SluggableBulkStoreWrapper.Update`
SHALL NOT perform this within-batch tracking.

#### Scenario: Two new items with the same title get distinct slugs

- **Given** a batch of two entities both resolving to base slug `"widget"`, with no existing row holding it
- **When** `SluggableBulkStoreWrapper.Create(items)` or `AsyncSluggableBulkStoreWrapper.CreateAsync(items)` is called
- **Then** the first receives `"widget"` and the second, blocked by the in-batch set, receives `"widget-2"`

#### Scenario: In-batch collision is case-insensitive

- **Given** a batch where one item resolves to `"widget"` and a later item's inner-store probe would pass
- **When** the batch is created
- **Then** the tracking set built with `StringComparer.OrdinalIgnoreCase` still reports the candidate taken

#### Scenario: Async batch update tracks in-batch slugs, sync does not

- **Given** a batch of two existing entities with different Guids, both re-resolving to base slug `"widget"`, and no other row holding that slug
- **When** `AsyncSluggableBulkStoreWrapper.UpdateAsync(items)` is called
- **Then** the first keeps `"widget"` and the second gets `"widget-2"`
- **When** `SluggableBulkStoreWrapper.Update(items)` is called for the same batch
- **Then** `ResolveSlug(item, item.Guid)` is invoked with no batch set, so both items resolve to `"widget"` and two rows carry the same slug

#### Scenario: Lazy source is materialized before mutation

- **Given** a lazily projected `IEnumerable<T>` that yields fresh object instances on each enumeration
- **When** a bulk slug create or update is invoked
- **Then** the source is materialized via `data as IReadOnlyList<T> ?? data.ToList()` first, so the slug-mutated instances are the ones handed to the inner store

#### Scenario: Slug resolution failure aborts the write

- **Given** a batch whose second item's inner-store probe throws
- **When** `AsyncSluggableBulkStoreWrapper.UpdateAsync(items)` is called
- **Then** the exception propagates from the sequential `await` loop and `_innerStore.UpdateAsync` is never reached

#### Scenario: Filter-based writes bypass slug resolution

- **Given** a `PropertyUpdate<T>` or `Action<T>` that assigns `Slug`
- **When** `SluggableBulkStoreWrapper.Update(filter, updates)` or `Update(filter, updateAction)` is called
- **Then** the call is forwarded to the inner store verbatim, with no normalization and no uniqueness check

### Requirement: Single-default enforcement

The system SHALL, whenever an entity is written with `IsDefault == true` through the Default decorator's
single-entity create, update or save path, first read all rows matching `e => e.IsDefault` from the inner
store, clear the flag on every row whose `Guid` differs from the written entity's, and write those cleared
rows back as a bulk update — skipping the write entirely when no other default exists.

#### Scenario: Promoting a new default demotes the old one

- **Given** exactly one stored row with `IsDefault = true`
- **When** an entity with `IsDefault = true` is created through `DefaultStoreWrapper.Create`
- **Then** the stored row's `IsDefault` is set to false and persisted via `_innerStore.Update(toUpdate)`, then the new entity is created

#### Scenario: No existing default means no corrective write

- **Given** no stored row has `IsDefault = true`
- **When** an entity with `IsDefault = true` is created
- **Then** `UnsetOtherDefaults` finds an empty candidate list and returns without calling the inner update

#### Scenario: Writing a non-default entity triggers nothing

- **Given** an entity with `IsDefault = false`
- **When** it is created or updated through the Default decorator
- **Then** no probe read is issued and no other row is modified

#### Scenario: Single-result reads bypass the bulk Read overload

- **Given** the Default decorator wrapping an `IBulkStore<T>`, where the bulk `Read(filter, orderBy, limit, offset)` hides the single-result `Read(filter)`
- **When** `DefaultStoreWrapper.Read(guid)` or `Read(filter)` is called
- **Then** the inner store is explicitly cast to `IReadStore<T>` (async: `IAsyncReadStore<T>`) so the single-entity overload is reached and a `T?` is returned

### Requirement: Default-flag collapsing on batch writes

The system SHALL, when a batch write contains more than one entity with `IsDefault == true`, clear the flag
on all but the LAST such entity in enumeration order, and on batch update SHALL exclude every Guid present
in the batch from the corrective clearing pass.

#### Scenario: Three defaults in one create batch

- **Given** a batch of five entities of which the 1st, 3rd and 5th have `IsDefault = true`
- **When** `DefaultStoreWrapper.Create(items)` is called
- **Then** stored defaults are cleared first, then the 1st and 3rd entities have `IsDefault` set to false and only the 5th is persisted as default

#### Scenario: Batch update protects its own rows

- **Given** a batch update containing rows A, B and C where B and C are marked default
- **When** `DefaultStoreWrapper.Update(items)` is called
- **Then** B is demoted, C stays default, and `UnsetOtherDefaults(C.Guid, {A,B,C})` clears only stored defaults outside the batch — A and B are not written twice by the corrective pass

#### Scenario: Create-batch exclusion uses a null Guid

- **Given** `Create(items)` calling `UnsetOtherDefaults(null)`
- **When** the candidate list `allDefaults.Where(e => e.Guid != excludeGuid)` is evaluated with `excludeGuid == null`
- **Then** any existing default row whose `Guid` is null is filtered OUT of the clearing pass and keeps `IsDefault = true`

### Requirement: Default-flag enforcement on filter-based updates

The system SHALL enforce the single-default invariant on the `Action<T>` filter-update overload — reading
matched rows and applying the corrective pass per row — but SHALL forward the `PropertyUpdate<T>` overload
straight to the inner store with no enforcement. The async and sync implementations of the `Action<T>`
overload SHALL differ in how the corrective pass is sequenced.

#### Scenario: PropertyUpdate can set IsDefault on many rows

- **Given** a `PropertyUpdate<T>` with `Set(x => x.IsDefault, true)` and a filter matching four rows
- **When** `AsyncDefaultStoreWrapper.UpdateAsync(filter, updates)` or `DefaultStoreWrapper.Update(filter, updates)` is called
- **Then** the call is forwarded verbatim and four rows end up with `IsDefault = true`, violating the invariant the decorator exists to enforce

#### Scenario: Async Action overload reads then writes row by row

- **Given** a filter matching three rows and an action setting `IsDefault = true`
- **When** `AsyncDefaultStoreWrapper.UpdateAsync(filter, updateAction, ct)` is called
- **Then** the rows are first read via `_innerStore.ReadAsync(filter, null, null, null, ct)`, then for each row the action is applied, `UnsetOtherDefaultsAsync(row.Guid)` runs, and `_innerStore.UpdateAsync(row)` is issued individually — so each successive row demotes the previously promoted one and only the last row remains default

#### Scenario: Sync Action overload re-enters the inner store from inside its own update callback

- **Given** the same filter and action
- **When** `DefaultStoreWrapper.Update(filter, updateAction)` is called
- **Then** the corrective `UnsetOtherDefaults(item.Guid)` — itself an inner-store read plus bulk update — executes inside the callback the inner store invokes while processing its own filter-based update, rather than after a completed read pass as in the async implementation

### Requirement: Optimistic concurrency version stamping

The system SHALL set `Version` to `1` on every create through the versioned decorators, regardless of the
value the caller supplied.

#### Scenario: Create resets a caller-supplied version

- **Given** an entity with `Version = 47`
- **When** `VersionedStoreWrapper.Create(data)` is called
- **Then** `data.Version` is `1` when the inner store receives it

### Requirement: Optimistic concurrency conflict detection

The system SHALL, before forwarding an update, read the stored entity by `data.Guid ?? Guid.Empty` and throw
`ConcurrentUpdateException(typeof(T), guid, data.Version)` when either no row was found or the stored
`Version` differs from `data.Version`; and SHALL otherwise increment `data.Version` before forwarding. This
check is a non-atomic read-check-write at the decorator level.

#### Scenario: Matching version proceeds and increments

- **Given** a stored entity with `Version = 3` and an in-memory copy with `Version = 3`
- **When** `AsyncVersionedStoreWrapper.UpdateAsync(data)` is called
- **Then** `data.Version` becomes `4` and the inner update is issued

#### Scenario: Stale version is rejected

- **Given** a stored entity whose `Version` has advanced to `4` and an in-memory copy with `Version = 3`
- **When** `UpdateAsync(data)` is called
- **Then** a `ConcurrentUpdateException` is thrown whose `EntityType` is `typeof(T)`, `EntityId` is the entity's Guid, `ExpectedVersion` is `3`, and whose message reads `Concurrency conflict on {TypeName} (Id: {guid}). Expected version 3 but the entity has been modified.`; `data.Version` is left at `3` and no inner update is issued

#### Scenario: Missing row is treated as a conflict

- **Given** an entity whose Guid no longer exists in storage (or whose `Guid` is null, so `Guid.Empty` is read)
- **When** `Update` / `UpdateAsync` is called
- **Then** a `ConcurrentUpdateException` is thrown rather than the update silently proceeding

#### Scenario: Version is incremented before the inner write can fail

- **Given** an inner store whose update throws
- **When** `UpdateAsync(data)` is called with a matching version
- **Then** `data.Version` has already been incremented when the inner exception surfaces, leaving the in-memory entity one version ahead of storage

#### Scenario: Concurrent updaters can both pass the check

- **Given** two callers holding the same `Version = 3` copy
- **When** both invoke `UpdateAsync` and their reads interleave before either write lands
- **Then** both reads observe `Version = 3`, both checks pass, and both writes proceed — the decorator narrows but does not close the lost-update window, since the expected version is not part of the inner store's update predicate

#### Scenario: Deletes are not version-checked

- **Given** a stale in-memory copy of an entity
- **When** `Delete` / `DeleteAsync` is called on the versioned decorator
- **Then** the call is forwarded to the inner store unchanged with no version comparison

### Requirement: Save routing and return value

The system SHALL treat a null or `Guid.Empty` `Guid` as "create" and any other value as "update" on every
decorator's `Save`/`SaveAsync`, and SHALL return the created Guid differently per decorator:
`AuditStoreWrapper`, `TimestampStoreWrapper`, `SoftDeleteStoreWrapper`, `SluggableStoreWrapper`,
`DefaultStoreWrapper`, `AsyncSluggableStoreWrapper`, `AsyncDefaultStoreWrapper` and both versioned
decorators return the value the inner create produced, while `AsyncAuditStoreWrapper`,
`AsyncTimestampStoreWrapper` and `AsyncSoftDeleteStoreWrapper` discard it and return `data.Guid ?? Guid.Empty`.

#### Scenario: Save with an empty Guid creates

- **Given** an entity whose `Guid` is `Guid.Empty`
- **When** `SluggableStoreWrapper.Save(data)` is called
- **Then** the decorator's own `Create` path runs (slug resolved) and the inner create's Guid is returned

#### Scenario: Save with a populated Guid updates

- **Given** an entity with a non-empty `Guid`
- **When** `AsyncTimestampStoreWrapper.SaveAsync(data)` is called
- **Then** the decorator's `UpdateAsync` path runs, stamping `PrevUpdatedAt`/`UpdatedAt`

#### Scenario: Async audit/timestamp/soft-delete Save loses the created Guid

- **Given** an inner store that returns a newly generated Guid from `CreateAsync` without assigning it to the passed entity
- **When** `AsyncAuditStoreWrapper.SaveAsync(data)`, `AsyncTimestampStoreWrapper.SaveAsync(data)` or `AsyncSoftDeleteStoreWrapper.SaveAsync(data)` is called with `data.Guid == null`
- **Then** the returned value is `Guid.Empty`, because the create result is awaited and discarded and `data.Guid ?? Guid.Empty` is returned instead — whereas the synchronous `AuditStoreWrapper.Save`, `TimestampStoreWrapper.Save` and `SoftDeleteStoreWrapper.Save` return the inner create's Guid for the same input

#### Scenario: Default decorator's Save bypasses its own Create/Update

- **Given** an entity with `IsDefault = true` and a null `Guid`
- **When** `AsyncDefaultStoreWrapper.SaveAsync(data)` is called
- **Then** `UnsetOtherDefaultsAsync(data.Guid)` runs and then `_innerStore.CreateAsync` is called directly, so the decorator's own `CreateAsync` (and its second probe) is not re-entered

### Requirement: Clock abstraction

The system SHALL supply the current instant to the decorators through `IDateTimeProvider`, exposing
`UtcNow`, `OffsetUtcNow` and `Today`, with `SystemDateTimeProvider` reading the machine clock and
`TestDateTimeProvider` holding a settable instant that defaults to `2026-01-01T12:00:00+00:00`.

#### Scenario: System provider derives Today from UtcNow

- **Given** a `SystemDateTimeProvider`
- **When** `Today` is read
- **Then** it is `DateOnly.FromDateTime(DateTime.UtcNow)` — the UTC date, not the local one

#### Scenario: Test provider freezes time

- **Given** a `TestDateTimeProvider` with no initial time
- **When** `UtcNow` is read twice with no intervening call
- **Then** both reads return the identical instant `2026-01-01T12:00:00Z`

#### Scenario: Test provider is advanced and set

- **Given** a `TestDateTimeProvider`
- **When** `Advance(TimeSpan.FromDays(1))` then `SetTime(new DateTimeOffset(2027, 3, 4, 5, 6, 7, TimeSpan.Zero))` are called
- **Then** `UtcNow` first reports `2026-01-02T12:00:00Z` and then `2027-03-04T05:06:07Z`, and `Today` tracks the same date

### Requirement: Concurrency exception surface

The system SHALL expose `ConcurrentUpdateException` with nullable `EntityType`, `EntityId` and
`ExpectedVersion` properties that are populated only by the `(Type, Guid, long)` constructor, and SHALL
default the parameterless constructor's message to `"The entity has been modified by another process."`.

#### Scenario: Diagnostic constructor populates the properties

- **Given** `new ConcurrentUpdateException(typeof(Order), id, 3)`
- **When** the exception is inspected
- **Then** `EntityType`, `EntityId` and `ExpectedVersion` are `typeof(Order)`, `id` and `3`

#### Scenario: Message-only constructor leaves the properties null

- **Given** `new ConcurrentUpdateException("custom message")` or `new ConcurrentUpdateException()`
- **When** the exception is inspected
- **Then** `EntityType`, `EntityId` and `ExpectedVersion` are all null
