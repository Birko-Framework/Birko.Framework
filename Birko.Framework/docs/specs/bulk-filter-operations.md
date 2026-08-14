---
area: bulk-filter-operations
generated-at: 34fa9b2797a9cfa273ccb2c4fcb9189a9648f55e
generated-on: 2026-08-14
sources:
  - ../Birko.Data.MongoDB/Stores/AsyncMongoDBStore.cs
  - ../Birko.Data.MongoDB/Stores/MongoDBStore.cs
  - ../Birko.Data.SQL/Exceptions/WholeTableWriteException.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractAsyncConnector_Delete.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractAsyncConnector_Select.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractAsyncConnector_Update.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractConnector_Delete.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractConnector_Select.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractConnector_Update.cs
  - ../Birko.Data.SQL/SQL/DataBase_OrderBy.cs
  - ../Birko.Data.SQL/Stores/AsyncDataBaseBulkStore.cs
  - ../Birko.Data.SQL/Stores/DataBaseBulkStore.cs
  - ../Birko.Data.Stores/AbstractAsyncBulkStore.cs
  - ../Birko.Data.Stores/AbstractBulkStore.cs
  - ../Birko.Data.Stores/IAsyncBulkStore.cs
  - ../Birko.Data.Stores/IBulkStore.cs
  - ../Birko.Data.Stores/OrderBy.cs
  - ../Birko.Data.Stores/PropertyUpdate.cs
shaped-by: [FEATURE-014]
# false, and NOT because nobody tried: the evidence pass cannot run from this aggregator at all.
# Every source glob above points into a sibling repo, so no task's `pr:` sha resolves under `git show`
# here (verified: TASK-109's d8c2f40 is "unknown revision" in this checkout). FEATURE-014 above comes
# from the regenerating task's own `feature:` field — the --story/--feature input to the union — not
# from evidence. True of every area in this repo's spec tree, not just this one.
shaped-by-derived: false
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

## Regen provenance

Full regen at `a62ec94`. Supersedes two partial regens that carried their scope in the `generated-at`
stamp itself (making it unparseable as a sha, so the staleness check reported the area stale on format
alone): SH-H003/SH-M022 → `Birko.Data.SQL@2a87f84`, TASK-128 → `Birko.Data.SQL.View@576707c`. Sibling-repo
shas are recorded here rather than in the stamp because this repo cannot resolve them — the area's sources
all live in sibling repos, so `generated-at` can only ever name this aggregator's HEAD.

Scoped regen at `5b4c2b4` for **TASK-109 / SH-H002 + SH-M023** — the no-WHERE destructive-write guard.
Sibling-repo shas: `Birko.Data.SQL@d8c2f40`, `Birko.Data.Stores@3cd8b2a`, `Birko.Data.InMemory@4f680b7`,
`Birko.Data.MongoDB@88f96ee`. This regen **rewrote a requirement whose own title asserted the defect**
(*"A filter that translates to no conditions produces an unfiltered statement"*) — the shipped behaviour
the previous harvest correctly recorded, and the thing the fix inverts. It also added the four
destructive funnels, `WholeTableWriteException` and the two MongoDB stores to this area's globs in
`.map.yml`: the guard's primary site was reachable by **no** glob in any area, so a regen could not have
seen the change at all. Same silent under-coverage as the TASK-110 note in the map, found the same way.

Scoped regen at `34fa9b2` for **TASK-137** — an empty `NOT IN` rendered `1 = 1`, which is a non-empty
`WHERE` and therefore **satisfied the guard TASK-109 had just installed**. Measured against SQLite:
`Delete(x => !empty.Contains(x.Col))` left 0 of 3 rows and threw nothing; the `Update` twin rewrote 3 of 3.
`WouldTargetEveryRow` now shares the renderer's reduction, so a non-empty condition collection whose terms
all reduce away is refused like the empty one.

**Coverage gap, reported rather than silently patched:** this area's requirement text describes
`WouldTargetEveryRow` and `AddRequiredWhere` in detail, and both live in
`../Birko.Data.SQL/SQL/Connectors/AbstractConnectorBase.cs`, which **no glob in this area reaches** — the
guard's own implementation file. TASK-137's change to it is visible here only through the funnels and the
store overloads that call into it, so a future fix confined to that file would produce a clean diff for this
area. The third instance of exactly this shape (after the TASK-110 and TASK-109 notes above), which is the
argument for deciding TASK-208 rather than routing around it again. `.map.yml` is human-owned and was not
edited.

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

