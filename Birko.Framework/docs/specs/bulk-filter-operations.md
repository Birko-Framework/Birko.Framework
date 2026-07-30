---
area: bulk-filter-operations
generated-at: f3ac6755e788bc3e4693d27d37c583d67532a816
generated-on: 2026-07-30
sources:
  - ../Birko.Data.SQL/Stores/AsyncDataBaseBulkStore.cs
  - ../Birko.Data.SQL/Stores/DataBaseBulkStore.cs
  - ../Birko.Data.Stores/AbstractAsyncBulkStore.cs
  - ../Birko.Data.Stores/AbstractBulkStore.cs
  - ../Birko.Data.Stores/IAsyncBulkStore.cs
  - ../Birko.Data.Stores/IBulkStore.cs
  - ../Birko.Data.Stores/PropertyUpdate.cs
shaped-by: []
---

# Bulk filter-based Update/Delete and Read semantics

## Purpose

A *bulk store* is a Birko data store that can act on many entities at once: read a filtered,
ordered, paged collection; create/update/delete a whole collection; and — the part this capability
exists for — **update or delete every entity matching a LINQ predicate without the caller ever
materialising those entities**. Two update flavours are offered: `Update(filter, Action<T>)`, a
portable read-modify-save loop, and `Update(filter, PropertyUpdate<T>)`, a declarative set of
property assignments that a backend is free to translate into one native statement (`UPDATE … SET …
WHERE …`, MongoDB `$set`, ElasticSearch `UpdateByQuery`).

The abstract bases (`AbstractBulkStore<T>` / `AbstractAsyncBulkStore<T>`) supply portable fallbacks
that work on any backend; the SQL bulk stores (`DataBaseBulkStore<DB,T>` /
`AsyncDataBaseBulkStore<DB,T>`) replace two of those fallbacks with single-round-trip SQL. Those two
paths are **not observationally equivalent**, and the differences (how many rows are written, which
per-entity hooks run, which inputs throw) are the substance of this document. Everything that layers
on stores — repositories, the decorator/wrapper chains, tenant scoping, localisation — inherits
whichever semantics the concrete store implements.

## Requirements

### Requirement: Bulk read surface

The system SHALL expose, on every bulk store, a collection-returning `Read` overload taking an
optional filter, optional `OrderBy<T>`, optional limit and optional offset, plus a no-argument
`Read()` that SHALL delegate to that overload with all four arguments null.

#### Scenario: No-argument read returns everything

- **Given** a store deriving from `AbstractBulkStore<T>` (or `DataBaseBulkStore<DB,T>`)
- **When** `Read()` is called
- **Then** the store calls `Read(null, null, null, null)`, which reaches `ReadCore` with a null
  filter, null order, null limit and null offset, so no predicate, ordering or paging is applied

#### Scenario: Filter, order and paging are passed through untouched

- **Given** an `AbstractBulkStore<T>` subclass
- **When** `Read(x => x.Name == "a", orderBy, 10, 20)` is called
- **Then** `EnsureInitialized()` runs first, and the same four arguments are handed to the abstract
  `ReadCore(filter, orderBy, limit, offset)` for the concrete store to translate

#### Scenario: SQL bulk read delegates to the connector's Select

- **Given** a `DataBaseBulkStore<DB,T>` with a non-null `Connector`
- **When** `Read(filter, orderBy, limit, offset)` is called
- **Then** `ReadCore` calls `Connector.Select(typeof(T), filter as LambdaExpression,
  orderBy?.ToDictionary(), limit, offset)` and returns `.OfType<T>()` of the result — unmaterialised:
  `Select` is an iterator method, so no query has run when `Read` returns, and the connection and
  `DbDataReader` are opened inside `RunReaderCommand` on the thread that later enumerates the sequence
  and closed only when that enumerator is disposed

#### Scenario: SQL bulk read with no connector returns an empty sequence

- **Given** a `DataBaseBulkStore<DB,T>` whose `Connector` is null
- **When** `Read(filter)` is called
- **Then** `ReadCore` returns `Enumerable.Empty<T>()` — no exception is thrown

