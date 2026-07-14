---
id: STORY-027
parent: EPIC-014
status: in-progress
created: 2026-06-18
source: CODE-REVIEW-AUDIT-2026-06-17.md
severity: low
finding-count: 418
finding-ids: CR-L001 …
---

# Low findings

## Progress

**202 / 418 triaged** as of 2026-07-14. Next open is CR-L203 (Birko.Data.Stores).

**Batch AB — Data.SQL.Views cluster (CR-L201, CR-L202):** Birko.Data.SQL.Views. Both closed;
**/code-review clean**. **L201** (bug): `SqlViewTranslator.Translate` replaces the nine silent `continue`s
(on a failed table/field/view-property/join lookup) with descriptive `InvalidOperationException`s that name
the unresolved `SourceType.SourceProperty` — a view referencing an unmapped table or a misspelled/unmapped
property now fails loudly at translation time instead of silently producing a structurally-wrong SQL view
that's very hard to diagnose. **L202** (cleanup): removed the unused `using System.Reflection;` from
`SqlViewStore.cs`. **Tests:** SQL.Views.Tests 17 → 18 (a view over an unregistered source type throws
`InvalidOperationException` naming it; well-formed views still translate — the grouped-aggregate/COUNT paths
stay green). Suite green: SQL.Views.Tests 18.

**Batch AA — SQL.View.Migrations + SQL.ViewModel (CR-L198; CR-L199, CR-L200):** All closed;
**/code-review clean**. **L198** (convention, docs): rewrote the `ViewSqlGenerator` docs in
Birko.Data.SQL.View.Migrations README + CLAUDE to state the real API — a single `char quoteChar` applied
**symmetrically** (default `"`), supporting ANSI/PostgreSQL + MySQL only; **SQL Server bracket quoting
`[ ]` and T-SQL `CREATE OR ALTER VIEW` are out of scope** (use Birko.Data.SQL.MSSql.View) — the docs had
claimed a `(open, close)` tuple and broad provider support that the single-char API can't deliver.
**L199** (convention): `AsyncDataBaseRepository` gains `AddOnInit`/`RemoveOnInit` (delegating to the
unwrapping `DataBaseStore`) for parity with the sync `DataBaseRepository`; the `Connector` accessor was
already restored under CR-C17 (verify-first). **L200** (cleanup): deleted the ~20-line commented-out
`ReadView<TView>` block in the sync `DataBaseRepository` (referenced a removed `GetConnector()`/`SelectView`
API). **Tests:** SQL.ViewModel.Tests 10 → 11 (async `AddOnInit`/`RemoveOnInit` fire/unfire via
`Connector.DoInit()` over a real SQLite store). Suite green: SQL.ViewModel.Tests 11.

**Batch Z — Data.SQL.View cluster (CR-L195, CR-L196, CR-L197):** Birko.Data.SQL.View. All closed;
**/code-review clean**. **L195** (bug): aggregate view columns are now aliased by the **unique view-property
name** (`field.Property.Name`) in both `ViewSelectSqlBuilder` (the `AS` alias) and
`GetPersistentViewSelectFields` (the queried column), instead of the aggregate **function name**
(`FunctionField.Name` = "COUNT"/"SUM") — two aggregates of the same function collided on a duplicate column
name in a persistent view's DDL, and the two sides must name the identical column so the persistent query
resolves. **L196** (bug): `BuildViewJoinConditionSql` routes a constant join value through a new
`FormatJoinConditionValue` — numerics emit unquoted via **InvariantCulture** (a comma-decimal locale no
longer corrupts the SQL), bools emit `TRUE`/`FALSE`, and strings keep the single-quoted + doubled-quote
escaping (was: everything quoted as a culture-dependent string). **L197** (nullable): `DataBase.ReadView`
guards a null `LoadView` result with a clear `TableAttributeException` instead of deferring an NRE into the
base `Read` via a null-forgiving `!`. **Tests:** SQL.Tests 294 → 299 (aggregate aliases use property names +
match `GetPersistentViewSelectFields`; `FormatJoinConditionValue` string/int/bool/decimal-under-de-DE-locale
matrix). Updated the MSSql.View schema-binding assertions to the new property-name aliases
(`AS [OrderCount]`/`AS [TotalSpent]`). All SQL.View consumers green: SQL.Tests 299, MSSql.View 19,
SqLite.View 9, Views 17, View.Migrations 11, ViewModel 10.