#### Scenario: Repeating one property in the sort loses the earlier direction

- **Given** `OrderBy<T>.By(x => x.Rank).ThenByDescending(x => x.Rank)` — two `OrderByField` entries on the
  same property, which `Fields` reports faithfully
- **When** the store converts it for the connector with `orderBy?.ToDictionary()`
- **Then** `ToDictionary` assigns by key (`dict[field.PropertyName] = field.Descending`), so the two entries
  collapse to one `Rank → descending` before any backend sees them — a multi-key sort that names one
  property twice cannot be expressed through the connector boundary, and no error reports the loss

### Requirement: SQL sort keys are resolved against table metadata before reaching the statement

`OrderBy<T>` carries **CLR property names** — `By`/`ThenBy` read `member.Member.Name`, and `ByName`
accepts an arbitrary caller string — and the SQL `ORDER BY` clause is built by interpolating those keys
into `CommandText` (`AbstractConnectorBase.CreateSelectCommand`), unparameterised.

The system SHALL therefore resolve every sort key through the selected tables' field metadata before it
reaches the clause (`DataBase.ResolveOrderFields`, invoked from the `Select(IEnumerable<Tables.Table>, …)`
and `SelectAsync(IEnumerable<Tables.Table>, …)` funnels that every SQL entity read passes through),
trying three lookups in order — property name (`Table.GetFieldByPropertyName`), then mapped column name
compared `OrdinalIgnoreCase`, then the optional `DataBase.ResolveFieldSelectName` hook that the view layer
registers — emitting the field's `GetSelectName(withTableName)`, qualifying with the table name when more
than one table is selected, preserving key order and direction, and SHALL throw `ArgumentException` naming
both the key and the entity type when a key matches none of the three. Because the emitted identifier is
always a name read out of metadata, caller-supplied text cannot reach the statement — **the resolution is
the whitelist**.

The system SHALL additionally throw `ArgumentException` naming every unresolved key when sort keys are
supplied but no non-null table metadata is, since there is then nothing to resolve against and emitting
the keys unresolved would reopen the injection. A null or whitespace-only key SHALL resolve to nothing and
therefore raise the same unresolved-key error.

Where two keys resolve to the same identifier — a property name and that property's own column name are
both accepted, so this is reachable without caller error — the system SHALL collapse them into one clause
entry (last direction wins) rather than throwing or emitting the column twice.

The resolved identifier SHALL NOT be quoted, matching every other column identifier the SQL layer emits
(the DDL's column list, the WHERE clause's `condition.Name`, the SELECT list); only table names are
quoted.

#### Scenario: A remapped property is emitted under its column name

- **Given** an entity with `[NamedField("label_col")] public string? Label` and
  `Read(null, OrderBy<T>.By(x => x.Label), null, null)`
- **When** the select command is built
- **Then** the clause is `ORDER BY label_col ASC` and the rows come back sorted — before resolution the
  CLR name was emitted and the database rejected the statement with *no such column: Label*, so a
  remapped or `ModelMap`-mapped column could not be sorted at all

#### Scenario: An unremapped property is emitted unchanged

- **Given** an entity with a plain `public int Rank` and `OrderBy<T>.By(x => x.Rank)`
- **When** the select command is built
- **Then** the clause is `ORDER BY Rank ASC` — bare and unquoted, identical to what was emitted before
  resolution existed, so no working sort changes meaning

#### Scenario: A sort key that is already the mapped column name resolves to itself

- **Given** `OrderBy<T>.ByName("label_col")` for the property mapped to that column
- **When** the key is resolved
- **Then** it resolves via the column-name match and the clause is `ORDER BY label_col ASC`

#### Scenario: The column-name match ignores case

