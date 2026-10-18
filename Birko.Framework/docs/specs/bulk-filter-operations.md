---
area: bulk-filter-operations
generated-at: 4b5bf7e6a33b66aa46da5e6adc40ff88e547da43
generated-on: 2026-10-16
sources:
  - ../Birko.Data.Core/Exceptions/WholeTableWriteException.cs
  - ../Birko.Data.Core/Expressions/BoundedFilterGuard.cs
  - ../Birko.Data.ElasticSearch/Stores/AsyncElasticSearchStore.cs
  - ../Birko.Data.ElasticSearch/Stores/ElasticSearchStore.cs
  - ../Birko.Data.ElasticSearch/Stores/ElasticSearchStoreHelper.cs
  - ../Birko.Data.MongoDB/Stores/AsyncMongoDBStore.cs
  - ../Birko.Data.MongoDB/Stores/MongoDBStore.cs
  - ../Birko.Data.MongoDB/Stores/MongoPropertyUpdateTranslator.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractAsyncConnector_Delete.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractAsyncConnector_Select.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractAsyncConnector_Update.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractConnector_Delete.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractConnector_Select.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractConnector_Update.cs
  - ../Birko.Data.SQL/SQL/DataBase_OrderBy.cs
  - ../Birko.Data.SQL/Stores/AsyncDataBaseBulkStore.cs
  - ../Birko.Data.SQL/Stores/DataBaseBulkStore.cs
  - ../Birko.Data.SQL/Stores/PropertyUpdateSqlTranslator.cs
  - ../Birko.Data.Stores/AbstractAsyncBulkStore.cs
  - ../Birko.Data.Stores/AbstractBulkStore.cs
  - ../Birko.Data.Stores/IAsyncBulkStore.cs
  - ../Birko.Data.Stores/IBulkStore.cs
  - ../Birko.Data.Stores/OrderBy.cs
  - ../Birko.Data.Stores/PropertyAssignment.cs
  - ../Birko.Data.Stores/PropertyUpdate.cs
# source-commits: omitted — since the monorepo migration (TASK-457) every source above is inside this
# repo, so `generated-at` covers them (regen step 5c). The old per-sibling shas no longer resolve here.
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-12 over 325 feature-linked tasks: 59 resolved, 266 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 9 of those are refused by the state gate (todo/cancelled). Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 266
---
# Bulk filter-based Update/Delete and Read semantics

## Purpose

A *bulk store* is a Birko data store that can act on many entities at once: read a filtered,
ordered, paged collection; create/update/delete a whole collection; and — the part this capability
exists for — **update or delete every entity matching a LINQ predicate without the caller ever
materialising those entities**. Two update flavours are offered: `Update(filter, Action<T>)`, a
portable read-modify-save loop, and `Update(filter, PropertyUpdate<T>)`, a declarative set of
property assignments — constant sets and numeric increments — that a backend is free to translate into
one native statement (`UPDATE … SET … WHERE …`, MongoDB `$set` / `$inc`, ElasticSearch `UpdateByQuery`).

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

Scoped regen at `5d6f328455d8034ad7070ba877fb9f3af0b2db57` for **TASK-212** — the MongoDB half of the same rule. `RequireFilter`
refused only a *null* filter, so a filter that was present and covered everything went straight to
`DeleteMany`. Measured offline (the driver renders a filter with no connection):
`x => !empty.Contains(x.Field)` becomes `{ "Field": { "$nin": [] } }` — **one element**, so the
"refuse an empty filter document" guard that seemed obvious would never have fired. The guard is therefore on
the **expression**, in `Birko.Data.Stores`, shared by every backend that overrides the public destructive
methods; `WholeTableWriteException` moved to `Birko.Data.Core/Exceptions/` so one `catch` still selects it.
Note this area's globs **do** cover both MongoDB stores, so unlike the `AbstractConnectorBase.cs` gap above,
this diff is real evidence for the change.

Scoped regen at `c78cfca` for **TASK-218**, which touches this area only because the MongoDB stores are
where the rewrite is wired. On .NET 9+ an **array**'s `set.Contains(x.Col)` binds to
`MemoryExtensions.Contains`, which the driver's LINQ translator does not know — `NotSupportedException:
Specified method is not supported`, naming no method, with a working `List<T>` look-alike one keystroke
away. Both destructive overloads in each store now normalise the caller's filter through
`SpanContains.Rewrite` before `RequireFilter` / `RequireBoundedFilter` / the driver see it, so all three
observe one shape. Worth noting for this area specifically: **the guard was never affected** —
`PredicateScope` already unwrapped the span conversion, and that unwrap is now the shared producer both it
and the rewrite call. The behavioural requirement lives in `filter-expression-translation`; what changed
here is only that a previously-untranslatable filter now reaches the destructive paths at all.

Scoped regen at `e37bebf` for **TASK-215**, which set out to wire the same guard into two more backends and
found the hole one layer up: `AbstractBulkStore`'s **own** six filter-based destructive wrappers never
called `RequireBoundedFilter` either. TASK-212 added the helper to the base and wired only MongoDB's
overrides. Measured on `JsonStore`, which overrides none of the six, `Delete(x => !empty.Contains(x.Value))`
left **0 of 3** rows with no exception — so the defect was live on every portable backend, not on the two
this task named. Guarding the base fixes JSON, XML, RavenDB, CosmosDB and InfluxDB by construction, and is
what stops a partially-overriding backend presenting a refusing `Delete` beside a wiping `Update`.

**`.map.yml` was edited** — the fourth instance of the under-coverage the notes above describe twice.
ElasticSearch's four overrides were reachable by no glob in this area, so a regen could not have seen their
behaviour change at all; both store files are now sources. The `AbstractConnectorBase.cs` gap is unchanged
and still waiting on TASK-208.

Scoped regen at `2b8663c1` for **TASK-498** — `PropertyUpdate<T>` gains an atomic `Increment` / `Decrement`.
`Assignments` stopped being a `(Property, Value)` tuple list and became a closed `SetAssignment` |
`IncrementAssignment` hierarchy reached only through `Match`; the SQL bulk stores now always build their SET
fragments in `PropertyUpdateSqlTranslator` and hand them to the connector's expression path; MongoDB and
ElasticSearch render increments as `$inc` / painless `+=`. **`.map.yml` was edited** — the fifth instance of
the under-coverage the notes above describe: the three new translator/assignment files and
`ElasticSearchStoreHelper.cs` (where the painless script has always been built) were reachable by no glob in
any area. The resolved `sources:` list also lacked the two ElasticSearch stores the map had listed since
TASK-215; both are now in it. The MongoDB and ElasticSearch native translations had no requirement here until
this regen, and now have one.

Full regen on 2026-10-12, after **TASK-513** (an increment's right-hand side is the connector's
`IncrementExpression`, so a SQLite decimal adds through `birko_decimal_add`), **TASK-555** (the SQL bulk stores'
per-item `UpdateCore` collects each item's `EntityNotFoundException` and throws one after writing the stored
items, and a single-entity `Update` of an unstored entity now throws — which the read-then-loop action path
does not catch) and **TASK-163** (the in-memory stores no longer override the public `Delete(filter)`; they
inherit the base's, guards included). TASK-331 and TASK-556 / TASK-557 also touched these sources without
changing a requirement here: the connector's expression-path `UPDATE` is now gated on its SET fragments rather
than its bound values (every `PropertyUpdate` assignment binds one, so the bulk path cannot observe it), and the
identity and delegate changes are specified in `store-crud-contract`.

Scoped regen on 2026-10-16, after **TASK-170** (a SQL store with no connector refuses at lazy init; the sync
SQL bulk read and the async sync-connector fallback are materialised; `Set` of a nested member is refused;
two sets of one property are last-wins on SQL; an empty `PropertyUpdate` writes nothing on the portable path;
a row deleted between the read and its write is skipped; `AggregateAsync` falls back to the sync connector),
**TASK-629** (a SQL bulk collection write is one transaction), **TASK-501** / **TASK-502** (the ElasticSearch
script writes a null as `null` under the stored field name and the by-query response is checked),
**TASK-153** (an ElasticSearch store with no client throws) and **TASK-272** (the destructive funnels name the
table through `TableIdentifier`). TASK-159, TASK-171, TASK-567, TASK-625 and TASK-632 touched these sources
too; their changes here are `PropertyUpdate.Clone`, `OrderBy.ThenByName` and the duplicate / unique
translation around the bulk collection writes.

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
  orderBy?.ToDictionary(), limit, offset)` and returns `.OfType<T>().ToList()` of the result — materialised
  (TASK-170): the connector's `Select` is an iterator that holds its connection and `DbDataReader` open until
  enumeration ends, so the rows are read and the connection released before `Read` returns, and a write
  issued inside `foreach (var x in store.Read(f))` no longer runs against a live reader