### Requirement: Single-result reads on a bulk store require ReadFirst

The system SHALL provide `ReadFirst(filter)` / `ReadFirstAsync(filter, ct)` as the single-result
counterpart of the collection `Read`/`ReadAsync`, because declaring the bulk overload hides the
inherited single-result `Read(filter)` from C# member lookup on bulk-store types.

#### Scenario: Read(filter) on a bulk store binds to the collection overload

- **Given** a variable typed as `AbstractBulkStore<T>` (or `IBulkReadStore<T>`)
- **When** the caller writes `store.Read(x => x.Guid == g)`
- **Then** the call binds to `IEnumerable<T> Read(filter, orderBy, limit, offset)` — the collection
  overload — and not to `IReadStore<T>.Read(filter)`, which returns `T?`

#### Scenario: ReadFirst resolves to the single-result base implementation

- **Given** an `AbstractBulkStore<T>` subclass
- **When** `ReadFirst(filter)` is called
- **Then** it invokes `base.Read(filter)` on `AbstractStore<T>`, which runs `EnsureInitialized()` and
  the single-result `ReadCore(filter)`, returning `T?`

#### Scenario: SQL ReadFirst issues a LIMIT 1 select

- **Given** an `AsyncDataBaseBulkStore<DB,T>`
- **When** `ReadFirstAsync(filter)` is called
- **Then** it invokes `base.ReadAsync(filter, ct)` on `AsyncDataBaseStore<DB,T>`, whose single-result
  `ReadCoreAsync` passes `limit = 1` **only** on the `AsyncConnector` branch; with a sync-only connector
  it calls `Connector.Select(typeof(T), filter as LambdaExpression)` with no limit, no order and no
  offset and returns the first item that is a `T`. The store's own comment on `ReadFirstAsync` states
  "LIMIT 1 via ReadCoreAsync" for both paths. The synchronous `DataBaseBulkStore<DB,T>.ReadFirst` does
  select with `limit = 1` and `FirstOrDefault()`

#### Scenario: Interface default implementation casts through IReadStore

- **Given** a type implementing `IBulkReadStore<T>` that does not declare `ReadFirst`
- **When** `ReadFirst(filter)` is called
- **Then** the default interface implementation executes `((IReadStore<T>)this).Read(filter)`

### Requirement: Filter-based update via an action is read-modify-save

The system SHALL implement `Update(Expression<Func<T,bool>> filter, Action<T> updateAction)` by
reading all matching entities into memory with `ToList()`, invoking `updateAction` on each, and
persisting each one individually through the single-entity `Update(item)`.

#### Scenario: Every matching entity is written back one by one

- **Given** a bulk store whose filter matches three entities
- **When** `Update(filter, e => e.Name = "x")` is called
- **Then** the store reads the three entities via `Read(filter, null, null, null)`, materialises them
  with `ToList()`, and calls the single-entity `Update` three times — once per entity

#### Scenario: No matches performs no writes

- **Given** a bulk store whose filter matches nothing
- **When** `Update(filter, action)` is called
- **Then** the materialised list is empty, `updateAction` is never invoked and no `Update` call is made

#### Scenario: Async variant awaits each per-entity update sequentially

- **Given** an `AbstractAsyncBulkStore<T>` (or `AsyncDataBaseBulkStore<DB,T>`)
- **When** `UpdateAsync(filter, action, ct)` is called
- **Then** the matching entities are awaited from `ReadAsync(filter, null, null, null, ct)`,
  materialised, and each is awaited through `UpdateAsync(item, ct: ct)` in sequence — never in parallel

#### Scenario: The SQL bulk store keeps the same read-modify-save action path

- **Given** a `DataBaseBulkStore<DB,T>`
- **When** `Update(filter, Action<T>)` is called
- **Then** it uses the identical read-then-per-entity-`Update` loop as the abstract base — there is no
  native translation for the action overload