- **Given** `OrderBy<T>.ByName("LABEL_COL")` for a property mapped to `label_col`
- **When** the key is resolved
- **Then** the column-name lookup compares `OrdinalIgnoreCase`, so it matches and the emitted identifier is
  the metadata's own spelling `label_col` — not the caller's. The property-name lookup runs first and is
  whatever `Table.GetFieldByPropertyName` implements, so the two steps need not agree on case sensitivity

#### Scenario: A view column resolves inside the entity funnel via the registered hook

- **Given** a sort key naming a column that no `Tables.Table` field knows, and the view layer has assigned
  `DataBase.ResolveFieldSelectName`
- **When** the key is resolved
- **Then** after the property-name and column-name lookups both miss, the hook is invoked as
  `ResolveFieldSelectName(table.Type, key, withTableName)` for each selected table with a non-null `Type`,
  and a non-empty answer is emitted as the identifier — so the entity funnel is not exclusively an
  entity-column whitelist, and a view column can be sorted through it

#### Scenario: Sort keys with no table metadata are rejected, not passed through

- **Given** a non-empty sort dictionary and a `tables` argument that is null or contains only nulls
- **When** `ResolveOrderFields` runs
- **Then** `ArgumentException` is thrown naming every supplied key and reporting that no table metadata was
  available — the keys are never emitted unresolved, which is what stops this path becoming a hole around
  the whitelist

#### Scenario: A blank sort key is an unresolved key

- **Given** `OrderBy<T>.ByName("")` or `ByName("   ")`
- **When** the key is resolved
- **Then** the per-key resolver returns null on the `IsNullOrWhiteSpace` check before consulting any
  metadata, so the caller raises the same unresolved-key `ArgumentException`

#### Scenario: Two keys resolving to one column collapse instead of throwing

- **Given** `OrderBy<T>.ByName("Label").ThenByDescending(x => x.Label)` — the CLR name and the property both
  resolving to `label_col`
- **When** the keys are resolved
- **Then** the resolved dictionary is written with the indexer rather than `Add`, so one entry survives
  carrying the later direction; no `ArgumentException` for a duplicate key is raised and the clause names
  the column once

#### Scenario: A joined select qualifies the column with its table

- **Given** a select over two tables and a sort key resolving to a field of the second
- **When** the keys are resolved
- **Then** the emitted identifier is `Table.Column`; with a single table it is the bare column

#### Scenario: An injection payload is rejected before the statement is built

- **Given** `OrderBy<T>.ByName("Rank; CREATE TABLE Pwned (x INTEGER); --")` — the shape a consumer
  reaches by passing a request's sort field straight through
- **When** the read is enumerated
- **Then** `ArgumentException` is thrown, no command is executed and no such table is created. Before
  resolution this text reached `CommandText` verbatim and **the table was created**; a payload of
  `"Rank LIMIT 1 --"` likewise commented out the framework's own `LIMIT` and returned one row where the
  caller had asked for a hundred, and `"(SELECT count(*) FROM sqlite_master)"` was evaluated as the sort
  key. The trailing ` ASC`/` DESC` the builder appends is not a mitigation — a comment removes it

#### Scenario: An unknown sort key names itself rather than surfacing a provider error

- **Given** `OrderBy<T>.ByName("NoSuchThing")`
- **When** the read is enumerated
- **Then** `ArgumentException` names both `NoSuchThing` and the entity type, instead of the provider
  answering with a column name the developer never wrote

#### Scenario: No sort keys emit no clause

- **Given** `Read(filter, null, null, null)`
- **When** the select command is built
- **Then** no `ORDER BY` is appended and resolution is a pass-through — a null or empty sort dictionary
  is returned unchanged

#### Scenario: The view path resolves through its own funnel, not this one

- **Given** a `SqlViewStore<TView>` query carrying an `OrderBy<TView>`
- **When** the view select command is built
- **Then** the keys do **not** pass through this `Tables.Table` funnel — the view clause is emitted from its
  own sites (`AbstractConnectorBase_View.CreatePersistentViewSelectCommand` and the on-the-fly builder) — and
  are instead resolved by `DataBase.ResolveViewOrderFields` in `Select(Tables.View, …)` / `SelectAsync(…)`,
  which additionally picks the resolved form per path because a persistent view exposes different column
  names than the on-the-fly join select. Specified under `views-and-aggregation`; closed by TASK-128.
  The separation is one-directional, not total: the entity funnel's own third lookup calls the
  `ResolveFieldSelectName` hook the view layer registers, so a view column reaching *this* funnel still
  resolves (see the hook scenario above)

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
- **When** `Update(filter, updates)` / `UpdateAsync(filter, updates, ct)` is called with a **non-null**
  filter
