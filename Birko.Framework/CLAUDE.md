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
  - **Where the backend hands the predicate to a driver, the scope test goes on the EXPRESSION** —
    `Birko.Data.Expressions.PredicateScope` (`ReducesToAllRows` / `IsExplicitAllRows`), consumed by
    `AbstractBulkStore.RequireBoundedFilter` and its async twin. Fourth instance of this family, and the one
    that shows the rule is not about SQL at all: MongoDB renders `!empty.Contains(x.F)` as
    `{ "F": { "$nin": [] } }` — a **one-element** document that matches everything while looking like an
    ordinary field predicate, so the obvious guard ("refuse an empty filter document") never fires, exactly as
    "refuse when nothing was rendered" never fired on `1 = 1`. Guarding the C# expression is also
    translation-independent: `!empty.Contains(x)` is true of every entity whatever the driver later emits.
    `WholeTableWriteException` lives in `Birko.Data.Core/Exceptions/` (beside `StoreException`, same
    `Birko.Data.Exceptions` namespace) precisely so one `catch` selects the refusal on every backend — do not
    invent a per-backend exception. **The guard is available to every store but must be WIRED per backend
    after measuring that the shape reaches a destructive path there**; wiring it blind is how a refusal ends
    up firing on a case it was never about. And keep the analyser narrow — it answers "no" when it cannot
    prove a predicate unbounded (a per-entity collection, a null or unevaluatable one, any string `Contains`),
    because a false refusal breaks working code and is worse than the hole.
  - **"Wire it per backend" does not mean "wire it only in backends" — the shared base is itself a
    wiring site, and it is the one that was missed.** TASK-212 put `RequireBoundedFilter` *on*
    `AbstractBulkStore` and wired it into MongoDB's four overrides; nobody wired it into the base's **own
    six** filter-based destructive wrappers, which are read-then-loop and therefore the purest instance of
    the defect. Measured on `JsonStore`, which overrides none of them:
    `Delete(x => !empty.Contains(x.Value))` left **0 of 3** rows, no exception — so the hole was live on
    every portable backend (JSON, XML, RavenDB, CosmosDB, InfluxDB), none of which the finding named.
    Three parts generalise (TASK-215):
    - **A per-backend rule still has to be applied to the layer the backends inherit.** "Measure before
      wiring" is about not guessing whether a shape reaches a destructive path — it is not a licence to
      skip the one implementation every unlisted backend runs. Where a guard's helper and its callers live
      in the same class, check the callers in that class *first*.
    - **Guard the whole verb family or none of it.** InMemory overrides `Delete(filter)` but inherits all
      four `Update(filter, …)` paths, so fixing only the filed overrides would have shipped a store whose
      `Delete` refuses beside an `Update` that rewrites every row. The scope of a guard is the set of
      methods that reach the same destruction, not the set of methods a finding happened to list.
    - **A refusal names the door THIS caller has.** The async twin threw the shared message naming
      `DeleteAll()`, which an async store does not have — an opt-out that does not compile, § SH-H037's
      rule in its quietest form. `WholeTableWriteException`'s scope constructor now takes the door name;
      async passes `DeleteAllAsync()` / `UpdateAllAsync(updates)`. **Cost of the base wiring, recorded
      because it is not free:** `PredicateScope` evaluates the collection operand, and
      `Update(filter, PropertyUpdate)` guards then delegates to `Update(filter, Action)` which guards
      again, so a side-effecting operand is now evaluated three times instead of once, on every portable
      backend. Both guards are kept deliberately — each covers a distinct partial-override case, and the
      placement mirrors `RequireFilter` — but put captured collections, not method calls, in a filter.
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
- **A qualifier resolves against a bare ALIAS, not against a quoted table — and that is what makes the rule
  above total instead of per-sink.** Fifth instance of the identifier family (TASK-211), and the one that
  showed the previous four were the visible corner of it. Every read this framework builds qualifies its
  columns — `Table.Column` from `GetSelectFields(withName: true)` for the projection, from
  `ResolveColumnName(…, withTableName: true)` for the `WHERE`, and the same for `GROUP BY` / `ORDER BY` /
  a join's `ON` — while `FROM` quoted its table. On PostgreSQL the bare qualifier folds and the quoted
  relation does not, so **every read of every PascalCase-named entity returned zero rows, silently**: not
  views, *everything*, `Read()` included, reaching consumer Symbio through
  `TimescaleDBConnector : PostgreSQLConnector`. `CreateSelectCommand` now emits
  **`FROM "Widgets" AS Widgets`** — quoted relation, bare alias — via `SelectTableReference`. Three parts
  generalise:
  - **Fix it where the identifier is RESOLVED, not at each producer, when the producers are open-ended.**
    TASK-209 could thread a `quoteTable` delegate because the view DDL has one metadata-driven producer.
    The read path does not: a qualifier can arrive function-wrapped (`LOWER(T.Col)`, `COALESCE`, the `.Date`
    rewrite), so quoting each producer means enumerating them, and **a producer missed is the identical
    silent empty result** — which is exactly how this survived four previous tasks in the same family. One
    alias makes every qualifier correct by construction, including the ones nobody has written yet. It also
    keeps `ParseConditionExpression` provider-independent (§ TASK-137's rule), which the alternative would
    not: the parser has no connector and must not acquire one.
  - **Never quote the alias.** A quoted alias is case-sensitive again and the bare qualifiers stop matching
    it — the fix would silently undo itself. And a name that cannot take a bare alias (spaces, punctuation,
    a reserved word) is emitted **unaliased**, because such a table already cannot be read through a
    qualified SELECT on any provider (measured: `SELECT Order.Guid FROM "Order"` is a syntax error with or
    without the alias) while an unqualified `SELECT COUNT(*)` over it works today and must keep working.
  - **A write drops the qualifier instead, because it can.** `DELETE FROM "T" WHERE T.Col = $1` failed
    identically on PostgreSQL, but the alias does not port — MSSql rejects `DELETE FROM t AS a` — so
    `AddRequiredWhere` strips the target table's qualifier (`StripTargetTableQualifier`, TASK-216) and emits
    `WHERE Col = $1`. A write targets exactly **one** table, so the qualifier carries no information there
    and a bare column cannot be ambiguous. Quoting it instead would have made the write path the only place
    a *qualifier* is quoted while reads resolve theirs against a bare alias — two conventions for one thing,
    which is the shape this family keeps arriving in. **One invariant now holds framework-wide: a qualifier
    is only ever emitted where a bare alias introduces it.** Three parts generalise:
    - **`AddRequiredWhere` is the funnel, and that it has exactly four callers — all writes — is why the fix
      is four lines.** Reads use `AddWhere`. Before inventing plumbing, check whether the parameter you need
      (here the target `tableName`) is already being passed to a method only the affected paths call.
    - **Rewrite the rendered clause, never the caller's `Condition` objects.** A qualifier arrives
      function-wrapped — `LOWER(T.Col)`, `COALESCE(T.A, T.B)`, and the `.Date` rewrite's
      `(T.Seen >= @a AND T.Seen < @b)`, all measured — so per-name rewriting misses exactly the shapes a
      partial fix always misses; and this file has been bitten three times by writing to a caller-owned
      object (CR-M168, TASK-113).
    - **Guard the left edge of a textual identifier rewrite.** With target `Person`, a naive replace of
      `Person.` turns a *different* table's `MyPerson.Col` into `MyCol` — a column that does not exist, i.e.
      a silently wrong statement instead of a loud one. `(?<![A-Za-z0-9_."])` is the guard, and it has its
      own test. Check the parameter names too before choosing a textual rewrite:
      `SqlBuilderContext.GenerateParameterName` sanitizes with `[^a-zA-Z0-9_]`, so `@WHEREPersonName0_0`
      carries no `Person.` to strip — had it kept the dot, this approach would have broken every
      parameterized filter.
- **A reader that answers an ERROR with an empty result is giving a wrong answer, so what it swallows must
  be exactly one thing.** The second half of TASK-211, and the reason the first half was invisible for the
  whole life of the framework. `IsMissingTableException` decides whether `RunReaderCommand` yields nothing
  instead of faulting, and every provider had widened it past its own name: PostgreSQL accepted **any**
  `42P01` — which is also *"missing FROM-clause entry for table x"*, an error about the **statement**, where
  the relation exists perfectly well — plus a bare `Message.Contains("does not exist")` that additionally
  covered undefined column (measured: `SELECT NoSuchColumn FROM "T"` → empty, no exception), function and
  type; MySQL had the same shape via `"doesn't exist"`. **The swallow hid the very defect that produced the
  error it swallowed.** Now the SQLSTATE is the primary key and the message separates only the two shapes
  that share `42P01`; the untyped fallback is kept (it is a shipped contract with tests) but narrowed to the
  provider's *relation* / *table* wording. Three things travel with this:
  - **The legitimate case is a genuinely absent relation** — lazy create-on-first-use and view-existence
    probing (CR-M149) depend on it, so narrowing must not close that door; it has its own test, and it is a
    **contract pin that passes either way**, which is what a guard's opt-out test is for.
  - **`OnException` is the same decision wearing different clothes.** PostgreSQL's and MySQL's handlers ran
    `DoInit()` and **returned** on the same substring, i.e. reported success for a statement that never ran —
    that is what let `CreateView` swallow `42P01` and report success (TASK-209). Both now call the reader's
    own predicate, so the two cannot disagree about what "the table is missing" means. **One producer**, the
    same rule as the entries above, applied to a classification instead of to a name.
  - **A narrowing will break tests that assert the wide behaviour, and those are the interesting ones.**
    Two suites failed on exactly the removed catch-all. The fix was not to restore it but to ask what signal
    the message actually carries: `relation "x" does not exist` names a *relation*, so requiring that word
    keeps the fallback and excludes column/function/type. **Narrow on the signal, don't delete the seam.**
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
- **Where a driver has no usable default, the framework picks one — once, at a funnel, with the
  consumer winning.** Sibling of the "one producer" rules above, applied to *global* driver state
  rather than to a name or a scope, and it arrives with its own failure mode: not two answers, but
  **no answer at all, in a capability that reports green.** `Birko.Data.MongoDB` registered nothing —
  no class map, no convention pack, no serializer — and `MongoDBModel` "compensated" with a
  `[BsonRepresentation(BsonType.String)]` **override** of `AbstractModel.Guid`. `BsonClassMap` maps
  *declared* members per class, so that override claimed an element name the base already claimed and
  the map refused to freeze. Measured against MongoDB 7: **neither store could write a single entity**
  — sync died on the class map, async on driver 3.x's `GuidRepresentation.Unspecified` (which throws
  instead of choosing), and a read-back returned 0 rows (TASK-214). Four parts generalise:
  - **A shadowing member is not a local decision.** Re-declaring a base member to hang an attribute on
    it is invisible at compile time and fatal at the first serialize. Configure the member on the class
    map of the type that **declares** it — which also made the fix cover the async store's
    `AbstractModel` constraint, something the override never could.
  - **Register at the funnel, not in a module initializer, so the CONSUMER wins.** `MongoDBClient`'s
    constructor is the one point both stores' `SetSettings` reach, and it runs after start-up, so a
    consumer that registered its own serializer first keeps it (`TryRegister*`, first-wins). A module
    initializer is stricter — it cannot be missed — and that is exactly why it is wrong here: it runs
    before consumer code and would silently override it. **Precedence beat coverage.**
  - **A default that "looks safe" can be the one that throws.** Driver 3.x *removed*
    `BsonDefaults.GuidRepresentation`; the remaining default is `Unspecified`, which refuses. Do not
    assume an unset knob means a sane fallback — measure what the unset state actually does.
  - **A framework-global flag needs its inheritance checked.** `IgnoreExtraElements` on the base class
    map applies to that class alone unless `SetIgnoreExtraElementsIsInherited(true)` — and every real
    entity is a derived type with its own automapped map. The narrower call compiles, reads correctly,
    and fixes nothing.
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