### Requirement: Portable PropertyUpdate fallback rewrites whole entities

The system SHALL, in `AbstractBulkStore<T>` / `AbstractAsyncBulkStore<T>`, implement
`Update(filter, PropertyUpdate<T>)` by delegating to the action overload with
`entity => updates.ApplyTo(entity)` — that is, by reading each matching entity, mutating it via
reflection and re-saving the **whole entity**.

#### Scenario: Named properties are applied via reflection then the entity is saved

- **Given** an `AbstractBulkStore<T>` and `new PropertyUpdate<T>().Set(x => x.Name, "n")`
- **When** `Update(filter, updates)` is called
- **Then** each matching entity is read, `ApplyTo` sets `Name` on it by reflection, and the single-entity
  `Update(item)` persists the entity — including every other property as it was read

#### Scenario: An empty PropertyUpdate still rewrites every matching row

- **Given** an `AbstractBulkStore<T>` and a `PropertyUpdate<T>` with zero assignments
- **When** `Update(filter, updates)` is called
- **Then** every matching entity is still read and passed to the single-entity `Update`, because the
  delegation happens before any emptiness check

#### Scenario: Async PropertyUpdate fallback delegates without awaiting itself

- **Given** an `AbstractAsyncBulkStore<T>`
- **When** `UpdateAsync(filter, updates, ct)` is called
- **Then** it returns the task from `UpdateAsync(filter, entity => updates.ApplyTo(entity), ct)`
  directly, without an intermediate `await`

### Requirement: PropertyUpdate assignment collection and reflection application

The system SHALL let callers accumulate property assignments fluently via
`PropertyUpdate<T>.Set(property, value)`, SHALL preserve insertion order in the internal
`Assignments` list, and SHALL apply them in that order in `ApplyTo` by unwrapping a `UnaryExpression`
body to its operand, casting to `MemberExpression`, and calling `PropertyInfo.SetValue`.

#### Scenario: Set returns the same instance for chaining

- **Given** a fresh `PropertyUpdate<T>`
- **When** `.Set(x => x.Name, "a").Set(x => x.Count, 5)` is chained
- **Then** both calls return the same instance and `Assignments` holds two entries in that order

#### Scenario: A boxed/converted property selector is unwrapped

- **Given** an assignment whose lambda body is a `UnaryExpression` (e.g. a `Convert` inserted for a
  value-type-to-object selector)
- **When** `ApplyTo(entity)` runs
- **Then** the operand is cast to `MemberExpression` and the underlying property is set

#### Scenario: A non-property member is silently skipped

- **Given** an assignment whose resolved member is not a `PropertyInfo` (for example a public field)
- **When** `ApplyTo(entity)` runs
- **Then** `memberExpr.Member as PropertyInfo` is null and the null-conditional `prop?.SetValue(...)`
  performs no assignment — no exception is raised and the caller receives no signal

#### Scenario: A non-member selector throws InvalidCastException

- **Given** an assignment whose lambda body is neither a `MemberExpression` nor a `UnaryExpression`
  over one (for example `x => x.Compute()`)
- **When** `ApplyTo(entity)` runs
- **Then** the unconditional `(MemberExpression)property.Body` cast throws `InvalidCastException`

#### Scenario: Duplicate assignments to one property are both applied in order

- **Given** `new PropertyUpdate<T>().Set(x => x.Name, "a").Set(x => x.Name, "b")`
- **When** `ApplyTo(entity)` runs
- **Then** both `SetValue` calls execute in order and the entity ends up with `"b"` (last wins)

### Requirement: SQL PropertyUpdate translates to a single UPDATE … SET … WHERE

The system SHALL override `Update(filter, PropertyUpdate<T>)` in `DataBaseBulkStore<DB,T>` and
`AsyncDataBaseBulkStore<DB,T>` to resolve each assignment's column through
`DataBase.GetFieldFromLambda`, translate the filter through `DataBase.ParseConditionExpression`, and
issue one connector `Update(tableName, fields, values, conditions)` — reading no entities and
invoking no per-entity update path.