- **Then** the method returns without error, without translating the filter and without writing anything
- **And** with a null filter it instead throws, because `RequireFilter` precedes the `Connector == null`
  early return — a missing store does not excuse a missing filter

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
- **When** `Delete(filter)` / `DeleteAsync(filter, ct)` is called with a **non-null** filter
- **Then** the method returns without error and no rows are deleted
- **And** with a null filter it instead throws, because `RequireFilter` precedes the `Connector == null`
  early return

### Requirement: A destructive statement that would carry no WHERE is refused, not issued

The system SHALL refuse to issue a destructive SQL statement (`DELETE` / `UPDATE`) that would carry no
`WHERE` clause, unless every-row was requested **explicitly**. The refusal SHALL be
`WholeTableWriteException` (a `System.InvalidOperationException`) naming the operation, the table and the
explicit door to use. All the inputs that previously collapsed into a conditionless statement — a null
filter, a predicate shape the parser has no branch for, and a predicate that reduces to "everything"
without being an explicit constant — SHALL therefore reach the same refusal rather than the same
whole-table write.

The guard SHALL sit at the **four destructive funnels** (`AbstractConnector_Delete`,
`AbstractConnector_Update` and their async twins), each of which is the single method every public
overload of its verb feeds; no provider overrides them, so one implementation covers all four SQL
providers. It SHALL be enforced twice, at two different points and on two different things:

- `WouldTargetEveryRow(conditions)` **before** entering `DoCommandWithTransaction`. That wrapper funnels
  every exception from its command-building callback through `InitException`, which re-wraps it in a bare
  `Exception` — so a refusal thrown from inside would reach the caller as a type no
  `catch (WholeTableWriteException)` could select. Refusing first also avoids opening a connection and
  beginning a transaction for a statement that will never run.
- `AddRequiredWhere(...)` on the **rendered** clause, as the backstop. `ConditionDefinition` returns
  `string.Empty` for a null *or* empty enumerable and builds each term through `BuildSingleCondition`,
  which can yield an empty string for a malformed condition — so a non-empty collection can still render
  no `WHERE`. Guarding the rendered text is what closes that case; guarding the collection alone would
  not.

An **empty** condition collection is not the only way to mean everything: a NON-EMPTY collection whose
terms all reduce away means it too. `WouldTargetEveryRow` SHALL therefore ask
`AbstractConnectorBase.IsAlwaysTrueCondition` / `IsAlwaysTrueChain` — the **same** reduction the renderer
uses, so the guard and the emitted SQL cannot disagree about what "everything" covers. A term that
constrains nothing SHALL NOT be rendered as a constant: a tautology such as `1 = 1` is a non-empty
`WHERE` and would satisfy the rendered-clause test above, which is precisely how an ordinary filter
(`Delete(x => !empty.Contains(x.Col))`) reached a whole-table `DELETE` with the guard's blessing.

A tree that reduces to always-**false** SHALL NOT be refused: it targets no rows, so it renders its
`1 = 0` and the statement is issued. The refusal fires on "everything", never on "nothing".

`AddRequiredWhere` SHALL be separate from `AddWhere` rather than a flag on it, because reads share
`AddWhere` and a null filter on a read legitimately means read-everything.

#### Scenario: A predicate the parser cannot translate is refused instead of affecting every row

- **Given** a `DataBaseBulkStore<DB,T>` and a predicate whose body is a shape `ParseConditionExpression`
  has no branch for — for example the `InvocationExpression` of `x => pred(x)` over a captured
  `Func<T,bool>`, which `ExpressionNormalizer` cannot funcletize because it references the parameter
- **When** `Delete(filter)` or `Update(filter, PropertyUpdate<T>)` is called
- **Then** the parse falls through to `Array.Empty<Condition>()`, `WouldTargetEveryRow` returns true, and
  `WholeTableWriteException` is thrown before any connection is opened — no statement is issued and no
  row is touched