### Nothing could be saved to MongoDB — the store was never able to serialize an entity (2026-08-16)

TASK-214, verified against a live **MongoDB 7** rather than the offline registry the finding was filed
from. The finding held and was **wider than filed**: not "the sync store cannot serialize", but
**neither store could persist a single entity**, and repositories inherited it through the same
constraints. `Birko.Data.MongoDB` registered no driver serialization at all, and `MongoDBModel`'s
attempt to compensate — a `[BsonRepresentation(BsonType.String)]` **override** of `AbstractModel.Guid`
— is what made the class map unfreezable. Fixed by deleting the override and adding one
`MongoSerialization.EnsureRegistered()`, called from the `MongoDBClient` constructor. The standing rule
is in § Conventions above. Split: **Revert A 7 of 78** (6 fix-dependent), **Revert B 6 of 78**
(5 fix-dependent); ungated 78/78, 6 dependent suites 39/39. Four things worth carrying:

- **The live server found a third failure the offline probe structurally could not.** With the writes
  finally landing, every *read* threw `FormatException: Element '_id' does not match any field or
  property` — no Birko model declares `_id`, by design, so the driver's auto-generated ObjectId had
  nowhere to go. **Failures queue** (§ TASK-209's rule), and the ones behind the filed one only appear
  once you clear it.
- **The env-gated suite had never run, and running it was most of the value.** `MongoFilterMatrixLiveTests`
  no-ops without `BIRKO_MONGO_HOST`, so the entire MongoDB surface was green while unable to write.
  Starting a container took a minute. The new serialization suite is deliberately **non-gated** —
  class-mapping and BSON round-trip need no server, which is precisely why gating them was indefensible.
- **Registration goes at a funnel and lets the consumer win.** `MongoDBClient`'s constructor, not a
  `[ModuleInitializer]`: shared projects compile into the consumer's assembly, so an initializer would
  run *first* and the framework would always beat the consumer's own configuration. Stricter coverage,
  wrong precedence.
- **`.map.yml` under-coverage, fifth instance — and this time it bit before the fix, not after.** None
  of the four changed files was reachable by any glob, so the harvest never specced the defect *and*
  this fix's own regen would have produced a clean diff over unread code. Added to
  `core-model-contracts`; `ChangeStreams/*.cs` + `MongoDBLogModel.cs` remain unmapped (TASK-208).
  Also spawned **TASK-218**: with writes working, the matrix suite reported 26/27, and the 27th is real
  — an array's `.Contains` binds to `MemoryExtensions.Contains` on .NET 9+ and the driver rejects it.

### The unbounded-filter guard was never wired into the base it lives on (2026-08-16)

TASK-215 set out to wire `RequireBoundedFilter` into two more backends and found the hole one layer up:
`AbstractBulkStore` / `AbstractAsyncBulkStore`'s **own six** filter-based destructive wrappers never called
it. Measured on `JsonStore`, which overrides none of them, `Delete(x => !empty.Contains(x.Value))` left
**0 of 3** rows with no exception — so the defect was live on JSON, XML, RavenDB, CosmosDB and InfluxDB,
none of which the finding named. Now called by all twelve paths: six base wrappers, InMemory's two
overrides, ElasticSearch's four. The standing rule is in § Conventions above. Split: **18 of 34** on the
whole revert (InMemory 10/16, JSON 2/5, ES 6/13), plus **2 of 69** on an isolating revert of the door-name
fix alone; 35 suites / ~2,100 tests green. Four things worth carrying:

- **A "wire it per backend" rule got read as "wire it only in backends".** The guard's helper and its
  unguarded callers were in the *same class*, and three tasks walked past them. When a rule says measure
  before wiring, that is about not guessing which shapes reach destruction — not a licence to skip the
  implementation every unlisted backend inherits.
- **The probe for the filed half found the real half.** The InMemory measurement was only supposed to
  confirm two overrides; including the four non-overridden `Update` paths in the same probe table is what
  exposed the base. Probe the whole verb family, not the methods the finding lists.
- **ElasticSearch is the third backend where the defect shape renders as ordinary output.**
  `!empty.Contains(x)` → `bool { must_not: [match_none] }` (selects everything) versus the legitimate
  `bool { must_not: [terms] }` — same structure, different inner type, so CR-H047's null-check guard never
  fired. After SQL's `1 = 1` and MongoDB's `$nin: []`, this is settled: guard the expression.
- **`.map.yml` under-coverage, fourth instance, this time caught before it mattered.** The ES stores were
  reachable by no glob in `bulk-filter-operations`, so the regen for this very fix would have been blind to
  them. Added. The older `AbstractConnectorBase.cs` gap the file already documents twice is still open
  (TASK-208).

### The write half: a filtered DELETE/UPDATE drops the qualifier the read path aliases (2026-08-15)

TASK-216, spawned by TASK-211 rather than absorbed into it, because the mechanism is shared but the fix is
not. `ResolveColumnName(…, withTableName: true)` qualifies every condition name while a write quotes its
target table, so on PostgreSQL every filtered `Delete`/`Update` on a PascalCase entity failed:

```sql
DELETE FROM "FwPeople" WHERE FwPeople.Name = $1
-- ERROR: missing FROM-clause entry for table "fwpeople"
```

Fixed by **stripping** the target table's qualifier in `AddRequiredWhere` — not by quoting it and not by
aliasing (MSSql rejects `DELETE FROM t AS a`). The standing rule is in § Conventions above. Split: **4 of 22**
live PostgreSQL, **3 of 500** offline; 23 SQL suites green. Four things worth carrying:

- **Unlike the read half this was loud, and that is the whole reason it was a separate task.** The
  missing-FROM wording is not the missing-relation wording, so it threw rather than being swallowed. Same
  root cause, opposite failure mode — worth separating, because severity follows the failure mode, not the
  cause.
- **The reproduction found all four shapes at once, including both function-wrapped ones.** `LOWER(T.Col)`
  and the `.Date` rewrite's `(T.Seen >= @a AND T.Seen < @b)` are the shapes a per-condition-name rewrite
  misses, and they were in the first run because the probes were written to include them *before* choosing
  the mechanism. Design the reproduction against the fix you might get wrong, not the one you expect.
- **Two of the six probes passed immediately, and both were worth writing.** `SelectCount` goes through
  `CreateSelectCommand`, so TASK-211's alias already covered it — the task's acceptance asked whether a
  fourth sink existed, and measuring answered *no* instead of leaving it to be rediscovered. The other pins
  the whole-table write guard, which the rewrite runs after.
- **The regression test found a second, unrelated defect and it was filed, not asserted.** The obvious
  `Update(Table, values, conditions)` overload builds its SET list from **every** column while binding only
  the caller's subset, so a partial update emits unbound placeholders. Measured on both providers before
  filing (loud on each; SQLite leaves the row unchanged) — the initial guess, that unbound would bind NULL
  and silently blank the other columns, was wrong and would have justified a much larger fix. TASK-217.