#### Scenario: Two assignments become one statement with two SET columns

- **Given** a `DataBaseBulkStore<DB,T>` with a non-null `Connector` and
  `new PropertyUpdate<T>().Set(x => x.Name, "n").Set(x => x.Count, 5)`
- **When** `Update(filter, updates)` is called
- **Then** `EnsureInitialized()` runs, the table is loaded via `DataBase.LoadTable(typeof(T))`,
  `fields` is populated as index → column name for both assignments, `values` as column name → value,
  the filter is parsed into conditions, and `Connector.Update(table.Name, fields, values, conditions)`
  is invoked exactly once

#### Scenario: A null assignment value becomes DBNull

- **Given** an assignment `Set(x => x.Name, null)`
- **When** the SQL override builds its value map
- **Then** it stores `DBNull.Value` for that column (`value ?? DBNull.Value`), so the statement sets
  the column to SQL NULL

#### Scenario: An empty PropertyUpdate short-circuits before touching the database

- **Given** a `DataBaseBulkStore<DB,T>` and a `PropertyUpdate<T>` with `Assignments.Count == 0`
- **When** `Update(filter, updates)` is called
- **Then** the method returns immediately after `EnsureInitialized()` — no table load, no filter
  parse, no connector call, and no rows are written

#### Scenario: An unresolvable property selector throws ArgumentException

- **Given** an assignment whose body does not resolve to a `PropertyInfo`
- **When** the SQL override calls `DataBase.GetFieldFromLambda(property)`
- **Then** `GetFieldFromLambda` throws
  `ArgumentException("Unable to resolve property from expression: …", "expr")` and no statement is issued

#### Scenario: Two assignments to the same property throw on the duplicate key

- **Given** `new PropertyUpdate<T>().Set(x => x.Name, "a").Set(x => x.Name, "b")` on a SQL bulk store
- **When** `Update(filter, updates)` is called
- **Then** the second `values.Add(field.Name, value)` throws `ArgumentException` for the duplicate
  dictionary key and no statement is issued

#### Scenario: The async override prefers the async connector

- **Given** an `AsyncDataBaseBulkStore<DB,T>` whose `AsyncConnector` is non-null
- **When** `UpdateAsync(filter, updates, ct)` is called
- **Then** it awaits `AsyncConnector.UpdateAsync(table.Name, fields, values, conditions, false, ct)`
  with `isExpressionValues` explicitly false

#### Scenario: The async override falls back to the sync connector on a thread-pool thread

- **Given** an `AsyncDataBaseBulkStore<DB,T>` with a non-null `Connector` but a null `AsyncConnector`
- **When** `UpdateAsync(filter, updates, ct)` is called
- **Then** it awaits `Task.Run(() => Connector!.Update(table.Name, fields, values, conditions), ct)`

#### Scenario: No connector means the update is silently dropped

- **Given** a SQL bulk store whose `Connector` is null
- **When** `Update(filter, updates)` / `UpdateAsync(filter, updates, ct)` is called
- **Then** the method returns without error, without translating the filter and without writing anything

### Requirement: Filter-based delete — portable read-then-delete versus native DELETE

The system SHALL implement `Delete(filter)` differently per store family: `AbstractBulkStore<T>` /
`AbstractAsyncBulkStore<T>` SHALL read the matching entities, materialise them and pass the collection
to the bulk `Delete(IEnumerable<T>)` (hence `DeleteCore`), while the SQL bulk stores SHALL issue one
connector delete against the translated filter without reading anything.

#### Scenario: Portable delete routes through DeleteCore with the matched collection

- **Given** an `AbstractBulkStore<T>` whose filter matches two entities
- **When** `Delete(filter)` is called
- **Then** the two entities are read via `Read(filter, null, null, null)`, materialised with `ToList()`
  and passed to `Delete(items)`, which runs `EnsureInitialized()` and `DeleteCore(items)`

#### Scenario: Portable delete with no matches still calls DeleteCore