#### Scenario: A null filter is refused at the store boundary, before the connector

- **Given** a SQL bulk store called as `Delete(null!)` / `Update(null!, updates)`
- **When** the call is made
- **Then** `RequireFilter` throws `ArgumentNullException` naming the `filter` parameter, with a message
  directing the caller to `DeleteAll()` / `UpdateAll(updates)` or an explicit `x => true`
- **And** the throw precedes the `Connector == null` early return, so a store with no connector still
  refuses rather than silently doing nothing

#### Scenario: The refusal names a type a caller can catch selectively

- **Given** any of the refused shapes above
- **When** the exception propagates
- **Then** it is `WholeTableWriteException : InvalidOperationException`, not a bare `Exception`, so
  `catch (WholeTableWriteException)` and `catch (InvalidOperationException)` both select it — a
  request-shaped problem does not surface as an unhandled fault

#### Scenario: A constant-false predicate still becomes an impossible condition

- **Given** a `DataBaseBulkStore<DB,T>` and the predicate `x => false`
- **When** `Delete(x => false)` is called
- **Then** `ParseConditionExpression` returns a single condition rendering `1 = 0`, so the statement is
  issued, carries a WHERE, and matches no rows — a predicate that legitimately matches nothing is not a
  whole-table write and is not refused

#### Scenario: A non-constant predicate that reduces to everything is refused, not allowed

- **Given** the predicate `x => true || x.A == 1`, which the parser reduces to an empty condition set but
  which is not a single constant node after normalization
- **When** `Delete(filter)` is called
- **Then** `IsExplicitAllRows` returns false, the funnel guard fires, and `WholeTableWriteException` is
  thrown — the explicit-opt-in test is deliberately narrower than the set of shapes that mean
  "everything", so the boundary errs toward refusing rather than toward writing

#### Scenario: An empty negated `Contains` is refused, not treated as a deliberate all-rows write

- **Given** a `DataBaseBulkStore<DB,T>` and the predicate `x => !ids.Contains(x.Amount)` where `ids` is an
  empty collection — a filter that constrains nothing, though it is not an explicit constant
- **When** `Delete(filter)` / `DeleteAsync(filter, ct)` or `Update(filter, updates)` is called
- **Then** `WouldTargetEveryRow` finds the single `In` term reduces to always-true, and
  `WholeTableWriteException` is thrown before a transaction opens — the same answer
  `x => true || x.A == 1` gets, because both merely *reduce* to everything rather than saying so
- **And** no row is deleted or rewritten: measured end-to-end against SQLite on a 3-row table, all 3 rows
  survive the delete and none is rewritten by the update

#### Scenario: An OR chain that collapses to everything is refused for the same reason

- **Given** the predicate `x => x.Amount > 20 || !ids.Contains(x.Amount)` with `ids` empty
- **When** `Delete(filter)` is called
- **Then** the OR group reduces to always-true (`A OR TRUE` is `TRUE`) and the call is refused — a
  collapsed chain targets every row just as surely as a sole always-true term does

#### Scenario: A bounded filter beside an always-true term still deletes its own rows

- **Given** the predicate `x => x.Amount > 20 && !ids.Contains(x.Amount)` with `ids` empty
- **When** `Delete(filter)` is called
- **Then** the always-true term is dropped, the statement carries `WHERE` on the real term, and only its
  rows are deleted — the reduction must not make a bounded delete look unbounded

#### Scenario: A filter that reduces to always-false deletes nothing rather than being refused

- **Given** the predicate `x => !(x.Amount > 20 || !ids.Contains(x.Amount))` with `ids` empty, which is
  always false
- **When** `Delete(filter)` is called
- **Then** the statement is issued carrying `WHERE 1 = 0` and no row is deleted — it is neither refused
  nor widened into a whole-table write

### Requirement: Every-row destructive writes are reachable only through an explicit door

The system SHALL keep a deliberate whole-table delete/update possible, through exactly two spellings:
the named `DeleteAll()` / `UpdateAll(updates)` methods (plus `DeleteAllAsync(ct)` /
`UpdateAllAsync(updates, ct)`), and a caller-supplied `x => true` predicate retained as their synonym.
Both SHALL exist on the SQL bulk stores and on the portable bases `AbstractBulkStore<T>` /
`AbstractAsyncBulkStore<T>`.

