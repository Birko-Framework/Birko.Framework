# Birko Framework

Modular .NET framework with data access, communication, AI, and model infrastructure. General-purpose across enterprise back-office, e-commerce, presentation/CMS, desktop, IoT, and real-time domains.

See also:
- [CLAUDE-projects.md](CLAUDE-projects.md) — Full project catalog
- [CLAUDE-maintenance.md](CLAUDE-maintenance.md) — Maintenance guidelines, new project checklist, solution registration
- [CHANGELOG.md](CHANGELOG.md) — Historical architectural changes
- [README.md](README.md) + [docs/](docs/) — User-facing documentation

Each project has its own `CLAUDE.md` at `../Birko.{ProjectName}/CLAUDE.md` with project-specific details.

## Architecture

### Store Hierarchy (Template Method Pattern)
```
AbstractStore -> AbstractBulkStore (sync)
AbstractAsyncStore -> AbstractAsyncBulkStore (async)
```

Stores use lazy-init: CRUD methods auto-call `Init()`/`InitAsync()` before first use (via `EnsureInitialized`/`EnsureInitializedAsync` with double-checked locking). Concrete stores override `*Core` methods (e.g., `CreateCoreAsync` instead of `CreateAsync`). Public methods are `virtual` on the base class.

### SQL Stores
```
DataBaseStore<DB,T> -> DataBaseBulkStore<DB,T> (sync)
AsyncDataBaseStore<DB,T> -> AsyncDataBaseBulkStore<DB,T> (async)
```

### Repository Hierarchy
```
AbstractRepository -> AbstractBulkRepository (sync)
AbstractAsyncRepository -> AbstractAsyncBulkRepository (async)
```

### Settings Chain (Birko.Configuration)
```
ISettings (GetId)
  -> Settings (Location, Name)
    -> PasswordSettings (+Password)
      -> RemoteSettings (+UserName, +Port, +UseSecure)
        -> SqlSettings (+CommandTimeout, +ConnectionTimeout, abstract GetConnectionString)
          -> MSSqlSettings (+MultipleActiveResultSets, +TrustServerCertificate)
          -> MySqlSettings (+BulkInsertBatchSize)
          -> PostgreSqlSettings (+UseBinaryImport)
          -> TimescaleDBSettings (+TimeColumn, +ChunkTimeInterval)
    -> SqLiteSettings (+CommandTimeout, Path, GetConnectionString) — extends PasswordSettings
    -> CosmosDB Settings (+PartitionKeyPath, +RequestTimeout, +AllowBulkExecution, GetCosmosClientOptions)
    -> RavenDB Settings (+RequestTimeout, CreateDocumentStore)
    -> MongoDB Settings (+AuthDatabase, +ReplicaSet, GetConnectionString) — already existed
    -> RedisSettings (+Database, +KeyPrefix, GetConnectionString) — already existed
```

### Dependency Flow
```
Birko.Contracts (zero deps: ILoadable, ICopyable, IDefault, ITimestamped, IGuidEntity, ILogEntity, RetryPolicy)
  -> Birko.Configuration (Settings hierarchy, namespace Birko.Configuration)
  -> Birko.Data.Core (AbstractModel, ViewModels, Filters, Exceptions)
    -> Birko.Data.Stores (store interfaces, imports Configuration)
      -> Birko.Data.Repositories

Birko.Models.Contracts (zero deps: ICatalogItem, IPriceable, IVariantable, ICategorizeable, IBatchable, ILocatable, IHierarchical, IDocument, IContactable, IAddressable)
  -> Birko.Models (AbstractPercentage, AbstractTree, ValueData + Value Objects: Money, MoneyWithTax, Percentage, PostalAddress, Quantity)
    -> Birko.Models.Inventory / .Pricing / .Customers / .Users / .Product / .Category / .SEO (clean, no SQL attrs)
    -> Birko.Models.SQL (ModelMap<T>, IModelMapping<T>, ModelMapRegistry — fluent SQL mapping framework only, no canonical mappings)
      -> Birko.Models.Users.SQL / .Customers.SQL / .Inventory.SQL / .Pricing.SQL / .Product.SQL
         (one optional sibling per domain — pre-built IModelMapping<T> for User/Role/Tenant, Address/Customer,
          StockItem/StorageLocation/InventoryDocumentLine, Currency/Tax/PriceGroup, MeasureUnit/UnitConversion/ProductPartnerCode)

Birko.Time.Abstractions (zero deps: IDateTimeProvider, SystemDateTimeProvider, TestDateTimeProvider)
  -> Birko.Time (calendars, working hours, time zones)

Birko.Data.Patterns + Birko.Data.Tenant + Birko.Time.Abstractions
  -> Birko.Data.Composition (StoreWrapperBuilder — runtime decorator chains)

Birko.Data.Core
  -> Birko.Data.Tagging (ITaggable, Tag, EntityTag, ITagService, TagServiceBase)

Birko.Data.Patterns (FieldType, FieldDescriptor, ISchemaBuilder, ICollectionBuilder, IIndexBuilder, IIndexManager, IndexDefinition, ISoftDeletable, IAuditable, ISpecification, IUnitOfWork, PagedResult)
  -> Birko.Data.Migrations (IMigrationContext, IDataMigrator, IContextualMigration, IMigration, IMigrationRunner, IMigrationStore)
    -> Birko.Data.Migrations.SQL (SqlMigrationContext — reuses AbstractConnector), .MongoDB, .ElasticSearch, .RavenDB, .CosmosDB, .InfluxDB, .TimescaleDB

Birko.AI.Contracts (zero deps: ILlmProvider, Message, ContentBlock, Tool, AgentOptions, LlmProviderFactory)
  -> Birko.AI (LlmProviderBase, Agent base, AgentFactory (registration-based), default tools)
    -> Birko.AI.Providers (Claude, OpenAI, Gemini, Ollama, AzureOpenAI, etc. + ProviderRegistration)
    -> Birko.AI.Agents (CodingAgent, language agents, media agents + AgentRegistration)
    -> Birko.AI.Orchestration (ITaskDispatcher, ImplementationPlan, StepDependencyAnalyzer)
  -> Birko.AI.Resilience (ProviderRateLimiter, ProviderCircuitBreaker, CostTrackingService, TrackedLlmProvider)

Birko.Communication.OAuth (IOAuthClient, OAuthClient, OAuthSettings)
  -> Birko.Communication.OAuth.Providers (GitHubOAuthProvider — pre-configured device flow)

Birko.Communication.GraphQL (IGraphQLClient, GraphQLClient, GraphQLSettings — queries, mutations, subscriptions over HttpClient + ClientWebSocket)

Birko.Communication.gRPC (GrpcSettings, GrpcChannelPool, GrpcClientFactory, GrpcAuthenticationInterceptor, GrpcException — client over Grpc.Net.Client)
  -> Birko.Communication.gRPC.Server (GrpcServerSettings, AddBirkoGrpc, GrpcServerAuthenticationInterceptor — server over Grpc.AspNetCore; mirrors REST / REST.Server split)

Birko.BackgroundJobs (IJobQueue, JobDescriptor, RetryPolicy, JobProcessor, JobScheduler)
  -> 8 backends: .SQL, .ElasticSearch, .MongoDB, .RavenDB, .JSON, .XML, .Redis, .CosmosDB

Birko.Workflow (WorkflowBuilder, WorkflowEngine, guards, actions, Mermaid/DOT)
  -> 7 backends: .SQL, .ElasticSearch, .MongoDB, .RavenDB, .JSON, .XML, .CosmosDB
```

### Reference Implementations
- **ElasticSearch** store — reference for async/bulk operations
- **JSON** store — reference for file-based storage
- **XML** store — reference for file-based storage with `System.Xml.Serialization` (note: no native `Dictionary` support — use wrapper types)
- **InMemory** store (`Birko.Data.InMemory`) — simplest possible store (thread-safe `ConcurrentDictionary`, no persistence); the canonical test double / prototyping backend

## Usage in Consumer Solutions

When using Birko.Framework projects in your solution, create **one or more aggregator library projects** that bundle the `Birko.*` shared projects you need (e.g. `FisData.Birko`, `Symbio.Birko`, or split by layer like `{Solution}.Birko.Core` + `{Solution}.Birko.Edge` + `{Solution}.Birko.Ai`). Your other projects reference the aggregator(s) instead of importing `.projitems` directly. Default to a single aggregator; split only when concrete pain shows up — bloated binaries, leaky transitive deps, or unused-heavy-dependency pull-ins (camera, AI, hardware). This avoids compilation and transitive reference issues that arise when multiple projects import overlapping sets of shared projects independently.