- **Given** an `AbstractBulkStore<T>` whose filter matches nothing
- **When** `Delete(filter)` is called
- **Then** `Delete(items)` is still invoked with an empty list, so `DeleteCore` receives an empty
  sequence rather than being skipped

#### Scenario: SQL delete never reads the entities

- **Given** a `DataBaseBulkStore<DB,T>` with a non-null `Connector`
- **When** `Delete(filter)` is called
- **Then** after `EnsureInitialized()` it calls `Connector.Delete(typeof(T), filter as LambdaExpression)`
  once; no select is issued, `DeleteCore` is not invoked, and the per-entity `Delete(item)` path — with
  any behaviour attached to it — does not run

#### Scenario: Async SQL delete prefers the async connector, else Task.Run

- **Given** an `AsyncDataBaseBulkStore<DB,T>`
- **When** `DeleteAsync(filter, ct)` is called
- **Then** it awaits `AsyncConnector.DeleteAsync(typeof(T), filter as LambdaExpression, ct)` when
  `AsyncConnector` is non-null, otherwise
  `Task.Run(() => Connector!.Delete(typeof(T), filter as LambdaExpression), ct)`

#### Scenario: No connector means the delete is silently dropped

- **Given** a SQL bulk store whose `Connector` is null
- **When** `Delete(filter)` / `DeleteAsync(filter, ct)` is called
- **Then** the method returns without error and no rows are deleted

### Requirement: A filter that translates to no conditions produces an unfiltered statement

The system SHALL, on the SQL native filter paths, emit no WHERE clause when
`DataBase.ParseConditionExpression` yields an empty condition set — so such an update or delete SHALL
affect every row of the table. Three distinct inputs yield that empty set: the constant-true predicate
(an explicit early return), a null filter (the whole method body is skipped), and any predicate shape
the parser has no branch for (the method's final statement is an unconditional
`return Array.Empty<Condition>()`). The three are therefore indistinguishable at this boundary.

#### Scenario: A constant-true predicate deletes the whole table

- **Given** a `DataBaseBulkStore<DB,T>` and the predicate `x => true`
- **When** `Delete(x => true)` is called
- **Then** `ParseConditionExpression` returns `Array.Empty<Condition>()`, `AddWhere` appends nothing,
  and the emitted statement is `DELETE FROM <table>` with no WHERE — every row is removed

#### Scenario: A constant-false predicate becomes an impossible condition

- **Given** a `DataBaseBulkStore<DB,T>` and the predicate `x => false`
- **When** `Delete(x => false)` is called
- **Then** `ParseConditionExpression` returns a single condition rendering `1 = 0`, so the statement
  matches no rows

#### Scenario: A predicate the parser cannot translate also affects every row

- **Given** a `DataBaseBulkStore<DB,T>` and a predicate whose body is a shape `ParseConditionExpression`
  has no branch for — for example the `InvocationExpression` of `x => pred(x)` over a captured
  `Func<T,bool>`, which `ExpressionNormalizer` cannot funcletize because it references the parameter
- **When** `Delete(filter)` or `Update(filter, PropertyUpdate<T>)` is called
- **Then** the parse falls through to the final `return Array.Empty<Condition>()`, `AddWhere` appends
  nothing, and the statement is issued as `DELETE FROM <table>` / `UPDATE <table> SET …` with no WHERE —
  every row is deleted or updated, with no exception and no signal that the predicate was dropped

#### Scenario: A null filter reaches the same fall-through

- **Given** a SQL bulk store called as `Delete(null!)` / `Update(null!, updates)` — the `filter`
  parameter is declared non-nullable and is not checked
- **When** the filter is translated
- **Then** `filter as LambdaExpression` is null, `ParseConditionExpression` skips its entire body and
  returns `Array.Empty<Condition>()`, and the emitted statement carries no WHERE — every row is affected

### Requirement: Bulk collection operations initialise lazily and default to per-item loops