#### Scenario: SQL bulk read with no connector refuses at lazy init

- **Given** a `DataBaseBulkStore<DB,T>` whose `Connector` is null (its `SetSettings` was never called)
- **When** `Read(filter)` is called
- **Then** `EnsureInitialized()` reaches `InitCore`, which throws `InvalidOperationException` ("… has no
  database connector: call SetSettings with the database's settings first.") — before TASK-170 the read
  answered `Enumerable.Empty<T>()`, so a misconfigured store looked like an empty table. `ReadCore`'s own
  null-connector fallback to an empty sequence remains but is no longer reached through `Read`

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

#### Scenario: A secondary key named by string is resolved like the first

- **Given** `OrderBy<T>.ByName("Rank").ThenByName("Label", descending: true)` (TASK-171)
- **When** the keys are resolved
- **Then** `ThenByName` appends an `OrderByField` carrying the caller's string exactly as `ByName` does, and
  that key passes through the same three lookups — so the clause is `ORDER BY Rank ASC, label_col DESC`, and
  an unknown or injected secondary key raises the same `ArgumentException`

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

#### Scenario: A match deleted between the read and its write is skipped

- **Given** a bulk store whose filter matches three entities, the second of which is deleted by another caller
  after the read has materialised the list
- **When** `Update(filter, action)` / `UpdateAsync(filter, action, ct)` reaches it
- **Then** the single-entity `Update` throws `EntityNotFoundException` for it (TASK-555), and the loop catches
  and discards it (TASK-170, SH-M434): the row no longer matches the filter, so it is skipped as a SQL
  `UPDATE … WHERE` would skip it, the first and third entities are written, and the call completes without an
  exception — the same holds for the `UpdateAll` action loops of the abstract bases and for both SQL bulk
  stores' action loops

#### Scenario: The SQL bulk store keeps the same read-modify-save action path

- **Given** a `DataBaseBulkStore<DB,T>`
- **When** `Update(filter, Action<T>)` is called
- **Then** it uses the identical read-then-per-entity-`Update` loop as the abstract base — there is no
  native translation for the action overload
- **And** both filter guards run **before** the read: `RequireFilter` for a null filter and
  `RequireBoundedFilter` for one that covers every row, because once the loop starts every statement it
  issues is individually key-bounded and nothing downstream can recognise the scope

### Requirement: Portable PropertyUpdate fallback rewrites whole entities

The system SHALL, in `AbstractBulkStore<T>` / `AbstractAsyncBulkStore<T>`, implement
`Update(filter, PropertyUpdate<T>)` by delegating to the action overload with
`entity => updates.ApplyTo(entity)` — that is, by reading each matching entity, mutating it via
reflection and re-saving the **whole entity** — and SHALL return without reading or writing anything when
the update has no assignments.

#### Scenario: Named properties are applied via reflection then the entity is saved

- **Given** an `AbstractBulkStore<T>` and `new PropertyUpdate<T>().Set(x => x.Name, "n")`
- **When** `Update(filter, updates)` is called
- **Then** each matching entity is read, `ApplyTo` sets `Name` on it by reflection, and the single-entity
  `Update(item)` persists the entity — including every other property as it was read

#### Scenario: An empty PropertyUpdate writes nothing

- **Given** an `AbstractBulkStore<T>` / `AbstractAsyncBulkStore<T>` and a `PropertyUpdate<T>` with zero assignments
- **When** `Update(filter, updates)` / `UpdateAsync(filter, updates, ct)` / `UpdateAll(updates)` /
  `UpdateAllAsync(updates, ct)` is called
- **Then** it returns (a completed task on the async side) without reading any entity and without calling the
  single-entity `Update` — as the SQL path already did. Before TASK-170 (SH-M032) every matched entity was read
  and re-saved, so `UpdatedAt`, audit fields and concurrency versions moved for rows nobody changed
- **And** on the filtered overloads `RequireFilter` and `RequireBoundedFilter` still run first, so an empty
  update with a null or unbounded filter is refused rather than silently ignored

#### Scenario: Async PropertyUpdate fallback delegates without awaiting itself

- **Given** an `AbstractAsyncBulkStore<T>` and a `PropertyUpdate<T>` with at least one assignment
- **When** `UpdateAsync(filter, updates, ct)` is called
- **Then** it returns the task from `UpdateAsync(filter, entity => updates.ApplyTo(entity), ct)`
  directly, without an intermediate `await`

#### Scenario: An increment in the fallback is read-add-save and not atomic

- **Given** an `AbstractBulkStore<T>` with no native translation and
  `new PropertyUpdate<T>().Increment(x => x.Count, 5)` over an entity whose `Count` is 10
- **When** `Update(filter, updates)` is called
- **Then** the entity is read, `ApplyTo` sets `Count` to 15 and the whole entity is saved — so a
  concurrent writer between the read and the save can lose one of the two increments; only the native
  translations below apply an increment atomically

### Requirement: PropertyUpdate assignment collection and reflection application

The system SHALL let callers accumulate assignments fluently via `PropertyUpdate<T>.Set(property, value)`,
`Increment(property, delta)` and `Decrement(property, delta)`, SHALL preserve insertion order in the
internal `Assignments` list, and SHALL store each entry as one of a **closed** set of kinds —
`SetAssignment` (carrying `Value`) or `IncrementAssignment` (carrying `Delta`) — with no shared value
member, so that a translator can reach an operand only through `PropertyAssignment.Match(set, increment)`
and must name the increment branch to read a delta. `ApplyTo` SHALL apply the entries in order, resolving
each target by unwrapping a `Convert` / `ConvertChecked` body to its operand and taking the
`MemberExpression`'s `PropertyInfo`: a set assigns its value, an increment reads the current value and
assigns `checked(current + delta)` in the property's own type. A selector that does not resolve to a
property SHALL throw rather than be skipped. `Set` SHALL refuse, when it is **declared**, any selector that is
not a direct access to a top-level property of `T` — a single `Convert` over one is allowed — with
`ArgumentException` for the `property` parameter (TASK-170, SH-M026), so the reflection refusals of `ApplyTo`
below are no longer reachable through the public builder. `Clone()` SHALL return a new `PropertyUpdate<T>`
holding the same assignments in the same order, so a caller (a decorator adding its own assignment) can extend
a copy without mutating the instance it was given (TASK-159).

#### Scenario: Set returns the same instance for chaining

- **Given** a fresh `PropertyUpdate<T>`
- **When** `.Set(x => x.Name, "a").Set(x => x.Count, 5)` is chained
- **Then** both calls return the same instance and `Assignments` holds two entries in that order

#### Scenario: Set and Increment chain into one update

- **Given** a fresh `PropertyUpdate<T>`
- **When** `.Set(x => x.Name, "a").Increment(x => x.Count, 1)` is chained
- **Then** both calls return the same instance and `Assignments` holds a `SetAssignment` then an
  `IncrementAssignment`

#### Scenario: A boxed/converted property selector is unwrapped

- **Given** an assignment whose lambda body is a `Convert` / `ConvertChecked` `UnaryExpression` (e.g. one
  inserted for a value-type-to-object selector)
- **When** `ApplyTo(entity)` runs
- **Then** the operand is taken as the `MemberExpression` and the underlying property is set
- **And** any other unary node (a negation, say) is not unwrapped, so its selector does not resolve
- **And** `Set` itself unwraps only `Convert`, so a `ConvertChecked` or other unary selector is refused at
  declaration with `ArgumentException` and never reaches `ApplyTo`

#### Scenario: A non-property member is refused

- **Given** a selector whose resolved member is not a `PropertyInfo` (for example a public field)
- **When** `Set(selector, value)` is called
- **Then** it throws `ArgumentException("Set needs a direct access to a top-level property of T; '…' is not
  one.")` and no assignment is recorded
- **And** `ApplyTo` keeps its own refusal — `ArgumentException("Unable to resolve property from expression:
  …")`, with no further assignment applied — for an assignment that reached it by any other route

#### Scenario: A non-member selector throws ArgumentException

- **Given** a selector whose lambda body is neither a `MemberExpression` nor a convert over one (for
  example `x => x.Compute()`)
- **When** `Set(selector, value)` is called
- **Then** it throws `ArgumentException` for the `property` parameter, before any backend sees the update

#### Scenario: A nested member selector is refused at Set

- **Given** `Set(x => x.Owner.Name, "n")`
- **When** the call is made
- **Then** it throws `ArgumentException`, because the member's `Expression` is not the lambda's parameter —
  before TASK-170 (SH-M026) the SQL path resolved the leaf alone and wrote `T`'s own `Name` column while the
  portable path threw, the rule `Increment` already applied

#### Scenario: Clone copies the assignments without sharing them

- **Given** a `PropertyUpdate<T>` holding `Set(x => x.Name, "a")`
- **When** `Clone()` is called and `Set(x => x.Count, 5)` is added to the copy
- **Then** the copy holds both assignments in order and the original still holds only the first

#### Scenario: An increment adds to the current value

- **Given** an entity whose `Count` is 10 and `new PropertyUpdate<T>().Increment(x => x.Count, 5)`
- **When** `ApplyTo(entity)` runs
- **Then** `Count` is 15

#### Scenario: A fallback increment that overflows throws

- **Given** an entity whose `int Count` is `int.MaxValue` and `Increment(x => x.Count, 1)`
- **When** `ApplyTo(entity)` runs
- **Then** the checked addition throws `OverflowException` and the property is not assigned

#### Scenario: Decrement is a negated increment

- **Given** `new PropertyUpdate<T>().Decrement(x => x.Count, 3)`
- **When** the update is built
- **Then** `Assignments` holds one `IncrementAssignment` whose `Delta` is `-3` — there is no separate
  decrement kind
- **And** `Decrement(x => x.Count, int.MinValue)` throws `OverflowException`, because the delta has no
  negation, and an unsigned property does not compile against `Decrement` (`ISignedNumber<T>`)

#### Scenario: Duplicate assignments to one property are both applied in order

- **Given** `new PropertyUpdate<T>().Set(x => x.Name, "a").Set(x => x.Name, "b")`
- **When** `ApplyTo(entity)` runs
- **Then** both `SetValue` calls execute in order and the entity ends up with `"b"` (last wins)

### Requirement: An increment names one direct numeric property and is its only assignment

The system SHALL refuse, when `Increment` / `Decrement` is **declared** (not when it is translated), any
selector that is not a direct access to a top-level property of exactly `TProperty`, any `TProperty`
outside `short`, `int`, `long`, `float`, `double` and `decimal`, and any combination of an increment with
another assignment to the same member path. A `Set` beside another `Set` on the same member SHALL remain
allowed.

#### Scenario: A cast or nested selector is refused

- **Given** `Increment(x => (int)x.NullableCount, 1)`, `Increment(x => (long)x.IntCount, 1)` or
  `Increment(x => x.Address.Number, 1)`
- **When** the call is made
- **Then** it throws `ArgumentException` for the `property` parameter — a cast hides the property's real
  (possibly nullable) type and a nested member would be resolved as its leaf by the providers
- **And** a nullable property selected directly (`x => x.NullableCount`) does not compile, because
  `int?` does not satisfy `INumber<T>`

#### Scenario: A numeric type not every provider stores as a number is refused

- **Given** a direct selector on a `byte`, `uint`, `ulong`, `sbyte`, `Half` or `Int128` property
- **When** `Increment` is called
- **Then** it throws `ArgumentException` naming the supported types, although the type satisfies
  `INumber<T>`

#### Scenario: An increment cannot share its member with another assignment

- **Given** a `PropertyUpdate<T>` that already sets or increments `Count`, including through a converted
  selector such as `x => (object)x.Count`
- **When** `Increment(x => x.Count, 1)` is added, or `Set(x => x.Count, 5)` is added after an increment
  of it
- **Then** it throws `InvalidOperationException` naming `Count` and the assignment is not recorded

### Requirement: SQL PropertyUpdate translates to a single UPDATE … SET … WHERE

The system SHALL override `Update(filter, PropertyUpdate<T>)` in `DataBaseBulkStore<DB,T>` and
`AsyncDataBaseBulkStore<DB,T>` to render the assignments through the shared
`PropertyUpdateSqlTranslator` — each column resolved through `DataBase.GetFieldFromLambda` and emitted
bare, a set as `col = @SETcol` and an increment as `col = ` followed by the store `Connector`'s
`IncrementExpression(field, col, @SETcol)` — `col + @SETcol` by default — translate the filter through
`DataBase.ParseConditionExpression`, and issue one connector
`Update(tableName, fields, values, conditions, isExpressionValues: true, allowAllRows)` — reading no
entities and invoking no per-entity update path. Two assignments to one member SHALL be last-wins, as
`ApplyTo` applies them; two different members mapped onto one column SHALL be refused. The `@SET…` parameter name SHALL have one producer,
`AbstractConnector.SetParameterName(column)` (`"@SET"` + the column with `.` removed), used both by these
fragments and by the connector's own non-expression SET rendering.

#### Scenario: Two assignments become one statement with two SET columns

- **Given** a `DataBaseBulkStore<DB,T>` with a non-null `Connector` and
  `new PropertyUpdate<T>().Set(x => x.Name, "n").Set(x => x.Count, 5)`
- **When** `Update(filter, updates)` is called
- **Then** `EnsureInitialized()` runs, the table is loaded via `DataBase.LoadTable(typeof(T))`,
  `fields` is populated as index → SET fragment (`Name = @SETName`, `Count = @SETCount`), `values` as
  parameter name → value, the filter is parsed into conditions, and
  `Connector.Update(table.Name, fields, values, conditions, true, allowAllRows)` is invoked exactly once

#### Scenario: A set and an increment share one UPDATE

- **Given** a SQL bulk store and `new PropertyUpdate<T>().Set(x => x.Name, "n").Increment(x => x.Count, 1)`
- **When** `Update(filter, updates)` is called
- **Then** one statement is issued, `UPDATE <quoted table> SET Name = @SETName, Count = Count + @SETCount
  WHERE …`, with the column names unquoted and the delta bound as `@SETCount`; the table is rendered by the
  connector's `TableIdentifier` (TASK-272), which is the bare `QuoteIdentifier(name)` for a model without a
  schema and the schema-qualified or schema-prefixed relation for one with
- **And** the addition happens on the server, so N concurrent `Increment(x => x.Count, 1)` calls on one
  row leave it exactly N higher

#### Scenario: A SQLite decimal increment adds through the exact-decimal function

- **Given** a SQLite store and `new PropertyUpdate<T>().Increment(x => x.Amount, 0.1m)` on a `decimal` property
  (a `Decimal`, `VarNumeric` or `Currency` field)
- **When** `Update(filter, updates)` is called
- **Then** the fragment is `Amount = birko_decimal_add(Amount, @SETAmount)` — `SqLiteConnector` overrides
  `IncrementExpression` for a decimal column, because SQLite's own `+` goes through a double (TASK-513); every
  other column, and every other provider, keeps `col + @SETcol`
- **And** on the async store the fragment is rendered by the sync `Connector` even when the statement runs on
  the `AsyncConnector`

#### Scenario: A null assignment value becomes DBNull

- **Given** an assignment `Set(x => x.Name, null)`
- **When** the SQL override builds its value map
- **Then** it stores `DBNull.Value` for that parameter (`value ?? DBNull.Value`), so the statement sets
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
  `ArgumentException("Unable to resolve property from expression: …", "expr")` and no statement is issued —
  though `Set` / `Increment` already refuse such a selector when it is declared, so this is a backstop

#### Scenario: A property that maps to no column throws ArgumentException naming it

- **Given** `Set(x => x.Ignored, v)` where `Ignored` is an `[IgnoreField]` / `[NotMapped]` / unsupported-type
  property
- **When** `Update(filter, updates)` is called on a SQL bulk store
- **Then** `GetFieldFromLambda` throws `ArgumentException("'…' names T.Ignored, which is not a mapped
  column.", "expr")` and no statement is issued — before TASK-170 (SH-M027) `First()` threw
  `InvalidOperationException` "Sequence contains no elements", naming nothing

#### Scenario: Two assignments to the same property keep the last

- **Given** `new PropertyUpdate<T>().Set(x => x.Name, "a").Set(x => x.Name, "b")` on a SQL bulk store
- **When** `Update(filter, updates)` is called
- **Then** the translator removes the earlier fragment and its `@SETName` value and keeps the second, so one
  statement sets `Name = @SETName` bound to `"b"` — the same result `ApplyTo` reaches on the portable path
  (TASK-170, SH-M030; it used to throw `ArgumentException` on the duplicate dictionary key). Only two sets can
  reach this, because an increment beside another assignment to its member is refused when the update is
  declared

#### Scenario: Two different properties mapped onto one column are refused

- **Given** two properties whose fields share one column name (compared `OrdinalIgnoreCase`) and a
  `PropertyUpdate<T>` setting both
- **When** `Update(filter, updates)` is called on a SQL bulk store
- **Then** the translator throws `InvalidOperationException` naming both member paths and the column, and no
  statement is issued

#### Scenario: The async override prefers the async connector

- **Given** an `AsyncDataBaseBulkStore<DB,T>` whose `AsyncConnector` is non-null
- **When** `UpdateAsync(filter, updates, ct)` is called
- **Then** it awaits `AsyncConnector.UpdateAsync(table.Name, fields, values, conditions, true, ct, allowAllRows)`
  with `isExpressionValues` explicitly true

#### Scenario: The async override falls back to the sync connector on a thread-pool thread

- **Given** an `AsyncDataBaseBulkStore<DB,T>` with a non-null `Connector` but a null `AsyncConnector`
- **When** `UpdateAsync(filter, updates, ct)` is called
- **Then** it awaits `Task.Run(() => Connector!.Update(table.Name, fields, values, conditions, true, allowAllRows), ct)`

#### Scenario: No connector means the update is refused at lazy init

- **Given** a SQL bulk store whose `Connector` is null
- **When** `Update(filter, updates)` / `UpdateAsync(filter, updates, ct)` is called with a **non-null**
  filter
- **Then** `EnsureInitialized()` / `EnsureInitializedAsync(ct)` throws `InvalidOperationException` from
  `InitCore` / `InitCoreAsync` ("… has no database connector …"), without translating the filter and without
  writing anything — before TASK-170 (SH-M031) the call returned as if it had worked. The
  `Connector == null` early return that follows is no longer reached
- **And** with a null filter it instead throws `ArgumentNullException`, because `RequireFilter` precedes
  initialisation — a missing store does not excuse a missing filter

### Requirement: Document-store PropertyUpdate translations render sets and increments natively

The system SHALL, in `MongoDBStore<T>` / `AsyncMongoDBStore<T>`, render a non-empty `PropertyUpdate<T>`
through the shared `MongoPropertyUpdateTranslator.Build` as one combined update definition — a set as
`$set` and an increment as `$inc`, each under the target property's C# name — and issue one
`UpdateMany`, inside `TransactionContext` when one is set. It SHALL refuse an increment whose member is
serialized with a string representation before anything is sent. The system SHALL, in
`ElasticSearchStore<T>` / `AsyncElasticSearchStore<T>`, render the assignments through the shared
`ElasticSearchStoreHelper.BuildUpdateScript` as one inline painless script run by `UpdateByQuery`, and SHALL
throw unless that by-query write ran to completion (`ElasticSearchStoreHelper.RequireComplete`, shared with
`Delete(filter)`'s `DeleteByQuery`). Both backends run their filter guards first and return without writing
when the update has no assignments. MongoDB also returns without writing when its collection is missing;
an ElasticSearch store with no client does not return silently (TASK-153) — see the scenario below.

#### Scenario: MongoDB renders a set and an increment in one UpdateMany

- **Given** a `MongoDBStore<T>` with a collection and
  `new PropertyUpdate<T>().Set(x => x.Name, "n").Increment(x => x.Count, 1)`
- **When** `Update(filter, updates)` is called with a bounded filter
- **Then** one `UpdateMany` is issued whose update combines `{ $set: { Name: "n" } }` and
  `{ $inc: { Count: 1 } }`, and the server applies the increment atomically

#### Scenario: MongoDB refuses $inc on a string-stored member

- **Given** an entity whose `decimal Amount` is configured with a string representation
  (`[BsonRepresentation(BsonType.String)]`; the driver's default for `decimal` is Decimal128)
- **When** `Update(filter, new PropertyUpdate<T>().Increment(x => x.Amount, 1m))` is called
- **Then** it throws `NotSupportedException` naming `T.Amount` and suggesting
  `[BsonRepresentation(BsonType.Decimal128)]`, and nothing is sent to the server
- **And** the same member stored as `Decimal128` is incremented normally

#### Scenario: MongoDB writes under the C# property name

- **Given** a property mapped to a different element name (for example with `[BsonElement("n")]`)
- **When** it is set or incremented through a `PropertyUpdate<T>`
- **Then** the update names the C# property, not the mapped element name (TASK-503)

#### Scenario: MongoDB refuses a selector that is not a property

- **Given** an assignment whose selector resolves to no `PropertyInfo`
- **When** the update is translated
- **Then** it throws `ArgumentException("Unable to resolve property from expression: …")`

#### Scenario: ElasticSearch renders `=` for a set and `+=` for an increment

- **Given** an `ElasticSearchStore<T>` and `new PropertyUpdate<T>().Set(x => x.Name, "n").Increment(x => x.Count, 1)`
- **When** `Update(filter, updates)` is called
- **Then** the script is `ctx._source.name = params.p_Name; ctx._source.count += params.p_Count` — each
  field name is the field the document is stored under (`ElasticSearch.StoredFieldName`, the producer the
  filter and sort translators share: the `Name` a NEST mapping attribute declares, else the property name
  camel-cased), each parameter `p_<PropertyName>`

#### Scenario: ElasticSearch writes a mapping-renamed property where it is read

- **Given** a property carrying a NEST mapping attribute or `[PropertyName("n")]`
- **When** it is set or incremented through a `PropertyUpdate<T>`
- **Then** the script assigns `ctx._source.n`, not the camel-cased C# name (TASK-501)

#### Scenario: ElasticSearch writes a null value as a literal null

- **Given** `Set(x => x.Name, null)` on an ElasticSearch store
- **When** the script is built
- **Then** the fragment is `ctx._source.name = null` and no `p_Name` parameter is bound — before TASK-501 the
  value was bound as `string.Empty`, so a string was stored as `""` and a nullable number or date field was
  handed a string, which the mapping refused with a 400 that failed the whole update

#### Scenario: ElasticSearch refuses a by-query write that did not complete

- **Given** an `UpdateByQuery` (from `Update(filter, updates)`) or `DeleteByQuery` (from `Delete(filter)`),
  sync or async
- **When** the response reports a version conflict, is invalid, carries failures, or timed out
- **Then** a version conflict throws `StoreConcurrencyException` carrying `Applied` (documents updated /
  deleted) and `Conflicts`, stating that the remaining matches were not processed; any other failure throws
  `StoreException`; a timeout throws `StoreException` even when the response is otherwise valid (TASK-502) —
  the store used to discard the response, so a contended increment could be lost silently. The write is still
  partial: documents before the conflict stay written and the caller decides

#### Scenario: ElasticSearch refuses a selector that is not a property

- **Given** an assignment whose selector resolves to no `PropertyInfo` (a method call, or a public field)
- **When** `BuildUpdateScript` runs
- **Then** it throws `ArgumentException("Unable to resolve property from expression: …")`, as `ApplyTo` and
  the MongoDB translator do (TASK-501) — though `Set` / `Increment` already refuse such a selector when it is
  declared

#### Scenario: ElasticSearch with no client throws instead of reporting success

- **Given** an ElasticSearch store whose `SetSettings` was never called (`SetSettings` with any settings type
  other than ElasticSearch `Settings` now throws `ArgumentException`, TASK-153)
- **When** `Update(filter, updates)` / `Delete(filter)` or their async twins are called with a bounded filter
- **Then** the async store's lazy `InitAsync` calls `RequireConnector`, which throws `InvalidOperationException`
  ("… has no ElasticSearch client …"); the sync store's lazy `Init` dereferences `Connector!` first and so throws
  `NullReferenceException` before its own `RequireConnector` is reached — either way nothing is reported as
  written

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

#### Scenario: No connector means the delete is refused at lazy init

- **Given** a SQL bulk store whose `Connector` is null
- **When** `Delete(filter)` / `DeleteAsync(filter, ct)` is called with a **non-null** filter
- **Then** lazy initialisation throws `InvalidOperationException` ("… has no database connector …") and no
  rows are deleted — before TASK-170 (SH-M031) the method returned without error. On the async store an
  explicit `x => true` reaches the same refusal through `DeleteAllAsync`, which initialises first
- **And** with a null filter it instead throws `ArgumentNullException`, because `RequireFilter` precedes
  initialisation

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
  WHERE and no `1 = 1` — `<table>` being the connector's `TableIdentifier(tableName)` (TASK-272), which is
  exactly the quoted table name for a model without a schema

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

### Requirement: A present filter that constrains nothing is refused, on the expression

A filter being **non-null** does not make it bounded. The system SHALL refuse a filter-based destructive
operation whose predicate covers every entity, unless every entity was requested explicitly, and SHALL
decide that on the **LINQ expression** — before any backend translates it — via
`Birko.Data.Expressions.BoundedFilterGuard.Require`, backed by
`Birko.Data.Expressions.PredicateScope`.

The rule SHALL have exactly **one implementation**. `BoundedFilterGuard` is that implementation, and every
declaration site is a thin forwarder that supplies only the thing which genuinely differs — the name of the
all-rows door **this** caller offers. Three sites forward to it: `AbstractBulkStore<T>.RequireBoundedFilter`,
its twin on `AbstractAsyncBulkStore<T>`, and the private `RequireBoundedFilter` on each SQL bulk store.
Re-inlining the checks at any of them SHALL NOT be done: the rule previously had two implementations and one
uncovered hierarchy, which is exactly how the defect below survived.

The guard SHALL be called by the **base classes' own** filter-based destructive wrappers — all six of
`Delete(filter)`, `Update(filter, PropertyUpdate)`, `Update(filter, Action)` and their async twins — and
not only by the backends that override them. Those wrappers are read-then-loop, so an unbounded predicate
reaches `Read(filter)`, matches every entity, and deletes or rewrites the lot; every statement issued
carries its own key, so no backend query guard downstream can see it. Enforcing it on the base makes every
portable backend that overrides nothing (JSON, XML, RavenDB, CosmosDB, InfluxDB) correct by construction,
and is the only placement under which a backend that overrides *some* of the six cannot end up with a
`Delete` that refuses beside an `Update` that does not.

**Enforcing it on those two bases does NOT reach the SQL bulk stores, because they derive from neither.**
`DataBaseBulkStore<DB,T>` and `AsyncDataBaseBulkStore<DB,T>` implement `IBulkStore<T>` / `IAsyncBulkStore<T>`
directly and carry their own copies of the six filter-based overloads — which is also why they declare their
own private `RequireFilter`. So the guard SHALL be wired into that third hierarchy explicitly. Measured on
on-disk SQLite before it was:
`Update(x => !empty.Contains(x.Name), r => r.Name = "OVERWRITTEN")` gave **`thrown=NONE`, 3 of 3 rows
rewritten**, sync and async alike.

Which of the six need the wiring SHALL be **measured per overload, per direction**, not inferred. Measured:
`Delete(filter)` and `Update(filter, PropertyUpdate<T>)` — four of the six — reach the connector, where
`AddRequiredWhere` already refuses the same predicate, so they were already correct; the two
`Update(filter, Action<T>)` overloads were not, because that path issues a `SELECT` (where an always-true
predicate is legitimate) followed by per-row `UPDATE … WHERE Guid = @g` statements, each individually
bounded, so no conditionless statement is ever emitted for a statement-level guard to see. Every overload's
guard SHALL be present regardless of which mechanism supplies it, because a store presenting a `Delete`
that refuses beside an `Update` that rewrites every row is the split this family keeps arriving in.

The decision cannot be made on the translated query. Measured on MongoDB.Driver 3.2.0,
`x => !empty.Contains(x.Field)` renders `{ "Field": { "$nin": [] } }` — a **one-element** document,
indistinguishable by inspection from an ordinary field predicate, while selecting every document. A guard
asking "is the emitted filter empty?" therefore does not fire. This is the same shape as the SQL side's
`1 = 1`, which satisfied a guard testing whether anything had been rendered: **the guard tests what the
operation means, not whether output was produced.**

The refusal SHALL be `WholeTableWriteException` — the same type the SQL connectors throw, which for this
reason lives in `Birko.Data.Core/Exceptions/` beside `StoreException` rather than in `Birko.Data.SQL` — so
one `catch` selects the refusal on every backend. Its message SHALL name the deliberate door
(`DeleteAll()` / `UpdateAll(updates)`) and SHALL NOT reference SQL machinery a document store does not
have.

The named door SHALL be one the refused caller can actually take. An asynchronous store has no
`DeleteAll()`, so its refusal SHALL name `DeleteAllAsync()` / `UpdateAllAsync(updates)` — a message
pointing at a method that does not exist for that caller is a wall wearing a door's label, the same defect
as a refusal whose opt-out throws.

An **explicit** `x => true` (and every spelling `ExpressionNormalizer` folds into it) SHALL be checked
**first** and allowed through: it is the documented `DeleteAll()` synonym, and a guard whose opt-out is
also refused is a wall rather than a door. Only a predicate that *happens* to cover everything is refused.

`PredicateScope` SHALL be narrow and answer `false` when uncertain, because a refusal that fires on a
predicate which does constrain something breaks working code. Specifically it SHALL NOT treat as unbounded:
a string `Contains` (a substring test, not set membership), a collection that references the entity (its
emptiness varies per entity), a null collection, or a collection it cannot evaluate.

#### Scenario: The SQL bulk stores refuse it on the action overload, sync and async

- **Given** `SQLiteStore<Row>` or `AsyncSQLiteStore<Row>` over an on-disk database holding three rows, and
  the filter `x => !empty.Contains(x.Name)`
- **When** `Update(filter, r => r.Name = "WIPED")` / `UpdateAsync(filter, action, ct)` is called
- **Then** each throws `WholeTableWriteException` and all three rows keep their original values — before
  the guard was wired into this hierarchy the call rewrote **3 of 3** rows and threw nothing
- **And** the async refusal names `UpdateAllAsync(updates)` while the sync one names `UpdateAll(updates)`

#### Scenario: The other four SQL overloads are refused by the connector rather than by this guard

- **Given** the same stores and the same filter
- **When** `Delete(filter)` or `Update(filter, PropertyUpdate<T>)` is called
- **Then** each throws `WholeTableWriteException` from `AddRequiredWhere`, because those paths do emit a
  single conditionless statement — a different mechanism reaching the same refusal, which is why the scope
  of the wiring had to be measured per overload instead of applied to all six

#### Scenario: A decorator over the SQL bulk store inherits the guard by delegating

- **Given** `CachedAsyncDataBaseBulkStore<SqLiteConnector, Widget>` — the only class in the framework that
  overrides `UpdateAsync(filter, Action<T>)` — holding three rows
- **When** `UpdateAsync(x => !empty.Contains(x.Name), w => w.Body = "WIPED")` is called
- **Then** it is refused and all three rows keep their bodies, because the override wraps
  `base.UpdateAsync` rather than re-implementing the read-then-loop
- **And** `UpdateAsync(x => true, action)` still rewrites every row through the same decorator, so the
  opt-out holds at every layer it passes through

#### Scenario: An empty negated Contains is refused on all four MongoDB overrides

- **Given** `MongoDBStore<T>` / `AsyncMongoDBStore<T>` and the filter `x => !empty.Contains(x.Amount)` over
  an empty collection
- **When** `Delete` / `Update` / `DeleteAsync` / `UpdateAsync` is called
- **Then** each throws `WholeTableWriteException` before any driver call, so the whole-collection
  `DeleteMany` never issues

#### Scenario: The base wrappers refuse it on a backend that overrides nothing

- **Given** `JsonStore<T>`, which overrides none of the six filter-based destructive methods, holding
  three rows, and the filter `x => !empty.Contains(x.Value)`
- **When** `Delete(filter)` or `Update(filter, updates)` is called
- **Then** each throws `WholeTableWriteException` and all three rows survive unchanged — before the guard
  reached the base, the delete left **0 of 3** and the update rewrote **3 of 3**, in both cases with no
  exception and no log entry

#### Scenario: A backend that overrides none of the six gets every guard from the base

- **Given** `AbstractInMemoryStore<T>` / `AbstractAsyncInMemoryStore<T>`, which override none of the six
  filter-based destructive methods — until TASK-163 (SH-M312) they overrode the public `Delete(filter)` and
  repeated the guard there
- **When** an unbounded filter is passed to any of the six
- **Then** every one refuses through the base wrappers, so the store cannot present a `Delete` that refuses
  beside an `Update` that rewrites every row, and a filter-based delete reaches `DeleteCore`

#### Scenario: An empty negated Contains is refused on all four ElasticSearch overrides

- **Given** `ElasticSearchStore<T>` / `AsyncElasticSearchStore<T>` and `x => !empty.Contains(x.Count)`
- **When** `Delete` / `Update` / `DeleteAsync` / `UpdateAsync` is called
- **Then** each throws `WholeTableWriteException` **before** `EnsureInitialized()`, so the refusal holds
  identically whether or not a cluster is reachable and no `_delete_by_query` is issued

#### Scenario: The ElasticSearch rendering is why the guard cannot sit on the query

- **Given** `x => !empty.Contains(x.Count)` and `x => !some.Contains(x.Count)`
- **When** each is translated by `ParseRequiredFilterQuery`
- **Then** the first becomes `bool { must_not: [ match_none ] }` — which selects **every** document — and
  the second becomes `bool { must_not: [ terms ] }`; the two are the same query structure differing only
  in the inner type, so a guard inspecting the rendered query would have to enumerate every backend's
  spellings of "everything"

#### Scenario: An OR chain containing an unbounded term is refused

- **Given** `x => x.Amount > 4 || !empty.Contains(x.Amount)`, which the driver renders as a `$or` whose
  second branch is `$nin: []`
- **When** a destructive method is called
- **Then** it is refused — `A || TRUE` is `TRUE`, so the chain still selects everything

#### Scenario: The explicit synonym is not refused

- **Given** `x => true`, or a captured true flag, or `x => 1 == 1`
- **When** a destructive method is called
- **Then** the guard allows it through, because the explicit-door check runs before the scope check

#### Scenario: A bounded filter and a matches-nothing filter are both allowed

- **Given** `x => x.Amount > 4`, `x => !some.Contains(x.Amount)` over a non-empty set, or
  `x => empty.Contains(x.Amount)` (which matches nothing)
- **When** a destructive method is called
- **Then** none is refused — the guard fires on "everything", never on "something" or on "nothing"

#### Scenario: An AND chain is unbounded only when every term is

- **Given** `x => x.Amount > 4 && !empty.Contains(x.Amount)`
- **When** the scope is analysed
- **Then** it is bounded, because `A && TRUE` is `A`

#### Scenario: A per-entity collection cannot make a predicate unbounded

- **Given** `x => !x.Tags.Contains(1)`, whose collection is a property of the entity
- **When** the scope is analysed
- **Then** it is not reported unbounded: the collection's emptiness varies per entity, and it cannot be
  evaluated without one

#### Scenario: A null filter keeps its own distinct refusal

- **Given** `Delete(null!)`
- **When** the call is made
- **Then** `RequireFilter` still throws `ArgumentNullException` — passing null for a parameter declared
  non-nullable is a different mistake from asking for an unbounded scope, and the two guards stay separate

### Requirement: A backend overriding the public destructive methods repeats the guard

The system SHALL enforce the require-a-filter guard in every store that overrides the **public**
`Delete(filter)` / `Update(filter, …)` methods rather than the `protected *Core` methods, because such
an override bypasses the base class's guard entirely.

This SHALL be read together with the family convention that concrete stores override `protected *Core`
and **not** the public CRUD methods, precisely so the base can enforce invariants. The overrides that
require the repeat predate the guard and stand against that convention; repeating the guard is the
contained fix, and converting them to `*Core` is separate work.

An override that **delegates** to the base rather than re-implementing it satisfies this requirement
without repeating anything, and is the preferred shape where the override only wraps behaviour. That
distinction is load-bearing rather than stylistic: an override which inlined the base's loop to save a
call would silently lose the guard while its own tests stayed green, so where such an override exists the
delegation SHALL be pinned by a test.

#### Scenario: The InMemory store no longer overrides Delete, so it repeats nothing

- **Given** `AbstractInMemoryStore<T>` / `AbstractAsyncInMemoryStore<T>`, which since TASK-163 (SH-M312) inherit
  the public `Delete(filter)` instead of overriding it
- **When** `Delete(null!)` or `Delete(x => !empty.Contains(x.Field))` is called
- **Then** the base wrapper's `RequireFilter` / `RequireBoundedFilter` refuse it (`ArgumentNullException` /
  `WholeTableWriteException`) and nothing is removed — the obligation this requirement places on an
  overriding store no longer applies to it. The scope guard still matters most here: the predicate is
  compiled and run as a C# delegate, so `!empty.Contains(x.Field)` is true of every entity by definition

#### Scenario: The MongoDB store repeats the guard on all four overrides

- **Given** `MongoDBStore<T>` / `AsyncMongoDBStore<T>`, which override the public `Delete(filter)` and
  `Update(filter, …)`
- **When** a null filter is passed to any of them
- **Then** each refuses, so the guard holds on the native `$set` / `DeleteMany` paths as well as on the
  portable ones
- **And** each also calls `RequireBoundedFilter`, so a *present* filter that covers every document is
  refused there too — the null case was only half the guard

#### Scenario: The ElasticSearch filter boundary covers the null case but not the scope case

- **Given** the ElasticSearch stores' four public overrides
- **When** an untranslatable or null filter reaches a destructive path
- **Then** `ParseRequiredFilterQuery` refuses it (CR-H047) — the invariant is enforced there by a
  different mechanism at the same boundary, so the null half needs no repeat
- **And when** a filter that is present but covers every document reaches the same path
- **Then** that boundary does **not** fire, because a query *is* produced and it looks ordinary; the
  overrides SHALL therefore call `RequireBoundedFilter` themselves. A null check is not a scope check —
  the same distinction that separates `RequireFilter` from `RequireBoundedFilter` everywhere else

### Requirement: Bulk collection operations initialise lazily and default to per-item loops

The system SHALL run `EnsureInitialized()` / `EnsureInitializedAsync(ct)` at the entry of every public
bulk collection operation before delegating to its `*Core` method, and the SQL bulk stores' default
`*Core` implementations SHALL loop over the collection invoking the corresponding single-entity
operation. The default bulk update SHALL catch each item's `EntityNotFoundException`, keep going, and throw one
`EntityNotFoundException` naming every missed `Guid` after the loop (TASK-555).

On the SQL bulk stores, each public collection `Create` / `Update` / `Delete` (and async twin) SHALL run its
`*Core` as **one transaction** (`Atomically` / `AtomicallyAsync`, TASK-629): inside an existing boundary — the
store's `TransactionContext`, or an ambient SQL transaction for the same settings id — it joins that boundary;
otherwise it opens a connection and transaction, publishes them as the ambient scope so every per-item write
and a provider's native bulk insert join it, commits at the end and rolls back on any failure or cancellation.
As that transaction's owner it SHALL run the work registered on it with `AmbientSqlTransaction.OnCommitted` once
after the commit succeeds, and drop it on rollback or a failed commit (TASK-669).
The collection `Create` SHALL translate a duplicate-key failure into the shared duplicate-create
refusal, and the collection `Update` SHALL translate a unique-index violation into
`StoreUniqueViolationException`, on the abstract bases and the SQL bulk stores alike (TASK-567, TASK-625,
TASK-632).

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

#### Scenario: SQL bulk update writes the stored items and reports the rest once

- **Given** a `DataBaseBulkStore<DB,T>` / `AsyncDataBaseBulkStore<DB,T>` that does not override `UpdateCore` /
  `UpdateCoreAsync`, and a batch of three entities of which the second is not stored
- **When** `Update(items)` / `UpdateAsync(items, storeDelegate, ct)` is called
- **Then** the per-item update of the second throws `EntityNotFoundException`, which the loop catches, the third
  is still updated, and after the loop one `EntityNotFoundException` naming the second's `Guid` is thrown —
  before TASK-555 the miss wrote nothing and reported success
- **And** outside a transaction boundary that report is the one exception that **commits** the batch's own
  transaction before propagating, so the stored items stay written

#### Scenario: A SQL bulk collection write that fails part-way leaves nothing written

- **Given** a SQL bulk store outside any transaction boundary, and `Create(items)` / `Delete(items)` /
  `Update(items)` (or an async twin) whose item k fails with a database error, or whose token is cancelled
- **When** the call runs
- **Then** the batch's own transaction is rolled back — on the async side with `CancellationToken.None`, so a
  cancelled batch is still rolled back — and items 0..k-1 are not left written; before TASK-629 (SH-M028)
  each item committed on its own

#### Scenario: A SQL bulk collection write inside a boundary joins it

- **Given** a SQL bulk store with a `TransactionContext` set, or called inside an ambient SQL transaction for
  the same database
- **When** a collection `Create` / `Update` / `Delete` is called
- **Then** no transaction of its own is opened: the write runs inside the caller's boundary, which decides
  whether it commits

#### Scenario: A duplicate in a collection create is the shared refusal

- **Given** a bulk store and a collection containing an entity whose key is already stored
- **When** `Create(items)` / `CreateAsync(items, …)` is called
- **Then** a failure the provider recognises as a duplicate key (`IsDuplicateKeyException`; on SQL
  `DuplicateCreate.IsDuplicate(Connector, ex)`, around the seam so a native bulk insert is covered too) is
  rethrown as the shared duplicate-create refusal, `StoreException.DuplicateCreate` — a
  `StoreUniqueViolationException`; a collection `Update` colliding with a unique index is rethrown as
  `StoreException.UniqueViolation`, the same exception type, and on the update paths (and the abstract bases'
  create) an exception that already is one passes through unchanged

#### Scenario: The action-based filter update initialises indirectly

- **Given** an uninitialised bulk store
- **When** `Update(filter, Action<T>)` is called
- **Then** initialisation happens inside the nested `Read(filter, …)` call — the method itself contains
  no `EnsureInitialized()`

### Requirement: Async bulk reads select their execution path from the available connector

The system SHALL, in `AsyncDataBaseBulkStore<DB,T>.ReadCoreAsync`, return an empty sequence when
`Connector` is null, stream through `AsyncConnector.SelectAsync` when an async connector is present,
and otherwise wrap the synchronous `Connector.Select` in `Task.Run` with the cancellation token, enumerating
the result into a list **inside** that task (TASK-170, SH-M024) — so the query runs off the caller's thread,
which it did not while only the iterator's construction was offloaded. Reaching `ReadCoreAsync` through
`ReadAsync` requires a connector, because lazy initialisation refuses a store without one.

#### Scenario: Async connector results are streamed and type-filtered

- **Given** an `AsyncDataBaseBulkStore<DB,T>` with a non-null `AsyncConnector`
- **When** `ReadAsync(filter, orderBy, limit, offset, ct)` is called
- **Then** `AsyncConnector.SelectAsync(typeof(T), filter as LambdaExpression, orderBy?.ToDictionary(),
  limit, offset, ct)` is enumerated with `await foreach` and only items that are `T` are collected

#### Scenario: Sync-only connector is wrapped in Task.Run

- **Given** an `AsyncDataBaseBulkStore<DB,T>` with a non-null `Connector` and a null `AsyncConnector`
- **When** `ReadAsync(filter, …, ct)` is called
- **Then** `Task.Run(() => Connector!.Select(…)?.OfType<T>().ToList(), ct)` is awaited, a null result maps to
  `Enumerable.Empty<T>()`, and a non-null result is the materialised list — like the `AsyncConnector` branch,
  every row is read before the method returns, inside the task, so a query failure surfaces from the awaited
  call rather than later on the caller's thread. No shipped SQL connector reaches this branch: all four are
  `AbstractAsyncConnector`

#### Scenario: No connector at all is refused at lazy init

- **Given** an `AsyncDataBaseBulkStore<DB,T>` whose `Connector` is null
- **When** `ReadAsync(filter, …, ct)` is called
- **Then** `EnsureInitializedAsync(ct)` throws `InvalidOperationException` from `InitCoreAsync` ("… has no
  database connector …") before `ReadCoreAsync` runs — before TASK-170 (SH-M031) the read answered
  `Enumerable.Empty<T>()`. `ReadCoreAsync`'s own null-connector return of an empty sequence remains for a
  direct call

#### Scenario: The parameterless async read reads everything

- **Given** an `AsyncDataBaseBulkStore<DB,T>` or `AbstractAsyncBulkStore<T>`
- **When** `ReadAsync(ct)` is called
- **Then** it delegates to `ReadAsync(null, null, null, null, ct)`

### Requirement: Aggregation on SQL bulk stores

The system SHALL expose `Aggregate(AggregateQuery<T>)` on `DataBaseBulkStore<DB,T>` and
`AggregateAsync(AggregateQuery<T>, ct)` on `AsyncDataBaseBulkStore<DB,T>`, each initialising first — which
refuses a store with no `Connector` (TASK-170) — and materialising every row produced by the connector's
aggregate select, the async one falling back to the synchronous aggregate when there is no async connector.

#### Scenario: Sync aggregation needs only the synchronous connector

- **Given** a `DataBaseBulkStore<DB,T>` with a non-null `Connector`
- **When** `Aggregate(query)` is called
- **Then** `EnsureInitialized()` runs and every row of `Connector.SelectAggregate(typeof(T), query)` is
  collected into a read-only `AggregateResult` list

#### Scenario: Async aggregation falls back to the sync connector

- **Given** an `AsyncDataBaseBulkStore<DB,T>` with a non-null `Connector` and a null `AsyncConnector`
- **When** `AggregateAsync(query, ct)` is called
- **Then** it awaits `Task.Run(() => Connector.SelectAggregate(typeof(T), query).ToList().AsReadOnly(), ct)`
  and returns those rows — the same fallback the read/update/delete paths use. Before TASK-170 (SH-M034) it
  returned `Array.Empty<AggregateResult>()`, answering no groups at all

#### Scenario: Aggregation with no connector is refused

- **Given** a `DataBaseBulkStore<DB,T>` / `AsyncDataBaseBulkStore<DB,T>` whose `Connector` is null
- **When** `Aggregate(query)` / `AggregateAsync(query, ct)` is called
- **Then** lazy initialisation throws `InvalidOperationException` ("… has no database connector …"); the
  methods' own empty-result return for a null connector is no longer reached

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
- **And** such an override carries the filter-guard obligation with it — either by delegating to `base` or
  by calling `RequireFilter` and `RequireBoundedFilter` itself, since these three are the public methods
  and the guards live in them rather than in a `*Core`

### Requirement: A filtered PropertyUpdate Set stores what an entity write stores

On a SQL bulk store, `UpdateAsync(filter, PropertyUpdate)` / `Update(filter, PropertyUpdate)` SHALL bind each Set's
value through the target field's `ToStoredValue`, the same `ToStorage` conversion an entity write goes through. It
SHALL NOT bind the raw value. (TASK-596: a `[UtcField]` Set bound the raw `DateTime`, so the stored text lost its
offset and an equality filter on the column missed the row.) An Increment / Decrement keeps going through the
connector's `IncrementExpression`.

#### Scenario: A UTC column set by PropertyUpdate is stored with its offset

- **Given** `[UtcField] public DateTime At` on SQLite, and one row created with `At = 2026-10-14 09:30:15.123Z`
- **And** a second row whose `At` is written by `UpdateAsync(r => r.Guid == id, new PropertyUpdate<T>().Set(r => r.At, sameInstant))`
- **Then** both rows store `2026-10-14 09:30:15.123+00:00`, with the same storage class

#### Scenario: Fields whose stored form is their CLR value are unaffected

- **Given** a `TimeOnly`, an enum, a `decimal` and a plain `DateTime` set both ways
- **Then** each column stores the same value and storage class either way, as it did before

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