Use `$(BirkoSrc)` (resolved from a root `Directory.Build.props`) for all `Import Project="…\Birko.X\Birko.X.projitems"` paths instead of hard-coded absolutes. The property reads `/p:BirkoSrc=…` first, then the `BIRKO_SRC` environment variable, then defaults to the `Birko\Framework` checkout relative to the consumer repo (the recommended layout nests the framework under a `Birko\Framework` bucket with consumers under a sibling `Birko\Consumers`, so the default resolves `..\..\Framework`; flat-sibling checkouts would use `..`). TypeScript bundlers consuming `Birko.Web.*` sources resolve a **separate `Birko\Web` bucket** — the frontend libs (`Birko.Web.Core` / `.Components` / `.Shell`) live there, apart from the .NET `Birko\Framework`. Their `build.js` walks up to find `Birko\Web` (or honors `BIRKO_SRC`). So the two builds resolve sibling buckets: `Birko\Framework` for MSBuild, `Birko\Web` for esbuild. See [README — Usage in Consumer Solutions](README.md#usage-in-consumer-solutions) for the full pattern.

## Conventions
- All stores implement: `IStore`, `IAsyncStore`, `IBulkStore`, `IAsyncBulkStore`
- All repositories implement: `IRepository`, `IAsyncRepository`, `IBulkRepository`, `IAsyncBulkRepository`
- Bulk stores support filter-based Update/Delete: `Update(filter, PropertyUpdate<T>)`, `Update(filter, Action<T>)`, `Delete(filter)`
- Use `PropertyUpdate<T>` for native platform operations (SQL SET, MongoDB $set, ES UpdateByQuery); use `Action<T>` for complex mutations
- New platform stores should override `Update(filter, PropertyUpdate<T>)` and `Delete(filter)` for native performance
- On a bulk store, `Read(filter)` returns the **collection** (`IEnumerable<T>`), not a single entity: the bulk `Read(filter, orderBy, limit, offset)` overload hides the inherited single-result `Read(filter)` from member lookup (C# only considers the most-derived type that declares the method name). Use `ReadFirst(filter)` / `ReadFirstAsync(filter)` (on `IBulkReadStore<T>` / `IAsyncBulkReadStore<T>`) for a single result, or cast to `IReadStore<T>` / `IAsyncReadStore<T>`
- Concrete stores override `protected *Core` methods (e.g., `CreateCoreAsync`, `ReadCore`), **NOT** the public CRUD methods. The base class handles lazy-init in the public wrapper
- Use protected setters for properties that derived classes need to modify
- `RemoteSettings` should be passed via `base.SetSettings()`, not constructed inline
- **A destructive all-rows operation is named `*All`; only reading everything gets the short name.** The
  asymmetry is deliberate and is not to be "fixed" by someone reasoning from symmetry: read-all is the
  existing parameterless overload (`Read()`, `ReadAsync(ct)`) because reading everything is harmless,
  while all-rows writes are `DeleteAll()` / `UpdateAll(updates)` (+ async) on the SQL stores and both
  portable bases. **`Delete()` must never be spelled parameterless** — it would sit one keystroke from
  `Delete(items)`, and a short name is a footgun in proportion to how destructive the operation is. A
  filter-based destructive overload **requires** a filter: null throws `ArgumentNullException` naming the
  `*All` door, and a filter that renders no `WHERE` throws `WholeTableWriteException` at the connector
  (SH-H002/SH-M023). `Delete(x => true)` is kept working as the `*All` synonym — a single normalized
  `ConstantExpression(true)` node, not a whitelist of always-true shapes. **No `1 = 1` is emitted** to
  mark a deliberate all-rows write: the SQL stays clean (`DELETE FROM "T"`), because `1 = 1` in a query
  log is indistinguishable from `' OR 1=1--` and trains operators to ignore the pattern that should
  alarm them
- **That rule is not about SQL — it is about any write whose scope can silently become "everything", in
  any backend.** The bullet above was written for `WHERE`-less statements, and the second instance arrived in
  Redis (SH-H006/TASK-117): with no `KeyPrefix` configured — the *default*, since `RedisSettings.KeyPrefix` is
  an unassigned `string?` — `RemoveByPrefixAsync("")` scanned `"*"` and `DEL`'d every key, and
  `ClearAsync` fell through to `FLUSHDB`. Both targeted the queued messages and pending jobs of every sibling
  sharing the database. **The two doors had different volumes, and the quiet one was not the one the finding
  named**: `SCAN`/`DEL` are not admin-gated so the prefix door destroyed data silently on every configuration,
  while `FLUSHDB` *is* gated, so it flushed only for a consumer whose `RawConnectionString` carries
  `allowAdmin=true` and threw for everyone else. Four parts generalise, and a third sink should be a reuse
  rather than a rediscovery:
  - **Resolve the scope once, and refuse when it reduces to everything.** One helper answers "what does this
    operation cover" for every door into the same write — `ResolveOwnedKeyPattern` for the cache the way
    `ResolveFieldNameIn` does for identifiers. `ClearAsync` was only the *documented* door;
    `RemoveByPrefixAsync("")` reached the identical whole-database delete by scanning `"*"`, and one shared
    resolver closed both. **Guard on the resolved scope, never on the configuration that produced it** — the
    blunt check ("no `KeyPrefix`") would have refused the bounded, legitimate `RemoveByPrefixAsync("user:")`.
  - **The refusal is `WholeTableWriteException`'s sibling**, deriving from `InvalidOperationException` for the
    same reason (existing `catch` blocks keep working; a host that wants to report it distinctly can catch
    the type first), and its message **names the deliberate door**. A guard that only says "no" gets reached
    around.
  - **The explicit door is named for what it destroys, not for the caller's intent.** `*All` is the SQL
    spelling because the scope there *is* the caller's own table; where the blast radius is somebody else's
    data, the honest name says so — hence `FlushDatabaseAsync`, not `ClearAll`, and it is declared on the
    concrete `RedisCache` and **not on `ICache`**, so a cache-shaped contract cannot empty a database. Same
    reasoning as keeping `Delete()` unspellable: a short or reassuring name is a footgun in proportion to how
    destructive the operation is. Assert the off-interface property with a test — "I didn't add it to the
    interface" is construction, not evidence, and the next person to widen the interface breaks it silently.
  - **Verify the escape hatch opens — a guard whose opt-out throws is a wall wearing a door's label.** The
    refusal message pointed at `FlushDatabaseAsync`, which StackExchange.Redis gates behind `allowAdmin=true`
    that `RedisSettings.GetConnectionString()` never emits: an operator following the message would have hit a
    second, unrelated exception. Fixed by naming `KeyPrefix` **first** (it works on every configuration) and
    stating the admin precondition on the flush door. The § SH-H037 rule says fail-fast is legitimate *only*
    where an opt-out exists and is checked first — so **the opt-out is part of the fix and needs its own
    test**, not a mention in a message nobody executed.
- **A scope guard tests what the statement MEANS, never whether text was produced — and an always-true
  term is reduced away, never rendered.** Third instance of the family above (SH-H002 SQL, SH-H006 Redis),
  and the one that shows a guard can be defeated by the code it is guarding. `AddRequiredWhere` refuses a
  destructive statement when *nothing was rendered*; `InConditionStrategy` rendered an empty `NOT IN` as
  `1 = 1`, which is a **non-empty `WHERE` that constrains nothing** — so `Delete(x => !empty.Contains(x.Col))`
  emptied the table with the guard's blessing (measured: 0 of 3 rows left, no exception; the `Update` twin
  rewrote 3 of 3). The tautology was chosen *for* its harmlessness and that is exactly what made it a
  bypass. Four parts generalise (TASK-137):
  - **"Something was emitted" is not a scope check.** Guard on the resolved *meaning* —
    `AbstractConnectorBase.IsAlwaysTrueCondition` / `IsAlwaysTrueChain`, called by **both**
    `WouldTargetEveryRow` and the renderer, so the guard and the emitted SQL cannot disagree about what
    "everything" covers. Same "one producer" discipline as the entry below, applied to scope instead of to
    identifiers: two implementations is how a guard ends up agreeing with itself and disagreeing with the SQL.
  - **An always-true term has no rendering, by design.** `A AND TRUE` is `A`, so drop it; `A OR TRUE` is
    `TRUE`, so **collapse the chain** rather than dropping the term (dropping it silently narrows the result —
    a wrong answer, worse than the constant being removed); and `NOT (A OR TRUE)` is **always-false**, so a
    negated group *flips* and renders the existing `1 = 0`. The asymmetry with the always-false side is not an
    oversight: `A AND FALSE` is `FALSE`, not `A`, so `1 = 0` must be emitted — and it carries no injection
    connotation, while `1 = 1` is the `' OR 1=1--` signature. A strategy asked to render the unrenderable
    **throws** (§ SH-H037): a constant is the defect and an empty string is silently joined between its
    neighbours' separators into `A AND  AND B`.
  - **Refuse where the caller can still catch it.** `AddRequiredWhere` runs inside
    `DoCommandWithTransaction`, whose `InitException` re-wraps every callback exception in a bare `Exception`
    that no `catch (WholeTableWriteException)` can select — so the reduction must be visible to the
    **pre-check**, or a silent whole-table delete merely becomes an unhandled 500. This is why SH-H002 has a
    pre-check *and* a rendered-clause backstop; a new "means everything" shape has to be taught to the former.
  - **Reduced-to-everything is REFUSED; only a one-node explicit constant is the door.** `x => true` is the
    `DeleteAll()` synonym; `x => true || x.A == 1` and an empty `NOT IN` merely *reduce* to everything and get
    the refusal. The task file argued the opposite from a false premise ("otherwise it starts throwing, which
    would be a regression") — it does not throw today, it wipes the table. **Measure the shipped behaviour
    before costing a remedy against it.**
- **An identifier that reaches interpolated SQL is resolved against table metadata, never validated as
  text — and the two sinks share one lookup.** Values are parameterised; *identifiers* cannot be, so every
  column name in `CommandText` arrives by interpolation and the only safe source is the schema. Two sinks
  have now shipped this defect: ORDER BY keys (SH-H003/M022, TASK-110) and rule fields (SH-H023, TASK-111),
  where `Field = "Rank; CREATE TABLE Pwned (x INTEGER); --"` **created the table**. Both now go through
  `DataBase.ResolveFieldNameIn` — property name, then mapped column name — so **the resolution IS the
  whitelist**: what survives is a name read out of metadata, and caller text has no path to the statement.
  One shared lookup because a consumer should not have to learn two rules for which field names are
  accepted — and it is `internal`, not `private`, precisely so the third sink can *call* it. **Do not quote
  the resolved column identifier**: this codebase emits column identifiers bare everywhere (DDL, SELECT
  list, every condition strategy) and quotes only table names, so quoting one sink breaks it on PostgreSQL,
  where the unquoted DDL identifier folds to lower case. Quoting was never what closed either injection.
  (**That argument covers the column, not the table qualifier.** An emitted `Table.Column` leaves the table
  part unquoted while the `FROM` clause quotes it, which on PostgreSQL breaks any table whose name is not
  already lower case. That is pre-existing and framework-wide — `GetSelectFields(true)` does the same — so
  it is not a reason to diverge in one sink, but do not cite the PostgreSQL rationale as if the qualifier
  were covered by it.) Where an entity type genuinely isn't available, the fallback is a **bare
  identifier check** (`ValidateRuleFieldIdentifier`) — weaker, since it cannot fix a `[NamedField]`
  remapping, but it still refuses every payload; anchor such a pattern with `\A…\z`, because .NET's `$`
  also matches before a trailing newline. Sanitising the *parameter name* is not this check:
  `SqlBuilderContext.GenerateParameterName` already did that, and it is what made SH-H023 look safe on a
  skim
- **A name that one layer CREATES and another layer READS BACK has exactly one producer.** Same "resolve it
  once" discipline as the two rules below, applied to identifiers rather than to scope or tenancy — and the
  failure mode is different: not a wrong answer, but two layers each patching their own half until the
  emitted SQL carries both. An aggregate view column's alias *becomes* the column name in the view's DDL, so
  three places must agree on it: the SELECT-list alias (`Table.GetSelectFields`), the persistent read
  (`View.GetPersistentViewSelectFields`) and the sort key (`DataBase.ViewOrderFieldName`). CR-L195 decided
  the name is the **view property**, taught two of the three to read `field.Property.Name`, and left the
  alias reading the `Fields` dictionary key — which both view builders set to the SQL *function* name. The
  two disagreeing producers then emitted `COUNT(VOrders.PersonId) as COUNT AS "OrderCount"` — **two aliases
  on one column**, a syntax error on every provider, so no persistent aggregate view could be created at all
  (TASK-129). Three parts generalise:
  - **Fix it at the producer, not at each emit site.** Suppressing the second alias would have shipped valid
    SQL with the identity still split, so the *next* aggregate sink rediscovers it. All three now read
    `Property.Name`, so they agree **by construction** — a fourth consumer is correct without being told.
  - **A duplicate key that is silently skipped is the same defect wearing a quieter coat.** The mis-keying
    had a second consequence nobody filed: `View.AddField` skips a `Fields` key it already holds, so two
    aggregates of the same function — both keyed `"SUM"` — produced **one** column, and the lost property
    read back as `default(T)` with no exception and no log entry. Worse than the loud one, and found only by
    running the generator. Where an identity is used as a dictionary key, **check what happens on collision
    before trusting the key** (§ SH-H037's rule, arriving through a `ContainsKey` guard instead of a
    `return null`). **TASK-129 re-keyed only the aggregates and left the guard, which moved the collision
    rather than closing it** — non-aggregates stayed keyed by *source column* beside aggregates keyed by
    *view property*, two namespaces in one key space, so an aggregate whose view property matched a
    neighbouring column's source name silently lost one of them (TASK-207, which also found the older shape:
    two view properties projecting one source column). **Every view field is now keyed by the property it
    populates** (`View.ViewFieldKey`) — one namespace, collisions impossible rather than reported. Two things
    generalise past views: **re-keying half a dictionary is not a fix, it is a narrower bug**, and a
    partial-fix comment that calls the new key "unique by construction" is true only within the half that
    changed — say which half. The residual guard now *throws* `FieldAttributeException` for a genuinely
    different field on a taken key, with the idempotent re-add checked first as SH-H037's required opt-out
    (`ViewAttribute` is `AllowMultiple = true`, so `LoadView` legitimately re-presents every field, as a
    **fresh instance** — so the opt-out compares by value; reference equality would condemn every
    multi-`[View]` view).
  - **⚠ SUPERSEDED by TASK-209 — read the entry below this list before applying the next bullet.** Its
    reasoning (match the sink's reader) is sound; its *conclusion* for this sink was wrong, because it
    reasoned from one reader instead of from the base-table DDL underneath both. Kept because the way it
    was wrong is the lesson.
  - **A created identifier is quoted the way its reader quotes it — the bare-identifier rule below does NOT
    apply.** This is the one place this codebase quotes a column identifier, and the distinction is
    *creating* versus *referencing*: the DDL alias becomes a real column, and its only reader
    (`CreatePersistentViewSelectCommand`) emits `QuoteIdentifier(GetPersistentViewSelectFields()[i])` through
    the **same connector**, so quoted round-trips on every provider (`"` ANSI, `` ` `` MySQL, `[]` MSSql)
    while a bare `as OrderCount` creates `ordercount` on PostgreSQL against a read asking for `"OrderCount"`
    — created, then unqueryable. **TASK-129's first attempt got this backwards** by applying the bare rule
    below on autopilot; the inline review at the close gate caught it before commit. The general rule:
    **match the sink's reader, then check whether the codebase-wide convention actually covers that sink** —
    "identifiers are emitted bare" was written for identifiers being *referenced*.
    (Three producers of a persistent view's column names currently take three positions: the DDL projection
    is bare + table-qualified for non-aggregates, the persistent SELECT quotes, the persistent ORDER BY
    interpolates bare. That means **non-aggregate** persistent view columns are still broken on PostgreSQL —
    TASK-209, found by this fix's own test and deliberately out of its scope.)
- **Quote table identifiers; never quote column identifiers. The base-table DDL is what settles it, and it
  is the thing to check before reasoning from any single sink.** `CreateTable` quotes the table name and
  emits **column definitions bare**, so on PostgreSQL — the one supported provider that case-folds an
  unquoted identifier — every base column is stored folded (`avpersons.name`) while every table keeps its
  PascalCase. It follows mechanically that a column reference must be **bare** to resolve and a table
  reference **quoted**. Four view sinks disagreed and SQL views were therefore **unusable on PostgreSQL** —
  not degraded, unreachable — while the entire suite stayed green because every end-to-end view test runs on
  case-insensitive SQLite (TASK-209, measured against 16.4). Four things generalise:
  - **The failures queue, so the filed one is not necessarily the reachable one.** The DDL died on an
    unquoted *qualifier* (`missing FROM-clause entry for table "avpersons"`), then on a *quoted join column*
    (`column AvOrders.PersonId does not exist`), and only then on the quoted read that had actually been
    filed. Fixing the filed defect alone would have changed nothing observable and closed the ticket.
    **When a defect is provider-specific, reproduce on that provider before costing the fix** — the task's
    own acceptance demanded it "cannot distinguish a fix from a no-op", and that is exactly what it caught.
  - **"Match the sink's reader" is not enough when the readers disagree with the storage.** TASK-129 quoted
    the DDL alias to agree with a reader that quoted, and that pairing was internally consistent and still
    wrong, because both disagreed with the bare column the base DDL had created. Reason from where the
    identifier is **created**, not from the nearest consumer of it.
  - **An assertion that a DDL/query call "did not throw" is worth nothing here, because this layer swallows.**
    `CreateView` swallowed `42P01` and reported success, so the first version of the PostgreSQL regression
    test passed against the unfixed code; asking `information_schema.views` instead took the split from 2/3
    to 3/3. The same swallow turns a broken on-the-fly view into an **empty result** rather than an error
    (TASK-211) — which is why none of this was ever visible. **Assert against the catalogue or the rows.**
  - **Check whether the "risk" of a convention change is already impossible.** Unquoting was challenged on
    reserved words (`Order`, `User`). Measured: `CREATE TABLE "T" (Order text)` is already a syntax error, so
    such a model cannot have its table created at all and there is no working case to break. A risk that
    cannot be realised should be measured away, not mitigated.
- **An operation that can take its tenant from more than one source resolves it ONCE, and refuses rather
  than picking a winner.** Two live answers in one run is the defect, not a precedence question:
  `TenantSyncProvider` keyed its knowledge from `options.TenantGuid` and its write filter from the ambient
  context, so the documented background-job call installed *no* write filter at all (SH-H050). One resolver
  feeds every consumer in the run — fetch predicate, write filter, knowledge key. An explicit tenant that
  **contradicts** the ambient one throws `TenantMismatchException`; **no** tenant from any source, on a
  tenant-scoped entity, throws `TenantScopeRequiredException`. `ITenantContext.IsAllTenantsScope` is the
  only sanctioned cross-tenant path, and an explicit tenant inside it still narrows the run
- **Scope the read, not just the write.** A tenant term on a write filter alone leaves every other path —
  compare, preview, version-hash, bookkeeping, and above all **delete** — operating on every tenant's rows
  (SH-H051/H052). Put the term where the data is *fetched* so those paths are correct by construction, and
  keep the write check as defence in depth. Re-check the tenant on the materialized rows too: a fetch
  predicate is only as strong as the backend's translation of it, and this family has shipped filters a
  backend silently widened to match-all
- **Any middleware that resolves a tenant must publish it via `ResolvedTenant.Publish(context, guid, source)`**
  (`Birko.Data.Tenant/Middleware/ResolvedTenant.cs`). `TenantHeaderClaimGuardMiddleware` correlates that
  published value with the JWT `tenant_id` claim after authentication, so a source that does not publish is a
  source the guard cannot see — and it fails *open*, silently (SH-H048). Publish on `HttpContext.Items` under
  the fixed key, never through `ITenantContext`: `UseTenantMiddleware` binds that from the root provider
  (SH-H049), so a scoped registration hands the guard a different instance
- **A mapper that cannot express something refuses; it never drops it quietly.** `CreateAbstractField`
  ended its CLR-type dispatch in `return null`, and `LoadField` turned that into an empty field set — so
  `long` / `short` / `double` / `float` / `byte[]` properties got **no column, no write, no read, no
  exception and no log entry** (SH-H037). The missing arms were half the defect; the silence was the half
  that guaranteed the *next* unmapped type would repeat it. An unmappable property now throws
  `FieldAttributeException` at table load, naming the declaring type, the property and its CLR type.
  **Fail-fast is only legitimate where an opt-out exists and is checked first** — here `[IgnoreField]` and
  `[NotMapped]`, both evaluated before the dispatch — otherwise a guard is a wall. And **measure the blast
  radius before turning silence into a throw**: this one was cleared against 19 SQL-touching suites,
  including every `Birko.Models.*.SQL` domain suite, because the change breaks any consumer model carrying
  a property the mapper never covered
- **Lazy schema-ensure degrades and reports; an explicit schema call throws.** Stores run schema-ensure on
  first data access and set `_initialized` only *after* it returns, so anything that throws in there leaves
  the store permanently uninitialised and re-throws on **every** later operation — reads included. An
  unbuildable index therefore took down the entity's whole surface, and the rows needed to repair it were
  unreachable through the very store that refused to start (TASK-204). Schema-ensure attempts **one index
  per statement** and records failures on `AbstractConnector.IndexCreationFailures` /
  `OnIndexCreationFailed`; the public `CreateIndexes` / `CreateIndexesAsync` are **unchanged and still
  throw**, because an explicit call (migrations' `SqlSchemaBuilder`) is a caller asking for that index *now*.
  Degrade only what is a constraint or an optimisation — never correctness — and **report rather than
  swallow**. Keep the re-attempt on later runs: that is what lets the index appear once the data is repaired.
- **State a host reads must be current state, keyed — not an append-only log.** Connectors are cached
  process-wide per (type, settings id) in `DataBase.GetConnector` while `_initialized` lives on the *store*,
  so a scoped store per HTTP request re-runs schema-ensure per request against one shared connector. A
  `List` of failures grew by one entry per request, forever, and re-raised its event each time (measured:
  5 stores → 5 entries, 5 re-executed failing DDL statements). Key such collections by their identity,
  fire events on the **transition** into the condition, and **clear the record when it no longer holds** —
  a report that cannot un-report is a report an operator learns to ignore.

## Task tracking — this repo is the polyrepo family's aggregator

The Birko family is a **polyrepo** (every `Birko.*` sub-project is its own git repo); this
repo is its **aggregator** (the `.slnx`, the shared CLAUDE docs — and the cross-cutting plan).
This is the aggregator override the generic `tasks` skill's shape detection defers to:

- **Cross-cutting epics** (work spanning several `Birko.*` sub-projects) live in **this repo's
  `tasks/`**, with the affected sub-projects listed in the EPIC's `affects:` frontmatter
  (e.g. `affects: [Birko.AI, Birko.Data.Core]`).
- **Single-sub-project work** stays in that sub-repo's own `tasks/` (the default
  walk-up-to-`.git` rule already lands there) — don't track it here.
- Cross-cutting `docs/features/` and `docs/specs/` follow the same split: family-wide at this
  aggregator, per-project in each sub-repo (a cross-cutting story regens specs per affected
  project, driven by `affects:`).

### Integration model — commit to `main`, one commit per repo

`tasks/.config.yml` sets `integration: single-branch`. **This family does not branch per task**, so
`/tasks pick` offers no `task/TASK-NNN` branch and `/tasks close` skips its merge step. `done` still
means *landed on the default branch* — only the mechanism differs from the generic PR-per-task default.

Because this is a polyrepo, **one fix normally spans three independent repos and needs three commits**:

| Repo | Contents | Message shape |
|---|---|---|
| `Framework/Birko.{Project}` | the production change | `fix(<FINDING-ID>): <what now holds>` |
| `Framework.Tests/Birko.{Project}.Tests` | the regression suite | `test(<FINDING-ID>): <what it pins>` |
| `Framework/Birko.Framework` (here) | task file + spec + dashboard | `tasks(TASK-NNN): <outcome>` |

**Order matters:** commit the production fix first, so its SHA can go into the task's `pr:` field
before the aggregator commit — otherwise the tracking file lands referencing nothing.

- **Stage explicitly. Never `git add -A`.**
- **No `Co-Authored-By:` trailer.** Standing preference; overrides the harness default. Don't copy it
  from older commits that carry it.
- Body over subject: say what was wrong and why the fix is shaped the way it is. A future reader gets
  the commit, not the session that produced it.

## Skills shipped by this repo

`.claude/skills/` is the home of the Birko-specific skills. They **build on top of the generic
project-lifecycle-skills set** (never the reverse — the generic skills know only a "stack
scaffolder" hook, not Birko). Project-local ones (new-birko-subproject, new-store-backend,
verify-conventions (the project-local shadow of the generic one), the roll-changelog shadow) auto-load
only inside this repo; the
consumer-facing ones (birko-new-project, new-birko-web-page, new-birko-web-component,
design-agent) are shared user-level via [install-skills.ps1](install-skills.ps1) (junctions —
edit here, live immediately).

## Code Style
- **Guard clauses:** Use early returns instead of wrapping entire method bodies in if blocks. Prefer `if (x == null) return;` over `if (x != null) { ... }`.
- **No nullable warnings:** All new code must compile without CS8600–CS8605, CS8618, CS8625. Use proper null checks, `!` only when provably safe, or `?` annotations.

## Testing
- All test projects use **xUnit + FluentAssertions**
- **Tests live in a parallel tree, not in the project repo:** `Birko.{Project}`'s tests are at
  `C:\Source\Birko\Framework.Tests\Birko.{Project}.Tests` (its own git repo — see the integration
  model above; a fix and its regression suite are two commits in two repos). Run with
  `dotnet test --nologo` from the test project.
- Every new public functionality must have corresponding tests in `Birko.{ProjectName}.Tests`
- Test both success and failure cases; include edge cases and boundary conditions
- Each test project has its own `CLAUDE.md` describing scope and conventions
- See [CLAUDE-maintenance.md](CLAUDE-maintenance.md) for test requirements on new projects and health check patterns

## Recent Updates

The rolling per-change log now lives entirely in [CHANGELOG.md](CHANGELOG.md) (newest-first). Add new architectural / behavioral change notes here as `### Title (YYYY-MM-DD)` entries; when this section grows past ~5–8 entries, roll the oldest into CHANGELOG.md (the project-local `/roll-changelog` skill does this). Granular code-review-remediation progress is tracked in `tasks/EPIC-014-code-review-remediation`, not here.

### A computed operand inside `Contains` was answered by a different predicate (2026-08-14)

TASK-213, found by TASK-137's own spec step — adding its shapes to the compiled-delegate oracle made a case
fail for an unrelated reason. `ids.Contains(x.Amount + 1)` never emitted an `IN`: the arm looped **every**
argument through `ParseConditionExpression`, so a computed operand was parsed as a nested **predicate**, took
the binary-comparison path, and fabricated a **subcondition** (`Amount = 1`) on the condition being built.
`AppendConditionTo` branches on `SubConditions` before it consults `Type`, so the `In` and its values were
discarded and the fabricated equality was emitted instead. Measured on SQLite: **1 row where C# says 0**, and
**3 where C# says 4** — wrong in both directions, silently. The operand now resolves through
`RenderValueFragment` exactly as a comparison's column side does. Split: **18 of 21**. Four things worth
carrying:

- **⚠ Consumers: an operand this parser cannot express now throws.** `ids.Contains(x.Name.Length)` (and an
  unmapped collection property in the extension-method form) previously returned rows chosen by a substituted
  predicate; they now raise `NotSupportedException` at parse time. That is § SH-H037's position, and the blast
  radius was measured across 22 SQL-touching suites with no failures — but the change is visible to a consumer
  whose predicate was quietly wrong.
- **The fix was a reuse, and the task's own § Approach had budgeted for a translator.** `RenderValueFragment`
  already rendered arithmetic / `COALESCE` / `CASE` / `.Value` and already threw for the rest, and
  `BuildValueComparison` was already doing precisely this for comparisons — so "translate or refuse" was not
  an open decision, it was answered by code that shipped months ago. Same shape as TASK-112, where the
  per-provider type mapping the task called "the bulk of the work" already existed. **Look for the existing
  producer before costing a new one.**
- **A test written to pin the fix passed against the defect, and only the revert said so.** The always-true
  read test used a seed with no NULL `Score`, and over non-null values the fabricated `NOT (Score = 0)`
  returns exactly the right rows. Adding one NULL row made SQL's three-valued logic diverge from C# and took
  the split from 17 to 18. Third instance of this in the epic (TASK-113, TASK-118) — **the revert is what
  classifies a test, not the intent it was written with.**
- **Two of the three surviving pins pass by arithmetic coincidence**, not by design: for
  `Ids.Contains(x.Score ?? 0)` and `x.Amount > 4 && Ids.Contains(x.Amount + 1)` the correct answer and the
  fabricated predicate's answer are both 0 rows on that seed. Their shapes are still covered by evidence
  because the *positive-match* variants of each fail on revert. Worth designing sets that match rather than
  sets that happen to exclude everything.

### The tautology chosen for being harmless was the thing that walked past the guard (2026-08-14)

TASK-137. An empty `NOT IN` rendered `WHERE 1 = 1`, filed — correctly — as a log-hygiene defect: `1 = 1` is the
`' OR 1=1--` signature and trains operators to scroll past it. What nobody had checked was what the constant
does one layer up. `AddRequiredWhere`'s whole contract (SH-H002 / TASK-109, landed 18 days earlier) is
*"nothing rendered → refuse"*, and `1 = 1` is a **non-empty `WHERE` that constrains nothing** — so
`store.DeleteAsync(x => !emptyIds.Contains(x.Amount))` reached a whole-table `DELETE` **with the guard's
blessing**: measured on real SQLite, 0 of 3 rows left and no exception, while the `Update` twin rewrote 3 of 3.
Always-true terms are now reduced away instead of rendered, and `WouldTargetEveryRow` shares that reduction.
The standing rule is in § Conventions above. Split: **29 of 54** (re-derived; the first number, 29 of 45,
expired when the fix's own spec step added 9 oracle cases). Five things worth carrying:

- **⚠ Consumers: a call that used to succeed now throws.** `Delete`/`Update` with a filter that reduces to
  everything — an empty negated `Contains`, or an `OR` chain containing one — now raises
  `WholeTableWriteException` instead of writing every row. Anything relying on that (deliberately or not) must
  move to `DeleteAll()` / `UpdateAll(updates)` or an explicit `x => true`. Reads are byte-for-byte unaffected.
  `InConditionStrategy.BuildSql` also now throws for the empty negated case rather than returning `1 = 1`,
  which is breaking only for code calling the strategy directly rather than through `ConditionDefinition`.
- **The finding was right, its severity was wrong, and the task's prescribed remedy would have preserved the
  bug.** Acceptance criterion 2 required the sole-condition destructive case to reach TASK-109's
  *deliberate-all-rows* path — i.e. to keep deleting everything — reasoning that refusing "would be a
  regression, not a fix". It was inverted before any code was written. **Sixth time in a month a written
  remedy needed re-costing** (TASK-111, 112, 117, 129, 207). The tell each time is the same: the approach
  reasons about what the code *should* do without measuring what it *does*.
- **Reads were never wrong, which is why nothing found this for 18 days.** All eight read shapes returned
  correct rows before and after — `1 = 1` is genuinely always-true. The 9 cases added to the
  compiled-delegate oracle (`SqlExpressionParityTests`, the strongest instrument available: SQL vs
  `expr.Compile()` over a real database) therefore **pass either way** and are recorded as contract pins.
  When a defect's whole surface is a guard being fooled, no amount of result-correctness testing can see it.
- **The fix's own first draft had the defect it was fixing.** Dropping an always-true term that *opened* an
  `OR` run would have rendered `A OR TRUE AND B` as `A AND B` — the intersection instead of the union, a
  silent narrowing. Caught by reasoning through the reduction rather than by a test, since the flat-list path
  is unreachable from the parser (which always yields one nested root). The dropped term now hands its `OR` to
  the next survivor.
- **Adding the shapes to the oracle suite found a second, unrelated defect — filed, not asserted.** A
  **computed** operand inside `Contains` (`x.Amount + 1`, `x.Score ?? 0`) is silently discarded and replaced
  by a fabricated subcondition, so `SomeIds.Contains(x.Amount + 1)` over a *non-empty* set answers 1 row where
  the truth is 0 — a wrong answer in the positive direction, pre-existing and independent (TASK-213). The
  parity case was rewritten over a plain column: encoding the broken behaviour to keep the suite green would
  have blessed it (the TASK-111 precedent). MongoDB's half-guard is TASK-212 — `RequireFilter` refuses only a
  *null* filter, so the shape this task spent its whole scope on is unguarded there, filed with its mechanism
  marked **unverified** because the MongoDB driver owns the translation and nothing in this repo settles it.

### Re-keying half a dictionary moved the collision instead of closing it (2026-08-14)

TASK-207, the residue TASK-129 filed rather than widening its own scope. `View.AddField`'s
`if (!table.Fields.ContainsKey(fieldName))` discarded any field whose key was taken — no column, no
exception, no log entry, the property reading back as `default(T)`. TASK-129 closed the aggregate instance by
keying aggregates on their view property and **left the guard**, which put two namespaces in one key space:
aggregates keyed by view property, non-aggregates beside them keyed by source column. Both surviving shapes
reproduce off the public fluent API and off the attribute builder — **6 of 7 first-pass tests failed against
unmodified code**. Every view field is now keyed by the property it populates. The standing rule is folded
into § Conventions' existing "one producer" entry above. Split: **7 of 9**. Three things worth carrying:

- **The task's own § Context under-counted the legitimate re-add paths, and that decided the design.** It
  named two; the load-bearing third is that `ViewAttribute` is `AllowMultiple = true`, so `LoadView` runs its
  whole per-property field loop **once per `[View]` attribute** — the ordinary way a three-table view
  declares its second join — re-presenting every field as a *fresh* `AbstractField`. § Approach's preferred
  option ("throw when the incoming field is genuinely different") would have broken every such view under the
  natural reading of "different". Fifth time in a month a prescribed remedy needed re-costing (TASK-111,
  TASK-112, TASK-117, TASK-129). **Enumerate the callers before choosing between report-it and prevent-it.**
- **Prevent beat report, and then both shipped.** Keying by view property makes the collision impossible;
  the throw stays as a backstop for `AddField`'s explicit `name` and for `AddTable`, because "I keyed it so
  it can't collide" is construction, not evidence — the same argument that put a test on `FlushDatabaseAsync`
  being off `ICache` (TASK-117). The backstop has its own test and its own opt-out test.
- **The fix landed in a file no spec area covers, exactly as the map predicted.** `View.cs` is in the 90% of
  `Birko.Data.SQL.View` the map excludes; its comment names TASK-129 and asks for a DECISION (TASK-208, still
  open). The new spec scenarios are grounded in `SqlViewTranslator.cs` instead and the backstop is left
  unspecced — the second consecutive task to hit this, which is the argument for deciding TASK-208 rather
  than routing around it a third time.

### An aggregate column had two names, so it got two aliases — and the quiet half lost a column (2026-08-14)

TASK-129. Every SQL view containing an aggregate generated
`COUNT(VOrders.PersonId) as COUNT AS "OrderCount"` — **two aliases on one column**, which SQLite rejects with
`near "AS": syntax error` and which is a syntax error on every other provider, so a persistent (or `Auto`)
aggregate view could **never be created**. The capability was unreachable, not degraded. Reproducing it turned
up a second defect with the same root cause and no filing: two aggregates of the *same* function collided on
their `Fields` dictionary key and the second was **dropped silently** — no column, no exception, no log entry,
the property reading back as `default(T)`. The standing rule is in § Conventions above. **Split: 15 of 17**
new-or-changed tests fail on a full revert. Five things worth carrying:

- **CR-L195 was right and only got two thirds of the way.** It decided an aggregate's identity is its view
  property and taught `GetPersistentViewSelectFields` and `ViewOrderFieldName` to read `field.Property.Name` —
  then aliased at its own emit site instead of at the producer, leaving `Table.GetSelectFields` still reading
  the dictionary key. Two producers of one name is what put two aliases on the column. The fix moves the read
  to the producer so a fourth consumer is correct without being told.
- **This task's own § Approach recommended the smaller fix, and the smaller fix was wrong.** It suggested
  parameterising an un-aliased projection so the shared `Table.GetSelectFields` stayed untouched — which
  closes the syntax error, leaves the identity split, and leaves the silent-drop defect entirely alive. Its
  *caution* was still correct about what to check: the on-the-fly path was verified to read **positionally**
  (`SqlViewStore.CreateTransformFunction` ignores the field-name map) and ORDER BY to match on
  `Property.Name`/`Name`, never the key — which is what made the larger fix safe. **Check what the written
  approach tells you to check, then re-decide the approach** — fourth time in a month a prescribed remedy
  needed re-costing (TASK-111, TASK-112, TASK-117).
- **The shipped test asserted the absence of the broken thing, and the broken thing passed it.**
  CR-L195's pin was `sql.Should().Contain("AS \"OrderCount\"")` — satisfied byte-for-byte by
  `as COUNT AS "OrderCount"`. It never executed the DDL. Its MSSql twin asserted `AS [OrderCount]` and did
  the same. Every DDL assertion now **executes** against SQLite and reads the created columns back out of the
  database, compared to what `GetPersistentViewSelectFields()` asks for rather than to a literal. Same lesson
  as the `b-chart` suite (2026-08-09): **assert the shape you want positively.**
- **Two tests changed classification by changing what they execute.** TASK-128's two
  `Persistent_aggregate_sort_*` were contract *pins* while their DDL was hand-written — they asserted a shape
  the generator could not produce, so no revert could touch them. Switching them to the generator (an
  acceptance criterion here) made both fix-dependent evidence. TASK-118 saw this in the opposite direction;
  the rule is that **classification follows what a test executes, not what it was written for.**
- **The fix landed partly in files no spec area covers, and `.map.yml` had predicted exactly that** — naming
  this task, and asking for a DECISION rather than a coverage fix. `ViewSelectSqlBuilder.cs` and
  `DataBase_View.cs` are in no area's globs, so the spec diff could not be evidence for the part of the
  change that lives there; a fix confined to those files would have produced a spotless diff. Left excluded
  and filed as TASK-208 (`assignee: human`) rather than silently widened — a note that gets quietly acted on
  stops being a decision. The residual silent-skip in `View.AddField` is TASK-207.

### "Clear the cache" deleted the database — by default, and the default had no default (2026-08-12)

TASK-117 / SH-H006. With no `KeyPrefix` configured — the **default**, since `RedisSettings.KeyPrefix` is an
unassigned `string?` — two doors in `RedisCache` targeted every key in the logical database, including the
queued messages and pending jobs of the siblings that share the connection by design
(`Birko.MessageQueue.Redis`, `Birko.BackgroundJobs.Redis`, the Redis sync stores). `ICache.ClearAsync` promises
to clear *the cache*; the implementation was wider than its own contract. Both doors now refuse with
`WholeDatabaseDeleteException` before opening a connection, and `FLUSHDB` lives on
`RedisCache.FlushDatabaseAsync` — off the `ICache` surface. The generalised rule is in § Conventions above.
Split: **9 of 27** new tests fail on a surgical reintroduction. Six things worth carrying:

- **The finding named the loud door and the quiet one was next to it.** `ClearAsync`'s `FLUSHDB` is what the
  finding described — but `FLUSHDB` is admin-gated by StackExchange.Redis (measured by reflecting the shipped
  2.8.41: `Message.IsAdmin` is `true` for `FLUSHDB` and `KEYS`, `false` for `SCAN`/`DEL`), and
  `GetConnectionString()` never emits `allowAdmin=true`, which nothing in `Framework`, `Framework.Tests` or
  `Consumers` sets. So on a settings-built cache that branch **threw** rather than flushing. The door that
  destroyed data silently on every configuration was `RemoveByPrefixAsync("")` — `SCAN "*"` + `DEL`, neither
  gated — found while re-verifying and filed as *secondary*. It was primary. Two lessons: **a defect's
  reachability depends on the client library's own gates, not only on the code path**, and the ranking
  rationale ("the default path destroys and reports success") was right about the defect and wrong about which
  command did it. Both were recorded, then corrected.

- **The finding's preferred remedy was unimplementable, and the reason is worth more than the fix.** It asked
  for an unprefixed clear to delete "this cache's entries and leave other keys intact". That set does not
  exist: an unprefixed cache writes bare keys, so they are byte-for-byte indistinguishable from every
  sibling's, and two unprefixed caches on one database *are* the same key space. Every way to invent one was
  worse — an owned-key index needs a key name (the layout change the finding's option 2 was rejected for),
  costs a round-trip per write, and grows without bound because Redis expiry does not remove members; while
  scanning a made-up prefix finds nothing and turns the clear into a **silent no-op reporting success**. The
  option the task ranked *last* was the only one that is neither destructive nor a lie. **When a finding
  prescribes a remedy, check the remedy is possible before costing it** — third time in a fortnight (TASK-111
  rejected "resolve and quote", TASK-112 found the mapping already built).
- **The documented door was not the only door.** `RemoveByPrefixAsync("")` reached the identical
  whole-database delete by scanning `"*"` instead of `FLUSHDB` — same root cause, one function away, not in
  the finding. Fixed together through one resolver. Findings travel in packs, and the pack members are
  usually in the same file.
- **Widening the guard meant narrowing what it fires on.** Guarding "no `KeyPrefix` configured" would have
  refused `RemoveByPrefixAsync("user:")` on an unprefixed cache — bounded and legitimate. The guard is on the
  *resolved scope* (`"*"`), not on the configuration that produced it. A refusal must not fire on the case it
  was never about.
- **`verify-conventions` found the register-on-introduce gap, and its own step 0 is why.** This is the second
  instance of one guard (after `WholeTableWriteException`) and § Conventions recorded it only in SQL terms —
  precisely the TASK-111 lesson arriving again, twelve days later, in a different backend. The frozen
  checks 1–10 could not have caught it; the live rulebook sweep did. Check 5 then caught that a ticked
  criterion ("`FLUSHDB` not reachable from `ICache`") rested on construction rather than a test.
- **The close gate then found a one-character bypass of the new guard, and the split had to be re-derived
  three times.** `security-review` could not run (no `origin/HEAD`, and the production change is in a sibling
  repo no skill in this repo can diff), so the pass ran **inline** — which is the only reason the question
  *"can this guard be walked past?"* got asked. It could: the literal prefix went into a Redis `MATCH` pattern
  unescaped, so `RemoveByPrefixAsync("*")` resolved to `"**"` — non-null, past the emptiness check, and
  matching **every key in the database**. A `KeyPrefix` of `"*"` did it to `ClearAsync` via `"*:*"`. The same
  escaping fixed a latent read/write disagreement, since `GetFullKey` always wrote metacharacters as literals.
  Two things generalise: **when a review skill fails to resolve, run the pass by hand — the gate is not
  optional**, and **a scope guard's own test is whether a caller can widen the scope back**, not whether it
  fires on the reported input. The split went 5 of 13 → 5 of 16 → **8 of 25**; the first number would have
  understated the check by three tests and the fix by a whole defect. Final: **9 of 27**, after the review's
  findings added two more.
- **The regression suite for a destructive-clear defect was itself destructive.** The tests asserting a call is
  *not* refused must run past the guard, so they reached `GetDatabase()`/`GetServer()` — and pointed at
  `localhost:6379` they issued real `SCAN test:*` + `DEL` and `SCAN user:*` + `DEL` against database 0. On any
  developer box with a local Redis they were deleting live `user:*` keys, and
  `NotThrowAsync<WholeDatabaseDeleteException>` swallowed every trace. They now point at TEST-NET-1
  (`192.0.2.1`, RFC 5737, never routable). The tell was in plain sight and nobody read it: suite runtime went
  **36s → 800ms** once the connections stopped. **A "not refused" assertion is an instruction to execute the
  dangerous path** — give it somewhere harmless to execute, and treat a slow offline suite as evidence it is
  not offline.
- **A `NotThrowAsync<TSpecific>` assertion passes on every other exception.** `FlushDatabaseAsync_IsNotItself
  Refused` claimed to prove "the escape hatch opens" while asserting only that one exception type was absent —
  so it passed identically whether the flush worked, hit the admin gate, or never connected. Narrowed to what
  it can actually see, with the real property (the admin precondition) pinned separately and by measurement.

### A rule field was executable SQL, and the second sink proved the first one's rule (2026-08-12)

TASK-111 / SH-H023. `RuleConditionConverter` made a rule's `Field` into the condition's **name**, and every
strategy interpolates that straight into `CommandText`. Measured against SQLite on a 3-row table, with rules
whose value matched nothing: `Rank OR 1=1 --` returned **3 rows of 3**, and
`Rank; CREATE TABLE Pwned (x INTEGER); --` **created the table** — from configuration data that
`docs/rules.md` advertises as a way to build "dynamic filtering from user-defined rules". Fields now resolve
against table metadata via new type-aware overloads (`ToConditions<T>`), which also fixes the ordinary half:
a `[NamedField("label_col")]` property emitted `WHERE Label = @p` and the database answered *no such column*,
so a remapped property could not be filtered at all. The standing rule is in § Conventions above.

Four things worth carrying:

- **The second instance is what turns a fix into a rule.** TASK-110 closed the identical defect on ORDER BY
  twelve days earlier and recorded its reasoning beautifully — in a commit message and a doc comment, which
  is exactly where the *next* sink's author will not look. Two sinks, one root cause, and no § Conventions
  entry between them. The shared `ResolveFieldNameIn` and the rule above exist so the third sink is a
  compile-time reuse rather than a rediscovery. **When a fix is the second of its kind, the deliverable
  includes the rule, not just the fix.**
- **The prescribed remedy was wrong, and the closed twin was the evidence.** The finding said "resolve
  **and quote**". Quoting would have broken working filters on PostgreSQL, where an unquoted DDL identifier
  folds to lower case — TASK-110 had already measured this and rejected it. Following the finding would
  have shipped a regression while closing a hole. Read what the twin decided before re-deciding it.
- **A test tripped over a second, unrelated defect, and the honest move was to not assert it.** The
  end-to-end OR-group check returned 0 rows where 2 were expected — SH-M128, already filed, different root
  cause (`ConvertGroup` wraps with `AndSubCondition`). Asserting the OR result would have had to encode the
  broken behaviour to stay green, which blesses it. The test uses an AND group and says why.
- **Full revert would have reported a fraction of the check.** Most of the new tests reference the new
  type-aware overloads and would not compile against the pre-fix tree, so a plain revert would have hidden
  them behind a build error — the TASK-204 trap arriving again. Reintroducing the defect *surgically* (one
  line in `ConvertLeaf`, every signature intact) gave a real split: **42 of 55**.
- **The split was then reported stale, and `/code-review` caught it.** The first recorded numbers ("34 of
  42", plus two mutually inconsistent totals) were taken *before* the security pass added four payload
  cases, and were carried by hand across three edits without being re-run. **A red-verify split expires the
  moment the suite changes** — re-derive it as the last step before reporting, never carry it forward. Two
  further defects in the fix came out of the same review: rules over a `[View]` type threw on the first
  call in a process (the resolver was registered only inside `LoadView`, while rule fields resolve at the
  caller — now a `[ModuleInitializer]`), and the "don't quote, for PostgreSQL" rationale was recorded in
  three places without noting that it covers the *column* and not the *table qualifier*.

### One index it could not build took down six entities' read surfaces — and the fix leaked per request (2026-08-12)

TASK-204. A duplicate `(TenantGuid, OrderNumber)` pair made a later-declared UNIQUE index unbuildable, and
because schema-ensure runs lazily and sets `_initialized` only on success, the store never initialised and
**re-threw on every later operation** — reads included, on six entities in consumer Symbio, permanently. An
unbuildable index is now recorded (`IndexCreationFailures` / `OnIndexCreationFailed`) instead of thrown; the
two standing rules that came out of it are in § Conventions above. Four things worth carrying:

- **The read surface is what makes repair possible.** The old behaviour was self-sealing: you could not read
  the duplicate rows to delete them, because reading them ran the schema-ensure that refused. Degrading the
  index kept the door open, and the fix's own recovery test walks through it — read, delete the duplicate,
  re-init, index builds itself with no restart.
- **The fix had the same class of bug as the defect.** An append-only failure `List` on a **process-cached**
  connector, fed by a **per-store** init flag, grew one entry per HTTP request and re-raised its event each
  time. Measured 5 stores → 5 entries and 5 re-executed failing DDL statements. Lifetime mismatches between
  a cached collaborator and its per-instance gate are worth checking whenever you add state to a connector.
- **Only two of the original five tests were evidence.** The other three referenced the new API and so could
  not compile against the reverted fix — pins, not proof. Counting them as red-verified would have reported
  five-fifths confidence for two-fifths of a check.
- **Found by diffing the working tree, not by a failure.** This arrived as three modified files with no
  commit, no test and no task — the third such find in a week after TASK-197/198. Nothing in this repo knew
  it existed, and nothing would have.

Note: `Recent Updates` is well past the ~5–8 entry threshold and is overdue a `/roll-changelog` pass.

### Six fixes that were written but never committed — and the false premise that kept them there (2026-08-09)

A sweep of the sibling repos found six real fixes sitting in working trees or landed without their
aggregator commit. All are now committed with framework-side regression coverage: `TimeOnly` mapping
(**TASK-197**, `b0dec59`), the `.Date` predicate rewrite (**TASK-196**, `f3cdf99`, landed 2026-08-08 with
no tracking at all), and four in the `Birko\Web` bucket — `b-chart`'s span-aware time axis (`a2521ce`),
`FormControlComponent`'s `willValidate` guard (`5a94c59`), `BaseCrudPage._openEdit`'s pre-fill window
(`352e198`) and the `setChecked` page object (`69e0583`), with Playground coverage in `c285e48`.

The individual defects are in their task files. What generalises:

- **A false premise about tooling can quietly replace version control.** The four Web fixes were
  uncommitted because a consumer's `docs/birko-framework-fix-prompts.md` recorded, twice, that *"the
  framework repo has no git"* and reproduced the diffs as prose on that basis. All four packages are
  ordinary git repos (Components 165 commits, Shell 59, Core 53, Testing 2). For two days a consumer's
  docs folder was the source of truth for framework code, and Symbio's committed `wwwroot/app.js`
  shipped a bundle whose sources existed in no repository. Nothing was broken and nothing failed —
  the only symptom was a note explaining why it had to be that way. **Run `git log` before concluding
  a repo is unversioned;** the cost of being wrong is invisible until a restore.
- **A polyrepo fix that stops at two commits looks finished.** `.Date` had its production fix and its
  regression suite, both excellent, and no aggregator commit — so no task, no `pr:` sha, no spec regen,
  and nothing in this repo knew it existed. It was found by diffing sibling `git log` against this
  repo's HEAD, which is now the only reliable way to notice: the third commit is the one with no
  compiler or test to demand it.
- **"We enumerated what this will break" was not true.** `TimeOnly` was on neither TASK-112's
  CLAUDE.md note nor TASK-150's list of remaining unmapped types, and it is the one that reached a
  consumer. An inventory of what the *mapper omits* is worth less than an inventory of what *consumer
  models declare* — grep the consumer trees, don't reason from the dispatch.
- **A wrong finding id survives every check a compiler can make.** The `TimeOnly` code cited SH-H038,
  which is an unrelated ElasticSearch reindex finding; it came in with the working-tree comments and
  was carried into two commit messages before anyone compared it to the register. It points every
  "which findings are closed" sweep at the wrong defect.
- **Two regression suites passed against the wrong thing, in opposite directions.** The `b-chart`
  checks set `type: 'line'` via `setOptions` — but `type` is an *attribute*, so the default **bar**
  renderer ran and its thinned category labels (`"0"`,`"3"`,`"6"`…) satisfied a "no label is a clock"
  assertion; three checks passed against a chart that was never a line chart. Assert the shape you
  want **positively**, never the absence of the broken one. And the `willValidate` checks used only
  `b-input`, which reports no validity flag while disabled, so they passed with the fix reverted — the
  throw needs `b-select`/`b-textarea`. **One representative is not a suite**, and picking the wrong one
  gives a green run over a live crash.

### Five CLR types that mapped to no column at all — and the mapping that was waiting for them (2026-08-08)

TASK-112 / SH-H037. A `[Table]` model with a `long`, `short`, `double`, `float` or `byte[]` property got a
`CREATE TABLE` **without that column**: the value was dropped on every save and read back as the type's
default, with no exception and no log entry. `decimal` *is* mapped, so money was safe — which is precisely
why it survived; what vanished were identifiers, measurements and blobs. Five new `AbstractField`
subclasses and their dispatch arms fix it, and an unmappable type now throws instead of disappearing (the
standing rule is in § Conventions above).

**⚠ Consumers: this can break a running deployment.** A consumer whose model already carries a `long` has a
live table with no such column. Adding the mapping means their DDL and their live schema now disagree, and
a read will *fail* rather than silently return zero. Migrating those tables is deliberately out of scope —
it is a consumer decision. The same applies to any model with a property the mapper still cannot express
(`char?`, `TimeSpan`, `DateTimeOffset`, collections): those now throw at table load where they previously
loaded fine minus a column. `[IgnoreField]` / `[NotMapped]` is the opt-out.

Four things worth carrying past this mapper:

- **The finding was right and its cost model was wrong — check both.** The task called the per-provider type
  mapping "the bulk of the work". All four `ConvertType` implementations **already** had `Int16` / `Int64` /
  `Single` / `Double` / `Binary` arms emitting exactly the requested types, `AddParameter` binds untyped, and
  `ModelMap<T>` does no type dispatch at all. The mapping had been built for a `DbType` the dispatch could
  never produce. Scope went from four providers to one method plus five small classes; following the written
  approach would have meant a large pointless change.
- **A test that builds the object under test by hand cannot witness a dispatch fix.** Step 6's first run had
  **all 15** per-provider DDL tests passing with the fix reverted — they constructed `new LongField(...)`,
  and the field classes survive a revert that only touches `CreateAbstractField`. They were pinning the
  providers' `ConvertType` contract while looking like evidence. Driving `DataBase.LoadTable(typeof(Model))`
  instead took the provider suites from 0 → 15 failing, and the total from 28 → **43 of 52**.
- **Fixing an inert defect is cheaper than filing it.** SQLite mapped `DbType.Single` → `INTEGER`, grouped
  with the integral types — the identical mistake PostgreSQL and MSSql had both already fixed under CR-H087.
  It was unreachable because nothing could produce a `Single` field; the moment `float` mapped, the reference
  and test provider would have been the one declaring a float column as an integer.
- **A silent skip and a wrong answer are the same bug.** `char?` was never in this task's scope and is still
  unmapped — but the fail-fast changes it from *silently dropped* to *reported*, which surfaced a latent
  instance of the very defect being fixed. Pinned by a test and specced rather than quietly mapped.

### Birko.Web: a cascade invariant that needed asserting twice, and two half-fixes (2026-08-04)

A review of what landed in the `Birko\Web` bucket over 2026-08-02…04. Three shipped fixes, one in flight; the
per-component detail is in `Birko.Web.Components`/`.Shell`'s own CLAUDE.mds. What generalises past the web
tree:

- **A style invariant that must hold in both trees needs a rule in both sheets.** `[hidden] { display: none
  !important }` now sits in `reset.css` *and* in `BasePage.styles` — `BasePage.styles` is adopted into a
  shadow root and `reset.css` styles the document, so neither reaches the other. The UA's `[hidden]` rule
  ties on specificity (0,1,0) with any class selector, so an author `display` declaration silently un-hid an
  element the code believed was gone: it shipped a four-week date range recorded as **one day**, with a
  success message, under a fully green suite. `!important` because a specificity bump only outranks the
  selectors that exist today. **Assert computed style, not the attribute** — an attribute assertion was green
  throughout the original defect.
- **Two layers sizing themselves in different viewport units is a defect even when both units are correct.**
  `BCoreAppShell` sized itself in `dvh`; `reset.css` kept `body { min-height: 100vh }`. `vh` is the *large*
  viewport, so on Android Chrome in a tab the body was taller than the shell by exactly the URL-bar height
  (56px), giving dead scroll that dragged the bottom nav behind the system bar. Neither declaration is wrong
  in isolation; the disagreement is the bug. And it **cannot be reproduced headlessly** — with no retractable
  chrome `vh == dvh` — so the guard asserts the declaration and labels itself as not a reproduction.
- **A regression test the fix's own revert cannot break is not evidence.** Two of the three fixes shipped as
  half-fixes for exactly this reason. `b-segmented`'s touch floor cited a 44 × 44 target and floored only
  *height*, and the test asserted height; the consumer's suite ran a locale whose labels all cleared 44px
  from padding alone, so no label short enough to fail existed. Reviewing the in-flight
  `Surface.alsoMatches` change found the same shape: its "first match wins" check passed with the fix
  reverted, because the fallback chain's last resort is `surfaces[0]` — the same answer it asserted. Both are
  fixed and red-verified (3 of 6 nav checks now fail on revert, up from 2).
- **A shared component's regression test belongs in the framework, not only in the consumer that reported
  it.** The half-floor's only coverage lived in the consumer for two months, which is how a single-locale
  suite was able to bless it. The framework check now includes the inverse case (a *fine* pointer stays
  dense), since a fix that floored every pointer type would have passed the original assertion and silently
  resized every consumer.

### A tenant-scoped sync that only filtered its writes — and deleted other tenants' rows (2026-08-03)

TASK-113 / SH-H050+H051+H052, three findings with one root cause. `TenantSyncProvider.ApplyTenantFiltering`
wrapped `CanSaveToLocal`/`CanSaveToRemote` and **said so in its own XML doc** — *"only modifies save filters,
not fetch predicates"*. Everything else followed from that sentence being true: every tenant's rows entered
`localDict`/`remoteDict`, so a preview under tenant *t* enumerated and version-hashed another tenant's
entities, their guids went into the knowledge store, and the `SyncAction.Delete` arm — the one arm that
consulted no predicate at all, unlike Create/Update/conflict-resolution — **deleted them**. The tenant term
now goes on the fetch predicates and one `ResolveTenantScope` answers "which tenant" for the whole run. The
two standing rules this produced are in § Conventions above; four things worth carrying past this provider:

- **A silent no-op beats a wrong answer only if someone notices.** The worst of the three was the *documented*
  call shape: `SyncAsync(new TenantSyncOptions { TenantGuid = u })` from a background job with no ambient
  tenant keyed knowledge to *u* while `if (_tenantContext.HasTenant …)` installed **no write filter at all**,
  copying every tenant's items into both stores. The configuration the docs recommend was the one that failed
  hardest, and it reported success.
- **Widening the guard's reach meant narrowing what the API accepts.** Ambient *t* plus an explicit *u* now
  **throws** instead of resolving by precedence, and a tenant-scoped entity with no tenant anywhere throws
  instead of syncing everything. From first principles the explicit option is the more specific instruction
  and should win — rejected, because that is code running in *t*'s scope reaching *u*, the same shape as
  SH-H048, and a silent winner makes it unobservable. The deliberate cross-tenant caller says so out loud.
- **A refusal must not fire on the case it was never about.** The missing-tenant throw applies **only** to
  entity types that declare `TenantGuid`. This provider legitimately serves models without one — two
  pre-existing regressions use exactly such a model — so an unconditional throw would have broken working
  behaviour rather than closing a hole. Fail-closed still has to know what it is closing.
- **The tests that passed before the fix are the informative ones.** 14 of 37 failed on the revert;
  `Sync_DoesNotCopyAnotherTenantsRowsIntoTheLocalStore` passed, because the write filter was the single path
  the old code *did* scope. Recorded as a contract pin, not as evidence — a pin logged as proof is how the
  next reader concludes a fix was verified when it wasn't.
- **The review found a defect in the fix, and then the revert found a defect in that fix's test.**
  `code-review` caught that scoping the fetch predicates wrote them back onto the **caller's**
  `SyncFilterOptions`, so the per-tenant admin loop — reusing one instance, the shape the README had just
  blessed — would carry `t1 && t2` on iteration two and silently sync nothing (fail-closed, so it would have
  read as "tenants stopped syncing", never as a leak; third time this file has been bitten by writing to a
  caller-owned object, after CR-M168). Its regression test then **passed the pre-fix revert**: asserting only
  the loop's *end state* ("both tenants present") cannot distinguish two correctly-scoped iterations from one
  unscoped iteration that copied everything. Asserting after *each* iteration fixed it. **Re-run step 6 when
  a later step adds a check** — a test written to pin a fix is not automatically evidence of it.

Also: `/specs regen` for `tenant-isolation` deleted three scenarios that **asserted the defects** as shipped
behaviour (`Both stores are read across all tenants`, `Deletes bypass the save predicates entirely`, `A
caller-supplied TenantSyncOptions is mutated in place`) — the ordering constraint the spec harvest warned
about, arriving exactly as predicted. And the `shaped-by` evidence pass **cannot run from this repo at all**:
every `tenant-isolation` source glob points into a sibling repo, so no task's `pr:` sha resolves under
`git show` here. That is true of every area in this aggregator's spec tree, not just this one.

### The tenant guard was on a transport, not on the tenant — and its own correction was wrong (2026-08-02)

TASK-118 / SH-H048. The guard shipped on 2026-07-28 compared one hard-coded `X-Tenant-Id` header against the
JWT `tenant_id` claim. Every other door was open: `TenantQueryStringKey`, `TenantRouteKey`, both
custom-resolver hooks, `SubdomainTenantResolver`, and — the quiet one — a **renamed**
`TenantMiddlewareOptions.TenantHeaderName`, which made the guard stop working with no error on a deployment
that looked correctly configured. A caller authenticated in tenant A reached tenant B's reads *and writes*
with their own permissions intact, and nothing failed or logged. Both resolving middlewares now publish their
result as a `ResolvedTenant` and the guard checks that, so a source added later is covered without editing it.

Four things worth carrying past this middleware:

- **A correction to a finding can be the thing that's wrong.** Both the filed finding and this task instructed
  the fix to record that "no `RouteValues` tenant source exists". It exists — `TenantMiddleware.cs:111-120`.
  Following the correction would have deliberately left a live, unguarded source out of a security fix. The
  usual step-3 failure is a finding that overstates; this one *understated*, via its own verification pass.
  Re-verify the corrections too, not just the claims.
- **Where the resolution travels decides whether the guard fails open.** Reading the resolved tenant off
  `ITenantContext` covers every source and is simpler — and would fail open, because `UseTenantMiddleware`
  binds its context from the root provider (SH-H049) while the guard resolves one per request, so under
  `AddTenantContextScoped()` they are different objects and the guard sees no tenant. It goes on
  `HttpContext.Items` under a **fixed** key: `TenantContextKey` is configurable, and a guard keyed on a
  configurable name is defeated by the same class of config change as the hard-coded header constant was.
- **Widening a guard can narrow it.** Replacing the literal `X-Tenant-Id` check with the resolved-tenant check
  would be cleaner and would have been a *coverage regression* for any app that never wired a tenant
  middleware but reads the header in its own code — the premise the original guard was written on. Kept both.
- **The revert reclassified one of the tests.** `SystemScopeToken_CannotAddressARealTenant` was filed as a
  contract pin and failed on the step-6 revert: it reaches the victim through the query string, so it was
  never pinning old behaviour. Split: **9 of 16 failed**, 2 more don't compile pre-fix, 7 genuine pins. The
  guard had also shipped with **zero tests** — this is its first suite.

### Shadow depth per theme, and the generator drift that had been running for two days (2026-08-02)

Started as a question — *should shadows be distinguishable on dark / neon / inverse?* — asked while trying
the new global theme switcher in `Birko.Web.Playground`. Measuring rather than eyeballing answered it and
turned up two defects and a process hole.

**The scale, not the depth, is the contract.** `sm`→`lg` on the dark themes were already fine: they raise
alpha from light's 0.05–0.1 to 0.3–0.6 and measure on par with light. But **`dark`/`neon`/`inverse` had
never overridden `--b-shadow-xl`**, so it fell through to the light `:root` value (0.1/0.04) and measured
**7 / 4 / 9** against those themes' own `--b-shadow` at **13 / 10 / 15** — the ladder inverted at its
deepest rung, and `xl` is the level *every* overlay uses (`b-modal`, `b-drawer`, `b-confirm-dialog`,
`b-command-palette`, `b-tour`). Now 33 / 25 / 37. `finstat` had the same inversion one rung lower
(`md` > `lg`, from a 1:1 mapping to the legacy `@box-shadow-preset`); where a brand mapping contradicts the
ladder, **the ladder wins** — `md` and `xl` still map 1:1.

Three things worth carrying past shadows:

- **A metric that is too narrow invents defects.** Judged on peak pixel, finstat's `xl` looked broken too.
  It isn't: it is deliberately wide and diffuse (`0 20px 70px -25px`), reads as the theme's deepest level,
  and only measures a low peak. Switching to *ink* (delta integrated down the column) cleared `xl` and kept
  `lg` condemned. Assert the ordering; the absolute number is decoration.
- **A question can be correctly closed with "no change".** `dark`/`inverse` set `--b-bg-secondary` ==
  `--b-bg-elevated`, so a `b-card` has zero surface step — which sounds like a defect until measured: the
  card edge reads at **26 / 23** against light's **22**, because the 1px border does the work. Nothing
  changed, and a permanent check now guards the property that made "no" the right answer.
- **Some defects have no DOM to assert against.** `--b-shadow-*` has no element, no ARIA, no geometry, so
  `verify.mjs` and every in-page smoke suite are blind to it *by construction*. It has to come off rendered
  pixels — screenshot, hand the PNG back into the page, sample a canvas. Now a permanent group in the
  playground's `device-fix-check.mjs` (63 checks, up from 47), verified to fail by reverting both fixes.

**The process hole is the more valuable find.** `verify` was **already red before any of this work**:
`c97d9bd` and `e07f9d3` had written real fixes straight into the *generated* CSS — the four dark
`--b-color-*-light` tint fixes and `--b-split-detail-sticky-top` — so `generate` would have deleted them,
and did, until they were restored and recovered with `extract`. That recovery exposed a live bug nobody
could see: the **Avalonia dark dictionary was still serving the light pastel tints** (`#DCFCE7`/`#FEF3C7`/
`#FEE2E2`/`#CFFAFE`) under near-white text — the very defect the web side fixed on 2026-08-01 — because
AXAML is generated from `tokens.json` and agreed *perfectly* with a stale source.

- **`verify` answers "does the output match the source", never "is the source still true".** A green run is
  evidence of consistency, not correctness. It flagged the two CSS files and passed the AXAML that was
  actively wrong.
- **Only the CSS has ever drifted, and only the CSS lacked a banner.** Every AXAML dictionary opens with
  `AUTO-GENERATED … DO NOT EDIT`; `tokens.css` opened straight into `:root {` and `dark.css` opened with a
  prose "how to use this theme" comment that reads exactly like a hand-authored file. All five sheets now
  carry the banner (in `Sheet.prologue`, so the verbatim round-trip keeps it with no emitter change), with
  `CssParityTests.Every_sheet_declares_itself_generated` per sheet.
- **The banner is a human signal; the gate is CI, and there was none.** `tokens.json`, the CSS, the AXAML
  and the parity suite live in **four separate repos**, so no single checkout can run the gate — which is
  exactly why an editor's diff, review and test run never mention `tokens.json`. A `token-parity` workflow
  now exists in **all four**: a gate that only fires on the source cannot catch an edit made to the output.