The system SHALL run `EnsureInitialized()` / `EnsureInitializedAsync(ct)` at the entry of every public
bulk collection operation before delegating to its `*Core` method, and the SQL bulk stores' default
`*Core` implementations SHALL loop over the collection invoking the corresponding single-entity
operation.

#### Scenario: Bulk create initialises then delegates

- **Given** an uninitialised bulk store
- **When** `Create(data, storeDelegate)` is called
- **Then** `EnsureInitialized()` runs (double-checked, once) and then `CreateCore(data, storeDelegate)`

#### Scenario: SQL bulk create defaults to per-item creates

- **Given** a `DataBaseBulkStore<DB,T>` that does not override `CreateCore`
- **When** `Create(threeItems, storeDelegate)` is called
- **Then** `CreateCore` calls the single-entity `Create(item, storeDelegate)` once per item

#### Scenario: SQL async bulk update defaults to per-item updates with the same delegate and token

- **Given** an `AsyncDataBaseBulkStore<DB,T>` that does not override `UpdateCoreAsync`
- **When** `UpdateAsync(items, storeDelegate, ct)` is called
- **Then** `EnsureInitializedAsync(ct)` is awaited and then `UpdateAsync(item, storeDelegate, ct)` is
  awaited once per item, in order

#### Scenario: The action-based filter update initialises indirectly

- **Given** an uninitialised bulk store
- **When** `Update(filter, Action<T>)` is called
- **Then** initialisation happens inside the nested `Read(filter, …)` call — the method itself contains
  no `EnsureInitialized()`

### Requirement: Async bulk reads select their execution path from the available connector

The system SHALL, in `AsyncDataBaseBulkStore<DB,T>.ReadCoreAsync`, return an empty sequence when
`Connector` is null, stream through `AsyncConnector.SelectAsync` when an async connector is present,
and otherwise wrap the synchronous `Connector.Select` in `Task.Run` with the cancellation token — which,
because `Select` is an iterator method, offloads only the construction of the enumerator: the query
itself still runs on whichever thread enumerates the returned sequence.

#### Scenario: Async connector results are streamed and type-filtered

- **Given** an `AsyncDataBaseBulkStore<DB,T>` with a non-null `AsyncConnector`
- **When** `ReadAsync(filter, orderBy, limit, offset, ct)` is called
- **Then** `AsyncConnector.SelectAsync(typeof(T), filter as LambdaExpression, orderBy?.ToDictionary(),
  limit, offset, ct)` is enumerated with `await foreach` and only items that are `T` are collected

#### Scenario: Sync-only connector is wrapped in Task.Run

- **Given** an `AsyncDataBaseBulkStore<DB,T>` with a non-null `Connector` and a null `AsyncConnector`
- **When** `ReadAsync(filter, …, ct)` is called
- **Then** `Task.Run(() => Connector!.Select(…), ct)` is awaited, a null result maps to
  `Enumerable.Empty<T>()`, and a non-null result is returned as `results.OfType<T>()` — still
  unenumerated, so unlike the `AsyncConnector` branch (which materialises a `List<T>` inside the
  method) this path returns before any row is read, and the connection is opened, `ct` unobserved, on
  the caller's thread when the awaited result is enumerated

#### Scenario: No connector at all yields an empty result

- **Given** an `AsyncDataBaseBulkStore<DB,T>` whose `Connector` is null
- **When** `ReadAsync(filter, …, ct)` is called
- **Then** `ReadCoreAsync` returns `Enumerable.Empty<T>()` before consulting `AsyncConnector`

#### Scenario: The parameterless async read reads everything

- **Given** an `AsyncDataBaseBulkStore<DB,T>` or `AbstractAsyncBulkStore<T>`
- **When** `ReadAsync(ct)` is called
- **Then** it delegates to `ReadAsync(null, null, null, null, ct)`

### Requirement: Aggregation on SQL bulk stores

The system SHALL expose `Aggregate(AggregateQuery<T>)` on `DataBaseBulkStore<DB,T>` and
`AggregateAsync(AggregateQuery<T>, ct)` on `AsyncDataBaseBulkStore<DB,T>`, each initialising first,
returning an empty read-only result when the required connector is absent, and otherwise materialising
every row produced by the connector's aggregate select.