**Batch Y — SqLite cluster (CR-L192; CR-L193, CR-L194):** Birko.Data.SQL.SqLite + .SqLite.View.
All closed; **/code-review clean**. **L192** (bug): `SqLiteConnector_OnException` detects the missing-table
case via the typed `SqliteException` + `SqliteErrorCode == 1` (SQLITE_ERROR) and a case-insensitive
`"no such table"` match, instead of the brittle locale/version-dependent `"SQLite Error"` prefix substring
(which would break on a Microsoft.Data.Sqlite upgrade or non-English locale). **L193** (other): documented on
`BuildCreateViewSql` that `CreateView`/`CreateViewAsync` is a no-op on SQLite when the view already exists
(`CREATE VIEW IF NOT EXISTS` doesn't update an outdated body, unlike the base `CREATE OR REPLACE VIEW`) — use
`RecreateView` to replace. **L194** (test-gap): new **Birko.Data.SQL.SqLite.View.Tests** project
(git-init'd + registered) — `ViewExists`/`ViewExistsAsync` null/empty/whitespace guards, the
`CREATE VIEW IF NOT EXISTS` DDL string (via `BuildCreateViewSql`), and a **real** round-trip against an
on-disk SQLite db (a seeded view → `ViewExists` true, a missing name → false, a same-named TABLE → false via
the `type='view'` filter; async parity). **Tests:** SqLite.Tests 26 (L192 build-verified, no regression),
SqLite.View.Tests 9 (new). Suites green: SqLite.Tests 26, SqLite.View.Tests 9.

**Batch X — PostgreSQL cluster (CR-L188 … CR-L190; CR-L191):** Birko.Data.SQL.PostgreSQL + .PostgreSQL.View.
All closed; **/code-review clean**. **L188** (cleanup): `IsTransientException`'s
`ex is NpgsqlException npgsqlEx && npgsqlEx is PostgresException pgEx` collapsed to `ex is PostgresException
pgEx` (PostgresException derives from NpgsqlException; the outer binding was unused). **L189** (other):
`PostgreSqlSettings.GetConnectionString` composes via `NpgsqlConnectionStringBuilder` so
Host/UserName/Password/Database values containing `;`/`=`/`'` are quoted/escaped correctly instead of
breaking the key=value parsing or injecting keywords (the builder omits keys at their Npgsql default —
e.g. Port 5432 — documented). **L190** (convention): documented on both `CreateCore`/`CreateCoreAsync` that
bulk create intentionally assigns a fresh Guid to every row (discarding a caller-supplied Guid), matching the
MSSql sibling's bulk convention — changing one provider alone would diverge cross-provider; callers needing a
known id use the single-item path. **L191** (bug): `ViewExists`/`MaterializedViewExists`(+async) add
`AND table_schema = current_schema()` / `AND schemaname = current_schema()` so a same-named object in another
schema on the search path isn't a false positive (the bare single-part CREATE lands in the current schema).
**Tests:** PostgreSQL.Tests 17 → 18 (connection-string round-trip via NpgsqlConnectionStringBuilder incl. a
`;`/`=`/`'`-in-password escaping case; the old literal-Port assertion replaced — the builder omits the
default port). L188 is behavior-preserving (build-verified), L191 HasRows is a live-PG path (code-review
verified). Suites green: PostgreSQL.Tests 18, PostgreSQL.View.Tests 7.

**Batch W — MySQL.View cluster (CR-L186, CR-L187):** Birko.Data.SQL.MySQL.View. Both closed;
**/code-review clean**. **L186** (convention): added a public `ViewExistsAsync(string, CancellationToken)`
override mirroring the sync `ViewExists` — runs the same parameterized `information_schema.VIEWS` query
scoped to `DATABASE()` via `DoCommandAsync` (observing the token), instead of the base fallback's
`SELECT 1 FROM <view> WHERE 1=0` in a try/catch that swallows connection/permission errors as
"view does not exist" and isn't database-scoped; extracted the shared SQL into a `ViewExistsSql` const.
**L187** (test-gap): new **Birko.Data.SQL.MySQL.View.Tests** project (git-init'd + registered in
`.slnx`/`.code-workspace`) — the `ViewExists`/`ViewExistsAsync` null/empty/whitespace `ArgumentException`
guards (both overloads) + a structural assert that the async override is declared on `MySQLConnector` (so
async callers get the information_schema query, not the base probe). The catalog-query HasRows outcome needs
a live MySQL (integration-tier). Suite green: MySQL.View.Tests 7.

**Batch V — MySQL cluster (CR-L183, CR-L184, CR-L185):** Birko.Data.SQL.MySQL. All closed;
**/code-review clean** (dead-code + doc, no behavior change beyond removing dead code). **L183** (cleanup):
the `MySQLConnector_OnException` table-missing guard was `A || (B && A)` — because `&&` binds tighter than
`||`, the second operand could only be true when the first already was, so it was entirely dead; reduced to
`!IsInitializing && ex.Message.Contains("doesn't exist")`. **L184** (convention): the `IsTransientException`
XML doc listed only 1213/1205/2006/2013/1040 but the switch also returns true for 1317/2002/2003 — updated
the doc to match the actual case labels. **L185** (other): documented on the `#region Native Bulk Operations`
that a first-run table-missing failure rolls back and auto-inits (DoInit) but does **not** re-run the bulk
command (the payload is silently dropped) — inherited framework auto-init behavior; callers must ensure the
schema exists (InitAsync / a prior single-row write / CreateTable) before the first bulk op. No new tests
(nothing observable changed — the removed clause was dead). Suite green: MySQL.Tests 14.

**Batch U — MSSql cluster (CR-L179; CR-L180 … CR-L182):** Birko.Data.SQL.MSSql + .MSSql.View. All closed;
**/code-review clean**. **L179** (bug): the six native bulk methods (`BulkInsert`/`Update`/`Delete` sync+async)
opened the connection (and began the transaction) **outside** the `try`, so an `Open`/transient failure
bypassed `InitException`. Moved `Open`/`OpenAsync` (and `BeginTransaction(Async)`) inside the `try`; the
transaction-bearing methods now declare a nullable `SqlTransaction? transaction` outside, roll back via a
null-check in `catch`, and dispose it in a `finally` (replacing the former `using var transaction`). **L180**
(convention): `MSSqlConnector_View.GetSchemaName` (was a private hardcoded `"dbo"`) is now `protected virtual`
so a derived connector can target a non-dbo schema in the SCHEMABINDING two-part names. **L181** (bug): a new
`EnsureIndexedViewSupported` guard makes `CreateIndexedView`/`CreateIndexedViewAsync` throw a clear
`NotSupportedException` for aggregate (GROUP BY) views (SQL Server requires `COUNT_BIG(*)` in a SCHEMABINDING
aggregate view's select list, which the generic builder doesn't emit — the clustered-index step would fail
at runtime with an opaque error). **L182** (convention): rewrote the MSSql.View CLAUDE.md Components section
to document the full indexed-view API (Create/Drop/Exists ×sync+async, key-column ladder,
BuildSchemaBindingSelectSql, GetSchemaName) + the SCHEMABINDING / aggregate-view limitations. **Tests:**
MSSql.Tests 21 (L179 bulk paths are live-SQL-Server-only — build-verified/code-review), MSSql.View.Tests
15 → 19 (aggregate CreateIndexedView[Async] → NotSupportedException; non-aggregate passes the guard;
GetSchemaName override changes the two-part qualification). Suites green: MSSql 21, MSSql.View 19.

**Batch T — Data.SQL.Caching cluster (CR-L177, CR-L178):** Birko.Data.SQL.Caching. Both closed;
**/code-review clean** (comment/doc only, no logic change). **L177** (verify-first, "no action required"):
documented on `CachedAsyncDataBaseBulkStore.ResolveTableName` that construction-time table-name resolution
is intentional and correct — it depends only on T's mapping attributes (not connection/settings state) and
`LoadTable` is static/cached, so resolving it before `SetSettings`/`Init` is cheap and safe; it feeds only
the cache-key prefix. **L178** (cleanup): fixed the misleading `SqlCacheKeyBuilder.ComputeHash` comment
("Use first 12 bytes" → "8 bytes"); the loop already reads 8 bytes = 16 hex chars, matching the
`StringBuilder(16)` capacity, so only the comment was wrong. **Tests:** SQL.Caching.Tests 6 → 7 (assert the
BuildKey filter/order hash segments are exactly 16 hex chars, locking in the corrected comment). Suite green:
SQL.Caching.Tests 7.

**Batch S — Data.SQL core cluster (CR-L173 … CR-L176):** Birko.Data.SQL (+ provider overrides in
.SQL.PostgreSQL / .MSSql / .MySQL). All closed; **/code-review clean**. **L173** (bug, diagnostic):
`DataBase.GetGeneratedQuery` replaces parameter names longest-first (`OrderByDescending(name.Length)`) so a
name that's a prefix of another (`@WHEREName0_5` ⊂ `@WHEREName0_50`, `@LIMIT` ⊂ `@LIMITxxx`) can't corrupt
the rendered SQL — this string is the OnExecute/InitException diagnostic, not executed. **L174** (bug): the
bulk `Insert(tableName, IEnumerable<IDictionary>)` (sync + async) validates that every row shares the first
row's column set (`HashSet.SetEquals`) and throws `ArgumentException` otherwise, instead of silently
mis-binding heterogeneous dictionaries (a missing key left the prior row's stale value); rows are also
materialized once instead of being re-enumerated 4×. **L175** (convention): `SqlUnitOfWork.FromStore` passes
`connector.Settings` (the public property) into the normal ctor, deleting the dummy ctor + the reflection
helper that read the private `_settings` field by name. **L176** (convention/bug): promoted table-missing
detection to a virtual `AbstractConnectorBase.IsMissingTableException(Exception)` (base = SQLite's
"no such table"), routed the three reader catches through it, and added provider overrides — PostgreSQL
(`relation … does not exist` / SQLSTATE 42P01), MSSql (`Invalid object name` / error 208), MySQL
(`doesn't exist` / error 1146) — so a missing table yields an empty read on all backends, not a hard error.
**Tests:** SQL.Tests 289 → 294 (GetGeneratedQuery prefix-collision + string-quoting; base
IsMissingTableException matrix), SqLite.Tests 24 → 26 (heterogeneous bulk-insert throws; `FromStore`
Begin/Commit persists without reflection), MSSql/PostgreSQL/MySQL .Tests +3 each (provider
IsMissingTableException wording matrix). Suites green: SQL 294, SqLite 26, MSSql 21, PostgreSQL 17, MySQL 14.

**Batch R — Data.Repositories cluster (CR-L171, CR-L172):** Birko.Data.Repositories. Both closed;
**/code-review clean**. **L171** (convention): the bulk repository layer now surfaces `ReadFirst(filter)` /
`ReadFirstAsync(filter, ct)` for parity with the store contract (where the inherited bulk `Read(filter)`
returns the collection, not a single entity). Added to `IBulkReadRepository<T>` /
`IAsyncBulkReadRepository<T>` and implemented in `AbstractBulkRepository` / `AbstractAsyncBulkRepository`,
delegating to the store's `ReadFirst`/`ReadFirstAsync` and mirroring each sibling read's null-store behavior
(sync throws `InvalidOperationException` when the store isn't an `IBulkStore`, async returns null). Every
concrete repo inherits the base impl — no direct interface implementers, so nothing breaks. **L172** (other):
`RepositoryLocator.GetRepository<TRepository, TSettings>` constructs the repo parameterlessly and uses the
settings only as a cache key — it has no store/model type to build a configured store. A repo whose only
ctor takes a store previously threw a raw `MissingMethodException`; a new `CreateParameterless` helper now
converts that to a clear `InvalidOperationException` naming the parameterless-ctor requirement, and the XML
doc states the settings-only-for-key contract (use a store-injecting overload to apply settings). **Tests:**
Repositories.Tests 10 → 16 — `BulkRepositoryReadFirstTests` (sync/async ReadFirst over a real InMemory
store: match / no-match) + `RepositoryLocatorSettingsOverloadTests` (store-only repo → clear error;
parameterless repo created + cached by settings id). Suite green: Repositories.Tests 16.

**Batch Q — RavenDB cluster (CR-L164 … CR-L170):** Birko.Data.RavenDB (store + repos), .ViewModel,
.Views. All closed; **/code-review clean**. **L164** (bug): both `RavenDBStore`/`AsyncRavenDBStore` now
implement `IDisposable` and track a `_ownsStore` flag — the connection-string ctor + `Settings.CreateDocumentStore`
paths mark the store owned (disposed on `Dispose`), while an externally-supplied `IDocumentStore` (the
`IDocumentStore` ctor) is left untouched; a repeat `SetSettings` disposes the previously-owned store via a
shared `ReplaceDocumentStore` helper (was leaking the prior store). **L165/L166** (convention): added a sync
`IsHealthy()` to `RavenDBStore` mirroring the async store's real connectivity probe (an empty
`Query<T>().Take(0)`), and pointed the sync repos (`RavenDBRepository`/`RavenDBModelRepository` in the store
project + the `.ViewModel` sync repo) at it instead of `DatabaseExists()` — which returned true for an empty
database name without touching the server, so sync `IsHealthy` disagreed with async. **L167** (other):
documented that the ViewModel `SetSettings(RemoteSettings)` is a delegation no-op only when the store fails
to unwrap (never for the constructor-guaranteed backing store). **L168** (bug): `RavenViewTranslator` emits
`group result by 1 into g` (global aggregate) for an aggregate-only view with no GroupBy/Fields, instead of
the invalid `group result by new {  } into g` an empty composite key produced (RavenDB rejected it at
index-put with an opaque error). **L169** (cleanup): removed the dead `firstJoin`/`rightTypeName` locals in
the join-map build (the per-join loop recomputes the name). **L170** (other): `RavenViewStore.QueryAsync`
replaces the bare `(IRavenQueryable<TView>)sorted` hard-cast with a guarded `is not` pattern that throws a
descriptive `InvalidOperationException`, so a future non-Raven `IQueryable` in the pipeline fails clearly
rather than with an opaque `InvalidCastException`. **Tests:** RavenDB.Tests 30 → 35 (`RavenDBStoreDisposalTests`
— owned-store disposed, external-store untouched, idempotent, sync + async; offline since `DocumentStore.Initialize`
doesn't connect), Views.Tests 4 → 5 (aggregate-only view groups by the constant `1`). L165/L166 health probes
are live-server paths (code-review verified). Suites green: RavenDB.Tests 35, ViewModel.Tests 4, Views.Tests 5.

**Batch P — Data.Processors cluster (CR-L160 … CR-L163):** Birko.Data.Processors. All closed;
**/code-review clean**. **L160** (cleanup): extracted a shared `AbstractDecoratorProcessor<TProcessor, TModel>
: AbstractProcessor<TModel>` that holds `_inner` + the public `Inner` accessor and wires the whole event
pipeline once; `HttpProcessor`/`ZipProcessor` now derive from it, deleting the byte-identical
`WireInnerEvents` copies (registered the new file in `.projitems`). **L161** (cleanup): `ProcessorParseException`
no longer fabricates a synthetic `new Exception(message)` inner exception when none is supplied — it passes
the nullable `innerException` straight through (the base `ProcessorException(string, Exception?)` ctor was
relaxed to accept null), so a parse exception without a cause has a clean null `InnerException` instead of a
misleading duplicate-message "caused by". **L162** (test-gap, verify-first): already covered — the nested
subfolder entry by CR-M127's `ProcessStreamAsync_NestedFolderEntry_Extracts` and the `../` Zip Slip path by
the dedicated `ZipProcessorZipSlipTests` (CR-H076). **L163** (test-gap): added the HTTP download→temp-file→
inner.ProcessStream(Async)→cleanup happy path (sync + async) via a stub `HttpMessageHandler` returning CSV,
asserting items are produced and the temp file is deleted. **Tests:** Processors.Tests 36 → 40
(HttpProcessor happy path ×2, ProcessorParseException null/preserved inner ×2). Suite green: Processors.Tests 40.

**Batch O — Data.Patterns cluster (CR-L158, CR-L159):** Birko.Data.Patterns. Both closed;
**/code-review clean**. **L158** (`RuleSpecification`): removed the dead `memberAsObject` local, and hardened
the compiled-expression (`ToExpression`) path against runtime throws — `BuildStringMethod` now guards a
null string member (`x.Name != null && x.Name.Contains(...)`, so an in-memory compiled delegate no longer
NREs on a null property) and returns an unsatisfiable leaf for a non-string member; `BuildComparison`/
`BuildBetween` route the value through a new `TryConvertConstant` helper that degrades to
`Expression.Constant(false)` when the value is null against a non-nullable value type or is
non-convertible (was `Convert.ChangeType` → `InvalidCastException`/`FormatException`/`ArgumentException`),
and accepts an already-correctly-typed value directly (covers enums, which `Convert.ChangeType` can't
target). **L159** (`AsyncPagedRepositoryWrapper.ReadPagedAsync`): awaits the page-read then the count
**sequentially** instead of starting both on the same `_repository` and `Task.WhenAll`-ing — a
connection-bound backend can't service two in-flight calls on one instance (matches the already-sequential
sync `PagedRepositoryWrapper`). **Tests:** Patterns.Tests 22 → 32 — `RuleSpecificationExpressionTests`
(compiled Contains/NotContains over a null string no-throw, non-convertible + null-vs-non-nullable →
unsatisfiable, convertible values still match, Between with valid bounds, enum value already-typed) +
`AsyncPagedRepositoryWrapperTests` (an instrumented probe repository asserts max observed concurrency == 1,
result assembly, null-ctor guard). Suite green: Patterns.Tests 32.

**Batch N — MongoDB cluster (CR-L153 … CR-L157):** Birko.Data.MongoDB (store), .MongoDB.ViewModel,
.MongoDB.Views. All closed; **/code-review clean (no findings)**. **Nullable:** L153
(`Settings.ReplicaSet` is now `string?` — the `= null!` suppression lied about the type; it is genuinely
optional and only ever read via `IsNullOrEmpty`). **Cleanup:** L154 (dropped the no-op
`(Expression<Func<T, bool>>)filter` self-cast in `MongoDBStore.Update`/`AsyncMongoDBStore.UpdateAsync` — the
parameter already has that type; the driver's implicit `FilterDefinition<T>` conversion is unaffected),
L155/L156 (removed the `DestroyAsync`/`Destroy` overrides on both ViewModel repos — the base already destroys
the store via `BulkStore`/`Store`, so the override's extra `DropAsync`/`Drop` dropped the collection a second
time, and that second call bypassed any wrapper by hitting the unwrapped store; `DropAsync`/`Drop` remain as
explicit collection-drop helpers), L157 (`MongoViewTranslator` materializes the group-by projection once and
reuses it for both the `_id` composite and the `$first`-carried-forward fields, instead of building two
identical `Select` enumerables). **Tests:** MongoDB.Tests 42 → 44 (`ReplicaSet` default-null + omitted from
the connection string, `LoadFrom` round-trips a null), ViewModel.Tests 4 → 6 (structural: the repos no
longer re-declare `Destroy`/`DestroyAsync` while `Drop`/`DropAsync` stay), Views.Tests 6 → 7 (the `$group`
stage carries the key in both `_id` and `$first`). Suites green: MongoDB 44, ViewModel 6, Views 7.

**Batch M — Migrations backend cluster (CR-L141 … CR-L152):** CosmosDB, ElasticSearch, InfluxDB, MongoDB,
RavenDB, SQL migration stores. **Bugs fixed:** L143 (ES `ElasticSearchDataMigrator` range operators
`$gt/$gte/$lt/$lte` validate the value via a new internal `ToRangeBound` — `Convert.ToDouble(null)`
silently returned 0, so `{"x":{"$gt":null}}` became a range > 0; now throws `ArgumentException`), L144 (ES
`CopyData` throws `NotSupportedException` when a `transformJson` is supplied instead of silently dropping it
— the server-side reindex applies no transform), L150 (SQL `SqlDataMigrator` routes identifier quoting
through the connector dialect via a `QuoteIdentifier` helper + a quoter threaded into `ParseFilterToWhere`,
instead of hardcoded ANSI double quotes that break on SQL Server `[brackets]`; `SqlMigrationContext` now
passes the connector), L152 (SQL `SqlSchemaBuilder.CollectionExists` picks `sqlite_master` vs
`INFORMATION_SCHEMA` from the connection provider — the unconditional INFORMATION_SCHEMA query threw on
SQLite). **Convention:** L142 (ES migrations-index create honors the configured `NumberOfShards`/
`NumberOfReplicas` — were hardcoded 1/0; `UseAliases` documented as reserved), L151 (SQL RemoteSettings
ctor copies the whole chain via `SqlMigrationSettings.LoadFrom(remoteSettings)` — the manual field-copy
dropped `UseSecure`). **Cleanup:** L141 (Cosmos) + L149 (Raven) replace the misleadingly-named
`_cachedState` (only ever a null-check sentinel; every read re-fetches) with a `bool _initialized` flag,
L147 (InfluxDB extracts the duplicated `if (_migrationsBucket == null) Initialize();` into one
`EnsureInitialized()`), L148 (MongoDB drops the unused `IMongoClient _client` field + ctor param;
`MongoMigrationRunner` updated). **Docs/verify-first:** L145 (InfluxDB `*Async` observe the token at entry;
genuine SDK-async threading is the deferred CR-M108 work), L146 (InfluxDB broad `catch{}` narrowed to
`InfluxException` so non-Influx exceptions surface — precisely distinguishing no-data from auth needs the
live tier, deferred). **Tests:** SQL 24 → 29 (`ParseFilterToWhere` bracket-quoter, `CollectionExists` on
real SQLite, `SqlMigrationSettings.LoadFrom` copies UseSecure), ES 2 → 8 (`ToRangeBound` null/non-numeric/
numeric matrix + `CopyData` transform-throws). **/code-review: 2 PLAUSIBLE** (L148 Mongo ctor breaking
change — documented/kept; L146 InfluxDB narrowing still swallows auth within InfluxException — deferred).
Suites green: Migrations CosmosDB 11, ElasticSearch 8, InfluxDB 17, MongoDB 7, RavenDB 11, SQL 29.

**Batch L — Data.Localization cluster (CR-L135 … CR-L140):** all in Birko.Data.Localization.
**Bugs fixed:** L135 (the filter-based `Update(filter, PropertyUpdate)` / `UpdateAsync` overrides in both
bulk wrappers now detect — on a non-default culture — a PropertyUpdate that targets a **localizable**
field and fall back to the `Action<T>` read-modify-write path so a translation row is persisted, instead
of the native pass-through that mutated only the base column and wrote no translation; a new shared
internal `LocalizedPropertyUpdateHelper` does the detect + `ToAction` (reusing `PropertyUpdate.ApplyTo`)),
L138 (`ApplyInMemoryOrderBy` sorts through a defensive `SafeObjectComparer` — nulls-first, same-typed
`IComparable` direct, else stable ordinal-string fallback — so a sort on a non-`IComparable` property
degrades gracefully instead of throwing `InvalidOperationException` at sort time). **Provider-friendliness:**
L136 (`BuildGuidFilter` builds the membership test over `List<Guid>.Contains` instead of
`HashSet<Guid>.Contains` — more query providers translate it to SQL `IN`; documented best-effort + no
chunking for very large sets). **Docs/verify-first:** L137 (documented the localized-filter operator/null
semantics on `LocalizedExpressionAnalyzer` — `== null`/`!= null` are not localized (pass through to the
base column) and `!=` only matches entities that HAVE a translation row; full `!=` correctness deferred),
L139 (`EntityTranslationFilter.ByEntityType`/`ByEntityTypeAndCulture` are intended public API — covered by
`EntityTranslationFilterTests` + documented in CLAUDE.md — so kept, per the finding's own criterion).
**Tests:** Localization.Tests 70 → 79 — `LocalizedHelperTests` (ApplyInMemoryOrderBy multi-field ASC/DESC,
ApplyInMemoryPaging offset/limit, the L138 non-comparable-no-throw, BuildGuidFilter empty/non-empty,
CombineFilters param rebinding — the L140 gap) and `LocalizedPropertyUpdateFallbackTests` (the L135
localizable-field fallback writes a translation; non-localizable field / default culture keep the native
path). **/code-review: clean (no findings).**

**Batch K — Data.JSON cluster (CR-L129 … CR-L134):** JSON + JSON.ViewModel.
**Bugs fixed:** L129 (async bulk `UpdateCoreAsync` guards `ContainsKey` before writing + only saves when
something changed — was silently upserting a non-existent item, unlike single-item + sync-bulk Update),
L130 (`AsyncJsonBatchStore.SetSettings(Settings)` uses `is not BatchSettings` → clear `InvalidDataException`
instead of an opaque `InvalidCastException` from the old `(BatchSettings)` hard-cast, matching the sync
batch stores), L131 (`JsonStore.LoadData` + `JsonBatchStore` + `JsonBatchBulkStore` loaders guard
`item?.Guid.HasValue == true` before `_items.Add` — a record with a missing/null guid used to NRE via the
`Guid!.Value`; matches the async/separate loaders). **Convention:** L132 (`AsyncJsonStore.GetPath` guards
both `Location` and `Name`, aligning with the sync `JsonStore.GetPath` — behavior already converged via
`GetDirectory()`). **Cleanup:** L134 (removed the unused `using Birko.Configuration;` from both
JSON.ViewModel repo files — `using Birko.Data.Stores;` kept, needed for the extension methods). **New
project:** L133 (**Birko.Data.JSON.ViewModel.Tests** — sync/async repo ctor guard + JsonStore unwrap, 5
tests; git-init'd + registered in `.slnx`/`.code-workspace`). **Tests:** JSON.Tests +4 (async-bulk no-upsert,
batch SetSettings clear-throw + accept, LoadData null-guid skip via a hand-written camelCase file located
through the public `GetPath()`). **/code-review: clean (no findings).** Suites green: JSON 18,
JSON.ViewModel 5.

**Batch J — EventSourcing + InfluxDB + InfluxDB.ViewModel + InMemory (CR-L120 … CR-L128):**
**Fixes:** L122 (InfluxDB `MapRecordToModel` outer catch now rethrows `InvalidOperationException` with
the target type instead of silently `return default` — the per-property inner catch still skips a single
bad field, but a structural/constructor failure no longer vanishes via the bulk read's `Where(m != null)`
and mask round-trip bugs; sync + async), L123 (added a public `AsyncInfluxDBStore.Settings` accessor;
`InfluxDbUnitOfWork.FromStore` reads Bucket/Organization through it instead of **reflecting** the private
`_settings` field). **Cleanup:** L120 (removed the unused `using Birko.Configuration;` from all 5
EventSourcing `Stores/` files — `System.Linq.Expressions` is actually *used* by the 4 wrappers, so kept;
the finding's claim it was unused in the Extensions file was stale — that file never imported it), L125
(removed the unused `using Birko.Configuration;` from both InfluxDB.ViewModel repo files — the finding also
named `using Birko.Data.Stores;` but that's **required** for the `GetUnwrappedStore`/`IsStoreOfType`
extension methods, so kept). **Docs/verify-first:** L121 (documented InfluxDB `Settings` intentionally
extends `Configuration.Settings` directly, not RemoteSettings — token+org auth, no user/password/port),
L124 (documented the accepted bulk filter-override gap — InfluxDB has no native update-by-predicate;
native filter-Delete is feasible but live-only, deferred; base fallback correct), L126 (the double-destroy
this finding worried about is already fixed in both repos under CR-M096; shared-base extraction deferred —
parallel sync/async hierarchies), L127 (documented InMemory `SetSettings(ISettings)` cast-or-no-op is
intentional — the store never reads settings). **wontfix:** L128 (InMemory `_settings`/`SetSettings`
duplication — the finding itself says leave as-is; no shared base across the sync/async abstract stores,
~20 lines). **Tests:** InfluxDB.Tests +1 (`Settings` accessor + `FromStore`-without-reflection). L122 is a
live-cluster read path (code-review verified — same sanctioned surfacing as ES L114). **/code-review: 1
PLAUSIBLE** (L122 read-now-throws on a structurally-corrupt row — documented, kept). Suites green:
EventSourcing 5, InfluxDB 26, InfluxDB.ViewModel 5, InMemory 40.

**Batch I — Data.ElasticSearch cluster (CR-L111 … CR-L119):** ElasticSearch (store), .ViewModel, .Views.
**Fixes:** L111 (filter-based `Delete`/`Update` overrides in both stores now call `EnsureInitialized`/
`EnsureInitializedAsync` first — lazy-init + cancelled-token gate, matching every base CRUD method),
L112 (`StoreAggregationHelper` composite-bucket `TryGetValue(out string keyValue)` → `out string?` ×2,
clears the CS8600 risk), L113 (extracted a shared `ElasticSearchStoreHelper` — `ResolveIndexName`/
`SanitizeIndexName` + `BuildUpdateScript<T>` — so the sync/async stores stop copy-pasting the index-name
sanitization and the PropertyUpdate→Painless script builder), L114 (bulk per-item failures in an
otherwise-valid response now **throw** with the offending items instead of a swallowed `// TODO` — matches
the single-item paths + UnitOfWork; ES bulk is non-atomic so the successful items are already persisted,
documented inline + flagged by /code-review as PLAUSIBLE, intended), L118 (`ExecuteSimpleQueryAsync`
clamps Size via a new internal `ClampWindowSize` so `From + Size` stays within the ES default
`max_result_window` of 10000 — a non-zero offset with the default size used to exceed it and get rejected),
L119 (`SetPropertyValue` handles enum/Guid targets via a new internal `ConvertValue` — `Convert.ChangeType`
silently dropped every enum/Guid group-by/aggregate column). **API asymmetry:** L115 (async repo gains
`CountAsync(QueryContainer)`/`ClearCacheAsync`/`ReadAsync(SearchRequest)` delegating through the unwrapping
`ElasticSearchStore` property, mirroring the sync repo). **Cleanup:** L117 (removed unused
`using Birko.Configuration;` from both repo files). **Verify-first:** L116 (Birko.Data.ElasticSearch.
ViewModel.Tests already exists — M092 — augmented with async-repo ctor guard + unwrap + CountAsync-zero).
**Tests:** Views.Tests +12 (`ConvertValue` enum/Guid/int/fail matrix + `ClampWindowSize` boundaries),
ViewModel.Tests +3 (async repo). L111/L114 are live-cluster paths (code-review verified). **/code-review:
1 PLAUSIBLE (the sanctioned L114 partial-commit-then-throw semantics — documented, kept).** Suites green:
ES.Tests 78, ViewModel.Tests 6, Views.Tests 19.

**Batch H — Data.CosmosDB (CR-L103 … CR-L110):** **Fixes:** L103 (`IsHealthyAsync` observes the ct),
L106 (removed `AsyncCosmosDBRepository.DestroyAsync` double-destroy override — base is sufficient), L109
(`CosmosViewManager.DropAsync` idempotent on NotFound), L110 (`CosmosViewStore` takes the SQL path for
group-by-only views, not just aggregate views — was returning ungrouped docs via LINQ). **New project:**
L108 (**Birko.Data.CosmosDB.ViewModel.Tests** — repo ctor guard + unwrap, 5 tests). **Docs/verify:** L104
(bulk filter override = accepted base fallback), L105 (CRUD/UnitOfWork need the emulator — deferred), L107
(SetSettings(RemoteSettings) already accepts Cosmos Settings via upcast). **/code-review: clean** (verified
the group-by SQL builder handles zero-aggregate views). Suites green: CosmosDB 43, CosmosDB.Views 7,
CosmosDB.ViewModel 5.

**Batch G — core foundation (CR-L094 … CR-L102):** Configuration, Contracts, CQRS, Data.Aggregates,
Data.Core. **Fixes:** L094 (`PasswordSettings.Password` defaults to `string.Empty` not `null!` — no
consumer distinguishes null from empty), L095 (`Contracts.RetryPolicy.GetDelay` clamps `attemptNumber` to
≥1), L098 (`CQRS/Unit.cs` adds `using System.Threading.Tasks;` for self-containment). **Docs/verify:**
L096 (Contracts CLAUDE.md `IDefault.Default`→`IsDefault`), L097 (Mediator static-cache process-wide intent),
L100 (AggregateMapper.Expand emits insert/delete only), L101 (ICopyable nullability contract — full
alignment deferred, would cascade warnings across ~15 implementers), L102 (LogViewModel duplication
deferred — parallel hierarchies). **Tests:** CQRS pipeline exception-propagation + pre-cancelled-token,
Contracts clamp Theory, Configuration empty-password. **/code-review: clean (no findings).** Suites green:
Configuration 12, Contracts 13, CQRS 30.

**Sub-batch F2 (CR-L084 … CR-L093) — SOAP + SSE + WebSocket:** **L084** (SOAP Send* helpers close the
OutputStream in `finally`), **L085** (StreamReader honors `request.ContentEncoding`), **L086** (extracted a
shared `SoapXml` Escape/BuildFault helper — 3 duplicated copies → 1, byte-identical output), **L087**
(removed dead query-strip in GetServicePath), **L088** (token query-string splits on first `=`), **L089**
(`SseEvent.ToString` emits explicit LF, not AppendLine's CRLF), **L090** (deferred — SendLoop Channel
refactor is untestable-live cleanup; correctness fine), **L091** (removed unused usings in SseServer),
**L092** (verify-first — WebSocketPort.Write no-op catch already gone, CR-M073), **L093** (WebSocketServer
`BroadcastAsync` is best-effort — one failed client no longer aborts the whole broadcast). Suites green:
SOAP 7, SSE 19, WebSocket 33.

**Communication cluster summary (A–F2, CR-L041 … CR-L093, 53 findings):** 2 new test projects created
(Birko.Communication.Tests, Birko.Communication.OAuth.Providers.Tests), `IPort : IDisposable` added,
numerous bug fixes (query-string `=` handling ×3, OutputStream `finally` close ×2, NDEF big-endian, OAuth
poll order + refresh retry, Modbus early-exit, Network thread-join, best-effort broadcast), and a batch of
doc/verify-first closures.

**Sub-batch F1 (CR-L078 … CR-L083) — REST + REST.Server:** **L078** (verify-first — `_clients` already a
ConcurrentDictionary with GetOrAdd), **L079** (documented OnRequest/OnResponse handlers must not throw —
they run inline), **L080** (added static-cache tests; SendRequestAsync HTTP-path seam noted as residual),
**L081** (RestServer route match iterates once + reuses the parameters dict instead of running IsRouteMatch
twice), **L082** (query-string parser splits on the first `=` so token/JWT values containing `=` aren't
dropped), **L083** (response `OutputStream.Close()` moved into a `finally` in both SendResponseAsync and
SendServerErrorAsync so a mid-write client disconnect doesn't leave the connection half-open). Suites green:
REST 22, REST.Server 7.

**Sub-batch E (CR-L070 … CR-L077) — NFC + OAuth + OAuth.Providers:** **L070** (`SerialNfcTransport.TransceiveAsync`
wraps the blocking serial Write/Read in `Task.Run` + observes the token, mirroring ReadTagAsync), **L071**
(documented `NfcReaderPort.Write` blocks + drops the APDU response — use `TransceiveApduAsync`), **L072**
(documented `NfcReaderSettings` intentionally stays on `PortSettings`), **L073** (`NdefRecord.GetText` UTF-16
now defaults to big-endian per the NFC Forum spec, honoring a LE BOM — was UTF-16LE unconditionally),
**L074** (OAuth `GetTokenAsync` no longer re-issues an identical just-failed refresh under the RefreshToken
grant — rethrows + removed the dead switch arm), **L075** (device-code poll issues the first request
immediately, delaying only after authorization_pending/slow_down per RFC 8628), **L076** (GitHub
`CreateDeviceFlowClient` delegates to `CreateDeviceFlowSettings` — single source of truth), **L077** (new
**Birko.Communication.OAuth.Providers.Tests** project, git-init'd + registered). Suites green: NFC 121,
OAuth 54, OAuth.Providers 5.

**Sub-batch D2 (CR-L065 … CR-L069) — Modbus + Network:** **L065** (dropped the always-overwritten dead
`expectedMinResponse` parameter of `SendWriteRequest` + the four `8` call-site args), **L066** (documented
Modbus client as synchronous-by-design, no `CancellationToken` — inherent to the sync IPort contract),
**L067** (response-wait loop exits early on a complete error frame via `IsCompleteErrorResponse()` instead
of spinning the full timeout), **L068** (verify-first — exception-response + TCP tx-id mismatch already
covered by CR-H025/CR-M052; chunked-buffer MockPort residual noted), **L069** (TcpIp/Udp `ReadWorker`
capture local `_stream`/`_client` refs + `Close()` joins the read thread with a 500ms timeout — Udp closes
the client first to unblock its blocking `Receive`). Also added `MockPort.Dispose()` in Modbus.Tests
(ripple from the L043 `IPort : IDisposable` change). Suites green: Modbus 71, Network 25.

Communication cluster remaining: NFC (L070–L073), OAuth + OAuth.Providers (L074–L077), and the web
protocols REST/SOAP/SSE/WebSocket (L078 onward).

**Sub-batch D1 (CR-L059 … CR-L064) — Hardware + IR:** **L059** (verify-first — Serial Read/HasReadData/
RemoveReadData already guard size<0, "negative = all", CR-M049), **L060** (removed a no-op `catch(Exception){throw;}`
in `Serial.Open`), **L061** (rewrote Hardware CLAUDE.md to the real Serial/Infraport/LPT surface; README was
already accurate), **L062** (`InfraredPort.HandleReceivedTiming` tries RawProtocol last regardless of
registration order — RawProtocol matches any non-empty timing; + regression test), **L063** (documented
`SamsungAcProfile.Protocol` as informational — transmit AC via `GetTiming()`), **L064** (documented
`IrTiming.TotalDurationUs` as single-pass, excluding repeats). Suites green: Hardware 23, IR 103.

**Sub-batch C (CR-L053 … CR-L058) — gRPC + gRPC.Server:** **L053** (Credentials doc corrected to
attribute scheme-based inference to `GrpcChannel.ForAddress`, not the pool), **L054/L055** (`DeadlineSeconds`
+ `ExtraMetadata` documented as reserved/not-auto-applied — kept, removing is breaking), **L056**
(verify-first: the client interceptor overrides only unary calls — no streaming overrides exist to test;
the settings-based CreateClient path is integration-tier), **L057** (added the three streaming server-handler
auth-gate tests — Client/Server/Duplex — gRPC.Server.Tests 5→11), **L058** (verify-first: `EnableReflection`
is a host-honored intent signal, already round-tripped by GrpcServerSettingsTests). Suites green: gRPC 25,
gRPC.Server 11.

**Batch 4 — Communication cluster (in progress).** Worked in sub-batches by project.
- **Sub-batch B (CR-L048 … CR-L052) — Camera + GraphQL:** **L048** (`FfmpegCameraSettings.JpegQuality`
  clamped to [1,31] in the setter, so an out-of-range value can't silently fail the ffmpeg capture),
  **L049** (verify-first — settings/frame tests exist; happy path needs ffmpeg; added a JpegQuality clamp
  Theory), **L050** (GraphQL subscription frames — connection_init/pong/subscribe — now use the shared
  `_serializer` so variables are camelCased consistently with the HTTP path, instead of raw default-options
  `System.Text.Json`), **L051** (`SchemaPath`/`SubscriptionProtocol`/`EnableAutoPersistedQueries` documented
  as reserved/not-yet-implemented — kept, since removing them is breaking), **L052** (dropped an unused
  `System.Text.Json` using). Suites green: Camera 21, GraphQL 58.
- **Sub-batch A (CR-L041 … CR-L047) — core + Bluetooth:** **L041** (`PortSettings.GetID` typo
  `AbstratPort`→`AbstractPort`), **L042** (`AbstractPort.InvokeProcessData` public→protected — fired
  internally by derived ports, not part of the IPort contract), **L043** (`IPort : IDisposable` +
  `AbstractPort.Dispose()`→`Close()` with a `_disposed` guard; the 3 ports that already had `Dispose`
  — Serial/NFC/BluetoothLE — became `override`), **L044** (new **Birko.Communication.Tests** project,
  git-initialized + registered in `.slnx`/`.code-workspace`, 6 hardware-free tests via an in-memory
  `AbstractPort` subclass). Bluetooth (platform-gated): **L046** (read worker surfaces faults via a new
  `ReadError` event instead of a bare `catch{break;}`), **L045** (WinRT discovery keys by `args.Id`
  instead of the not-always-present address property — code-review-only), **L047** (Linux P/Invoke uses
  `Marshal.SizeOf<SockaddrL2>()` + `[StructLayout(Sequential)]` — compile-checked with `DefineConstants=LINUX`).
  Suites green: Communication.Tests 6; Bluetooth/Hardware/NFC test projects build clean.

**Batch 3 (2026-07-14) — Caching cluster (CR-L034 … CR-L040, 7 findings):** Birko.Caching + .Hybrid + .Redis.

**Batch 3 (2026-07-14) — Caching cluster (CR-L034 … CR-L040, 7 findings):** Birko.Caching + .Hybrid + .Redis.
**Bugs fixed:** L036 (`MemoryCache.GetAsync` degrades a type-mismatch to a Miss via `is T` instead of the
unchecked `(T)Value!` cast that threw `InvalidCastException`; a stored null is still a hit), L040
(`RedisCache.RemoveByPrefixAsync` batches deletes via the `KeyDeleteAsync(RedisKey[])` array overload
instead of one round-trip per key — ct was already checked under CR-M034). **Convention:** L034 (the six
`MemoryCache` async CRUD methods now observe the `CancellationToken`). **Hardening:** L035 (largely resolved
by CR-M030 — `EvictExpired` no longer sweeps `_locks`; added a `volatile _disposed` flag + guard as belt-and-
suspenders). **Docs:** L038 (documented the intentional L2-hit L1-population staleness cap that differs from
GetOrSet). **Test-gaps:** L037 (added sliding-expiration + CacheSerializer round-trip tests; stampede + L1Max
already covered; NeverRemove-in-EvictExpired left uncovered — not observable without reflection), L039
(`GetL1Options` made `internal` + a full matrix test: null / absolute-below/above-max / sliding-only / null-max).
Suites green: Caching 40, Hybrid 39, Redis 12.

**Batch 2 (2026-07-14) — BackgroundJobs cluster (CR-L014 … CR-L033, 20 findings):** core + 8 backends.

**Batch 2 (2026-07-14) — BackgroundJobs cluster (CR-L014 … CR-L033, 20 findings):** core + 8 backends.
Verify-first. **Bugs fixed:** L014 (`RetryPolicy.GetDelay` overflow → compute in double, saturate at
MaxDelay), L019 (`JobExecutor` typed path returned `JobResult.Failed` when the matched `ExecuteAsync`
yields a null Task instead of masquerading as Succeeded), L025/L029 (JSON + RavenDB `FailAsync` fall back
to `RetryPolicy.MaxRetries` when the job's own MaxRetries is 0, mirroring `InMemoryJobQueue`), L021 (Cosmos
dequeue `ScheduledAt != null` guard), L020 (Cosmos FIFO tiebreaker `ThenBy(EnqueuedAt)`). **Convention/
cleanup:** L017 (`InMemoryJobQueue.EnqueueAsync` stamps `EnqueuedAt` from the injected clock), L022 (Cosmos
`FailAsync` signature matches the interface's non-nullable error), L023 (removed dead ES `IndexName` const),
L027 (removed dead Mongo `CollectionName` prop), L031 (Redis lock-release Lua extracted to one const +
shared sync/async helpers), L015 (documented `JobStatus.Failed` as reserved/unused), L024/L030 (corrected
stale "called automatically" schema doc-comments), L026/L033 (documented the intentional JSON-metadata
serializer + XML `Delay`-resolved-by-pipeline behavior). **Comment-only + deferred:** L016 (fixed the
misleading "re-enqueue" comment; a true no-retry requeue needs a dedicated `IJobQueue.RequeueAsync` — every
backend `EnqueueAsync` is an insert, so re-enqueuing the same id would PK-conflict). **Verify-first (already
resolved / not-a-defect):** L018 (helper is used by backend models — finding scoped to a core-only checkout),
L028 (`Birko.BackgroundJobs.MongoDB.Tests` already exists, CR-M022), L032 (SqlJobLockProvider already
dispatches per dialect, CR-M027). Suites green: core 77, JSON 7, RavenDB 7, Cosmos 3, ES 6, Mongo 6,
Redis 7, XML 7.

**Batch 1 (2026-07-14) — Birko.AI cluster (CR-L001 … CR-L013):**
`Birko.AI` / `.Agents` / `.Contracts` / `.Providers` / `.Resilience`. Verify-first (unverified reviewer
claims). Closed: **L001** (snapshot conversation before the sync fallback so a partially-mutated
streaming turn isn't resubmitted), **L002** (dead `Done ? conversation : conversation` ternary +
`HandleResponse`/`HandleToolUse` return type simplified from the unused `(Done,Continue)?` tuple to
`bool?`), **L003 partial** (removed the redundant all-errors reflection in Agent.cs via an `errorCount`;
the cross-provider `ToolResult` record was deliberately not introduced — the anonymous shape is JSON-
serialized to the wire with exact snake_case keys, so a record risks breaking every provider),
**L004/L009** (double-checked-lock `RegisterAll` in `AgentRegistration`/`ProviderRegistration`),
**L005** (`AgentOptions.FromDictionary` uses `TryParse` so a malformed config value is skipped, not a
`FormatException`), **L006** (`LlmProviderFactory` → `ConcurrentDictionary`), **L007** (new
`LlmProviderFactoryTests` + `FromDictionary` tolerance tests in the existing `Birko.AI.Tests`),
**L008 verify-first** (already resolved by CR-M003 — `LlmStreamingResponse` is `IDisposable`/
`IAsyncDisposable`), **L010** (stale default Claude model `claude-3-5-sonnet-latest` → `claude-sonnet-4-6`),
**L011** (Claude/Gemini tool-arg parsing deserializes the whole input object into `Dictionary<string,object>`
so nested JSON structure survives round-tripping, instead of per-property `ToString()`), **L012**
(`CheckBudgetAsync` short-circuits when `_config.Enabled` is false), **L013** (`ProviderRateLimiter`
last-wins dictionary build, no throw on case-duplicate providers). Suites green: `Birko.AI.Tests` 20,
`.Resilience.Tests` 13, `.Providers.Tests` 17, `.Agents.Tests` 18. Statuses flipped in the audit.

## User story

As a maintainer, I want the **low**-severity code-review findings triaged so genuine nits get
cleaned up opportunistically without derailing higher-priority work.

## Scope

The 418 low findings `CR-L001 …` from
[`CODE-REVIEW-AUDIT-2026-06-17.md`](../../../CODE-REVIEW-AUDIT-2026-06-17.md). **Unverified** —
reviewer claims, not adversarially re-checked; many are stylistic. Confirm value before fixing.

## Tasks

**Not pre-created.** Extract tasks from `CODE-REVIEW-AUDIT-2026-06-17.md` on demand — one task per
`CR-Lxxx` entry (verify-first), copying its ID/Title → title, Path → file:line, Detail → context,
Fix → approach, Acceptance → derive + add a regression test. Flip each finding's `Status` in the
audit (`done` / `wontfix`) as it's triaged.