The emitted SQL SHALL be the clean conditionless statement (`DELETE FROM "T"`) and SHALL NOT carry a
`1 = 1` marker: that pattern is indistinguishable from `' OR 1=1--` in a query log and would train
operators to ignore a real attack signature. A bare `DELETE` in a log therefore means somebody asked for
it explicitly.

The synonym test SHALL be a **one-node** check (`IsExplicitAllRows`) applied after
`ExpressionNormalizer` funcletization, not a catalogue of always-true shapes — `x => true`,
`x => 1 == 1` and `x => capturedFlag` all normalize to the same `ConstantExpression`. Enumerating the
parser's reduce-to-everything sites SHALL NOT be used, because such a whitelist rots when a site is
added and its failure mode is a refused destructive operation on working code.

#### Scenario: The named door emits the conditionless statement

- **Given** a `DataBaseBulkStore<DB,T>`
- **When** `DeleteAll()` is called
- **Then** the connector's `DeleteAll(type)` runs `Delete(table, conditions: null, allowAllRows: true)`,
  the guard is bypassed by the explicit flag, and the emitted statement is `DELETE FROM <table>` with no
  WHERE and no `1 = 1`

#### Scenario: An explicit constant-true filter routes to the same door

- **Given** a `DataBaseBulkStore<DB,T>` and the predicate `x => true`
- **When** `Delete(x => true)` is called
- **Then** `IsExplicitAllRows` returns true and the call is routed to `Connector.DeleteAll(typeof(T))` —
  the pre-existing idiom keeps working and reaches the same clean statement

#### Scenario: A captured-flag or arithmetic-true predicate is the same one node

- **Given** the predicates `x => 1 == 1` and `x => capturedFlag` where `capturedFlag` is true
- **When** either is passed to `Delete(filter)`
- **Then** `ExpressionNormalizer` funcletizes each to a single `ConstantExpression(true)`,
  `IsExplicitAllRows` returns true, and both reach the all-rows door — the test is on the normalized
  node, not on the source spelling

#### Scenario: Destroy remains distinct from emptying

- **Given** a caller wanting the table itself gone rather than emptied
- **When** they consult `DeleteAll`'s contract
- **Then** it directs them to `Destroy()` — `DeleteAll` empties, `Destroy` drops, and the two SHALL NOT
  be conflated

### Requirement: A backend overriding the public destructive methods repeats the guard

The system SHALL enforce the require-a-filter guard in every store that overrides the **public**
`Delete(filter)` / `Update(filter, …)` methods rather than the `protected *Core` methods, because such
an override bypasses the base class's guard entirely.

This SHALL be read together with the family convention that concrete stores override `protected *Core`
and **not** the public CRUD methods, precisely so the base can enforce invariants. The overrides that
require the repeat predate the guard and stand against that convention; repeating the guard is the
contained fix, and converting them to `*Core` is separate work.

#### Scenario: The InMemory store repeats the guard in its overriding Delete

- **Given** `AbstractInMemoryStore<T>` / `AbstractAsyncInMemoryStore<T>`, which override the public
  `Delete(filter)`
- **When** `Delete(null!)` is called
- **Then** the override throws `ArgumentNullException` itself rather than deleting the whole collection —
  reaching the base's guard is not possible from an override of the public method

#### Scenario: The MongoDB store repeats the guard on all four overrides

- **Given** `MongoDBStore<T>` / `AsyncMongoDBStore<T>`, which override the public `Delete(filter)` and
  `Update(filter, …)`
- **When** a null filter is passed to any of them
- **Then** each refuses, so the guard holds on the native `$set` / `DeleteMany` paths as well as on the
  portable ones

#### Scenario: The ElasticSearch stores are already covered by their own filter boundary

- **Given** the ElasticSearch stores' four public overrides
- **When** an untranslatable or null filter reaches a destructive path
- **Then** `ParseRequiredFilterQuery` already refuses it (CR-H047), so no additional repeat is required
  there — the invariant is enforced, by a different mechanism at the same boundary

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