#### Scenario: Sync aggregation needs only the synchronous connector

- **Given** a `DataBaseBulkStore<DB,T>` with a non-null `Connector`
- **When** `Aggregate(query)` is called
- **Then** `EnsureInitialized()` runs and every row of `Connector.SelectAggregate(typeof(T), query)` is
  collected into a read-only `AggregateResult` list

#### Scenario: Async aggregation requires both connectors and otherwise returns empty

- **Given** an `AsyncDataBaseBulkStore<DB,T>` whose `AsyncConnector` is null (regardless of `Connector`)
- **When** `AggregateAsync(query, ct)` is called
- **Then** it returns `Array.Empty<AggregateResult>()` — there is no `Task.Run` fallback to the
  synchronous `SelectAggregate`, unlike the read/update/delete paths

#### Scenario: Async aggregation streams rows when both connectors exist

- **Given** an `AsyncDataBaseBulkStore<DB,T>` with both `Connector` and `AsyncConnector` non-null
- **When** `AggregateAsync(query, ct)` is called
- **Then** `AsyncConnector.SelectAggregateAsync(typeof(T), query, ct)` is enumerated with
  `await foreach` and the rows are returned as a read-only list

### Requirement: Overridability differs between the abstract bases and the SQL bulk stores

The system SHALL declare the bulk public methods `virtual` and the `*Core` methods `abstract` on
`AbstractBulkStore<T>` / `AbstractAsyncBulkStore<T>`, whereas on `DataBaseBulkStore<DB,T>` /
`AsyncDataBaseBulkStore<DB,T>` the collection-facing public methods `Read(filter,…)`,
`Create(IEnumerable…)`, `Update(IEnumerable…)`, `Delete(IEnumerable…)` (and their async twins) SHALL
be non-virtual while their `*Core` counterparts are `virtual` with a working default.

#### Scenario: A concrete abstract-base store must supply every core

- **Given** a new store deriving from `AbstractBulkStore<T>`
- **When** it is compiled
- **Then** it must implement `CreateCore`, `ReadCore`, `UpdateCore` and `DeleteCore`, because all four
  are abstract

#### Scenario: A SQL-derived store customises behaviour only through Core

- **Given** a store deriving from `DataBaseBulkStore<DB,T>`
- **When** it needs different bulk-read behaviour
- **Then** it must override `ReadCore(filter, orderBy, limit, offset)` — the public
  `Read(filter, orderBy, limit, offset)` is not declared virtual and cannot be overridden

#### Scenario: The filter-based write overloads remain overridable on SQL stores

- **Given** a store deriving from `DataBaseBulkStore<DB,T>`
- **When** it needs provider-specific filter-based writes
- **Then** `Update(filter, Action<T>)`, `Update(filter, PropertyUpdate<T>)` and `Delete(filter)` are
  declared `virtual` and can be overridden directly

### Requirement: Cancellation tokens flow through every async bulk path

The system SHALL accept a `CancellationToken` on every async bulk operation, default it to `default`,
and forward the caller's token to initialisation, nested reads, per-item operations and connector calls.

#### Scenario: The token reaches initialisation and the nested read

- **Given** an `AbstractAsyncBulkStore<T>` and a token `ct`
- **When** `DeleteAsync(filter, ct)` is called
- **Then** `ReadAsync(filter, null, null, null, ct)` and then `DeleteAsync(items, ct)` — which awaits
  `EnsureInitializedAsync(ct)` — both receive `ct`

#### Scenario: The token reaches the offloaded synchronous connector call

- **Given** an `AsyncDataBaseBulkStore<DB,T>` with no async connector
- **When** `DeleteAsync(filter, ct)` is called
- **Then** the fallback is `Task.Run(() => Connector!.Delete(…), ct)`, so `ct` governs the scheduling of
  the offloaded work
