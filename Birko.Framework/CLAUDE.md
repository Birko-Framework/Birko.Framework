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
- **A write that opens its own connection cannot be inside anybody's transaction — and a boundary is only
  as wide as its NARROWEST participant.** `AmbientSqlTransaction` (TASK-240) taught the single-command paths
  to join an open boundary; the bulk paths kept opening their own connection and their own transaction, so
  every collection-shaped write — create-many, update-many, delete-many, delete-where, delete-all — happened
  outside it (TASK-242, measured in consumer Symbio: **20 of 158** boundary-wrapped operations broke). Five
  parts generalise:
  - **Rank the SILENT provider above the loud one.** SQLite blocks on a lock it cannot take and fails after
    the command timeout; PostgreSQL, MySQL and MSSql allow two connections, so the escaped write **commits
    and survives the owner's rollback with no error at all**. A test that asserts "no exception was thrown"
    passes against the broken code on every one of them, so **the assertion is committed rows counted after
    a rollback, on a connection of its own**. Same family as § TASK-218's "a driver that silently drops what
    it cannot translate is the worst case".
  - **One producer for "am I inside a boundary", and the OWNED path is where providers differ.**
    `AbstractConnector.RunBulk` / `AbstractAsyncConnector.RunBulkAsync` own the decision and hand the body
    `(connection, transaction, owned)`; the body commits and rolls back only `if (owned)`.
    `RunBulkOnConnection[Async]` is the same decision for a write that carries its own atomicity and wants
    no transaction of its own — PostgreSQL's binary `COPY`, `SqlBulkCopy` — so those two keep running
    unwrapped when they own. Where the shipped providers already disagreed (SQLite's bulk path retries per
    CR-M144, the three servers' never did), the difference became an explicit `retryWhenOwned` parameter:
    **a shared helper is exactly where a per-provider policy gets flattened by accident.**
  - **The participating path never retries.** A retry re-runs statements inside a transaction whose earlier
    statements already succeeded, and on most providers the first failure has already aborted it, so it can
    only fail differently. Retrying is the boundary owner's decision — the same reasoning `RunCommandOn`
    already applies to single commands. Do not "improve" this by adding retry back.
  - **Joining is only half of it: something has to PUBLISH the boundary, and the layer that publishes is
    not the layer that joins.** The eight provider stores override the bulk `*Core` methods and call
    `Connector.Bulk*` directly, bypassing the base's per-item write — and the base was the only caller of
    `EnterTransactionScope`. So `SetTransactionContext` was inert for every bulk write on every provider,
    which is the **only** door a sync store has (`SqlUnitOfWork.FromStore` takes an `AsyncDataBaseStore`).
    Measured: reverting just those lines fails 4 of 10 (SQLite) and 3 of 11 (each server). When a rule is
    wired into a connector, ask which layer feeds it.
  - **`SqlBulkCopy` enlists only through its `SqlTransaction` overload, and `TableLock` is owned-path only.**
    The third constructor argument was `null`, which is precisely how the copy escaped. A bulk-update table
    lock taken by a standalone copy dies with that copy; taken inside somebody else's boundary it is held
    until *their* commit, serialising every other writer against the table for the life of a transaction
    that never asked for it.
  - **DDL is not a write like the others: on a provider whose DDL is not transactional it must be issued
    OFF the boundary, and that is a stated provider capability rather than a blanket rule.** Stores
    initialise lazily, so a store's *first* data access issues `CREATE TABLE IF NOT EXISTS` — and once the
    single-command paths joined the ambient, that DDL did too. **MySQL implicitly commits an open
    transaction before and after every DDL statement**, so a store whose first operation happened inside a
    boundary committed it before the caller's own write ran, and the rollback undid nothing: three rows
    survived a rolled-back boundary, silent on the way in and on the way out (TASK-243, measured on 8.4).
    `AbstractConnectorBase.SupportsTransactionalDdl` is the switch — false for MySQL alone — and
    `AbstractConnector.DoDdlCommand` / `DoDdlCommandAsync` the single funnel that consults it, suppressing
    the ambient via `AmbientSqlTransaction.Suppress()`. Four parts generalise:
    - **The blanket version of this fix is a HANG, not a smaller win.** "Run schema-ensure outside any
      boundary" is the obvious provider-independent answer and it is wrong: SQLite serialises at the file
      level, so a second connection cannot take the write lock the boundary holds and blocks for the whole
      busy timeout. Measured by making the suppression unconditional — 3 of 3 SQLite tests failed with
      `SQLite Error 5: 'database is locked'`. **The two halves of the trade land on opposite providers**:
      the one that needs DDL on the boundary's connection is exactly the one with transactional DDL, and
      the one that needs it off is exactly the one where a second connection is legal. That is what makes
      the switch safe rather than lucky, and it is why the answer is a capability and not a rule.
    - **A funnel with four overrides is not a funnel.** The base emitters were rewired first and the fix
      measured as *not working* (5 of 7 still red): `MySQLConnector`, `PostgreSQLConnector`,
      `MSSqlConnector` and `TimescaleDBConnector` each **override** `CreateTable(string, IEnumerable<string>)`
      with their own `DoCommand`. Third instance of this shape in a fortnight — TASK-215's base wrappers,
      TASK-242's store `*Core` overrides, this. **When introducing a funnel, grep `override` on every
      method that reaches it before believing the wiring**, and confirm with a revert rather than a read.
    - **Suppression is for DDL and nothing else.** `AmbientSqlTransaction.Suppress()` installs a fresh cell
      with no head, so it hides the whole chain and restores exactly what was there. Anything else that
      suppresses a boundary is *escaping* it, which is the defect TASK-240 and TASK-242 exist to remove.
      The legacy `ExternalConnection`/`ExternalTransaction` pair is deliberately **not** suppressed: its
      only user is the migrations `SqlSchemaBuilder`, which exists to run DDL in a transaction it owns.
    - **The two providers now give opposite answers about whether a created table survives a rollback, and
      both are pinned.** On MySQL it survives (the DDL is no longer in the boundary); on PostgreSQL, MSSql
      and SQLite it is rolled back with it. Asserting both is what stops the next reader "unifying" them
      from symmetry. Whether schema-ensure should be in a caller's unit of work at all is TASK-244, still
      open — and the residue is that a store whose schema-ensure was rolled back still believes it is
      initialised.
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
  where the unquoted DDL identifier folds to lower case. (**Sixth instance, TASK-242**: PostgreSQL's binary
  `COPY … FROM STDIN` built its column list with `QuoteIdentifier`, so `BulkInsert` had *never* worked for a
  PascalCase column on that provider — `42703: column "Name" of relation "T" does not exist`. Found only by
  running a bulk write against a live server, which nothing in the tree had done.) Quoting was never what closed either injection.
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
- **An identifier that reaches SQL as a string VALUE rather than as an identifier must be PRE-FOLDED — the
  parser's case-folding never runs on it, and that is the opposite of the quoting rule above.** Eighth and
  ninth instances of the identifier family (TASK-472), and the pair that shows the family is about *where the
  parser looks*, not about quoting. `create_hypertable('T', 'col', …)` takes a `regclass` and a `name`, both
  inside quoted literals, so **one argument needs quotes added and the other needs case removed**:
  - **The table is a `regclass`, so it carries its own quotes** — `'"Widgets"'`. Emitted bare the regclass
    folded to `widgets` against the `"Widgets"` that `CreateTable` created, raising `42P01`, **which
    `IsMissingTableException` classifies as a missing table so `OnException` swallowed it**. Measured on
    TimescaleDB 2: `CreateTable` reported success and **no hypertable existed for any PascalCase entity** —
    chunk routing, compression and retention silently absent, with a plain table serving reads and writes.
    Note the compounding: the § *reader that answers an ERROR with an empty result* entry below narrowed that
    swallow and it is **still** wide enough to hide a genuinely different statement's `42P01`.
  - **The column is a `name` compared against `pg_attribute.attname`, so it is folded** — `'ts'`, never
    `'Ts'`. This is the only `ToLowerInvariant` in the SQL connectors, and correctly so: everywhere else an
    identifier is emitted *as* an identifier and PostgreSQL folds it, so there is nothing to pre-fold. Grep
    for that asymmetry before assuming a new sink belongs to the quoting rule.
  - **The tell that a sink belongs here is a quoted literal, not a name.** Ask whether the parser will ever
    see the text as an identifier; if it will not, neither half of the bare/quoted convention applies and the
    sink needs its own answer. And **a default value can hide the folding half indefinitely** — this one was
    masked because the shipped `TimeColumn` was already lowercase and matched a folded property by luck, so
    the only configuration anyone ran was the one that worked.
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
  - **A framework-global flag needs its inheritance checked — and then ask why you need the flag.**
    `IgnoreExtraElements` on the base class map applies to that class alone unless
    `SetIgnoreExtraElementsIsInherited(true)`, because every real entity is a derived type with its own
    automapped map; the narrower call compiles, reads correctly, and fixes nothing. **⚠ The flag itself
    is gone as of TASK-219** — it was only ever tolerating a *second* id, and the entry below removes
    the second id instead. Kept here because "check the inheritance semantics of a global flag" is the
    part that generalises; needing such a flag at all was the smell.
- **A language-level overload change is a framework-wide event: normalise it ONCE, wire it where it
  actually breaks.** On .NET 9+ an **array**'s instance-style `set.Contains(x.Col)` binds to
  `MemoryExtensions.Contains(ReadOnlySpan<T>, T)` — or its `IEqualityComparer<T>` overload when `T` is not
  `IEquatable<T>`, which is every enum — instead of `Enumerable.Contains`. Nothing in the source changed;
  a recompile moved it. This framework has now been bitten **three times, in three different ways**, and
  that is the real lesson: `PredicateScope` could not evaluate the ref-struct operand and left every
  array-typed caller unguarded; the SQL parser fed the trailing comparer in as an operand and flipped the
  condition to `IS NULL` (0 rows against 21, Symbio TASK-249/254); MongoDB's driver does not know the
  method at all and threw `NotSupportedException: Specified method is not supported`, naming nothing
  (TASK-218). `Birko.Data.Expressions.SpanContains` now owns both halves — the unwrap and the rewrite to
  `Enumerable.Contains` — and `PredicateScope` calls the same unwrap. Four parts generalise:
  - **A driver that SILENTLY drops what it cannot translate is the worst case, and it is not rare.**
    RavenDB emits no `where` clause for a boolean ternary and malformed RQL for a ternary inside a
    conjunct — never an exception (TASK-222). Rank a dropped predicate above any number of loud refusals,
    and treat the two oppositely: **fix what lies, document what refuses.** The five loud Raven shapes are
    accepted in a ledger with a reason each; the silent ones were fixed. A ledger like that must fail on a
    listed entry that starts *passing*, or it stops being a record and becomes a blanket.
  - **A backend can reject a portable spelling for its OWN reasons, and then it needs its own rewrite.**
    RavenDB was excluded from the span rewrite correctly and still needed fixing: it translates no
    collection `Contains` at all, and refuses `x => true` — the documented read-all synonym — outright
    (TASK-221). Both are one root cause, "a spelling every other backend accepts", so they are one
    rewriter. **The dangerous half is what a rewrite must leave alone**: `x.CollectionMember.Contains(const)`
    is membership in the opposite direction and already worked, so a blanket rewrite would have broken it.
    Where a discrimination like that already exists somewhere in the framework — here
    `ElasticSearch.ParseContains` — reuse the test rather than re-derive it.
  - **Measure every translator before deciding the fix's scope — the audit both widens and narrows it.**
    Classified (TASK-218 + TASK-220): backends that *compile* the delegate are never at risk (InMemory,
    JSON, XML, InfluxDB — the runtime knows the method); the hand-rolled parsers were already correct
    (SQL `IN (1,5)`, ElasticSearch `terms=(1,5)`) because they evaluate the operand themselves; the
    raw-expression-to-driver backends **MongoDB and CosmosDB** were both broken. **RavenDB looks like the
    obvious fourth and is not**: *every* `Contains` spelling fails there, so the rewrite would have turned
    one failure into an identical one — it needs `.In()`, a different fix. One pass caught a backend the
    original task had waved off as needing a live service *and* stopped the fix going somewhere it would
    have achieved nothing. **"Needs a live service" is itself worth testing** — Cosmos renders SQL
    offline via `ToQueryDefinition()` and Raven builds RQL from an uninitialised-to-network
    `DocumentStore`, so both were measurable all along.
  - **Rewrite at the entry point, not at each hand-off.** The two MongoDB stores hand a filter to the
    driver from ~30 sites, but the caller's expression *arrives* at only nine methods. Normalising there
    means the guard, the whole-collection check and the driver all see one shape, and a new hand-off site
    is correct without being told — the same "fix where it is resolved, not at each producer" rule the
    identifier family above arrives at.
  - **Keep the rewrite narrow and reference-identical.** A real comparer is left alone (it cannot be
    honoured by `Enumerable.Contains(source, item)`, and a silent meaning change is worse than the throw),
    and a predicate with no span node is returned **by reference** so the pre-pass costs nothing on the
    overwhelming majority of reads. Assert that identity in a test; "it's a no-op" is otherwise a claim.
  - **Pin the premise, not only the fix.** A test asserts that `arr.Contains(x)` really does bind
    `MemoryExtensions` on this runtime. If a future .NET moves it back, the rewrite becomes dead code —
    and that test is what says so, rather than the rewrite silently never firing again.
- **Where two layers can each define an identity, ONE of them owns it — and the tell is a silently
  empty result, not an error.** Same one-producer family as the identifier rules above, at the layer
  where a *document* gets its key. TASK-214 fixed serialization by leaving `_id` to the driver as an
  auto-generated ObjectId with the canonical `Guid` beside it; `Birko.Data.MongoDB.Views`'s translator
  had always rewritten the `Guid` property to `_id`. Both were self-consistent and they disagreed, so
  every Mongo view was wrong: measured on MongoDB 7, projecting the canonical id **threw**
  (`Cannot deserialize a 'Guid' from BsonType 'ObjectId'`) and filtering on it returned **0 rows for a
  document that exists** — no error, no log. Settled by making the canonical `Guid` **be** `_id`
  (`SetIdMember`), which is also what let `IgnoreExtraElements` be deleted. Four parts generalise:
  - **Resolve the contradiction, don't patch the louder side.** Fixing the translator would have left
    two ids in every document and kept the framework-wide silent-drop reader that tolerating them
    required. The cheaper edit was the wrong one.
  - **A migration objection can be measured away.** Changing an id layout is normally expensive; here
    TASK-214 had just proved *no write had ever succeeded*, so there was no stored data to migrate.
    That window closes the moment the fixed stores are used — **check whether a cost is real before
    paying to avoid it, and check whether it is about to become real.**
  - **A projection type is not an entity, and the driver assumes otherwise.** A view's class map must
    mirror the projection: canonical-id property string-represented (or the rendered `$match` compares
    BinData to a string and matches nothing), **no id member**, and element names equal to property
    names (or the driver's `NamedIdMemberConvention` binds a view property called `Id` to `_id`, which
    the projection explicitly suppresses). All three are one registration, `MongoViewSerialization`.
  - **The filter and the reader must share one map, because the filter fails quietly.** `MongoViewStore`
    renders `$match` through the same class map it deserializes with. A map that disagrees with storage
    produces a *wrong answer* on the filter and an *exception* on the read — so the read is what you
    notice, and the filter is what costs you.
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
- **A provider without a conditional form has to fake one, and the flag that turns it off has to be
  honourable everywhere — otherwise it is a silent no-op wearing a parameter's name.** Third member of the
  identifier/one-producer family to arrive through index DDL (TASK-245), and it shipped **two** defects at
  once, on two different providers, for two unrelated reasons — both silent, because TASK-204 made
  schema-ensure *record* an unbuildable index and nothing in a host subscribes to the report:
  - **MySQL rejected the statement outright.** `CREATE INDEX IF NOT EXISTS` is `ERROR 1064` there — a syntax
    error, so no `[IndexedField]` or `[CompositeIndex]` on a MySQL entity had **ever** produced an index,
    and for a UNIQUE one, never a constraint. MSSql overrides the emitter with a `sys.indexes` guard and
    SQLite/PostgreSQL support the clause, which left MySQL as the one provider that neither overrode nor
    supported it. The fix is a plain `CREATE INDEX` plus tolerance of **1061** (`Duplicate key name`) at the
    `CreateIndexes` funnel — the client answering the question the server cannot be asked.
  - **PostgreSQL could not resolve the columns.** `CreateIndexSql` quoted each column while `CreateTable`
    emits column definitions **bare**, so the stored column is folded (`status`) and a quoted `"Status"`
    raises `42703`. Measured on 16: **no declared PascalCase index could be created there either.** Seventh
    instance of the identifier family, and the second provider with this task's exact symptom by a
    completely different mechanism. Fixed by emitting columns bare — the rule two entries above, applied to
    a sink nobody had checked.
  Five things generalise:
  - **"Already there" is not "unbuildable", and a tolerance is only safe because they are different codes.**
    1061 vs **1062** (`Duplicate entry` — a UNIQUE index over violating data) vs **1170** (BLOB/TEXT with no
    key length). Tolerating 1061 cannot swallow the other two, so TASK-204's degrade-and-report survives
    intact; widening the predicate to any `MySqlException` fails 4 of the MySQL live suite. Match on the
    **code**, never the message, and walk `InnerException` — `InitException` re-wraps every command failure.
  - **An opt-out that only one provider can honour is the silent-drop shape, so parameterise the emitter,
    not just the funnel.** `CreateIndexes(..., throwIfExists: true)` would have been meaningful on MySQL
    alone and a no-op on the three providers whose conditional DDL cannot raise. Adding
    `CreateIndexSql(..., conditional)` — base drops `IF NOT EXISTS`, MSSql drops its guard, MySQL stops
    tolerating 1061 — is what makes the flag mean one thing everywhere. § SH-H037's "the opt-out is part of
    the fix and needs its own test", arriving as *the opt-out must be honourable on every backend it is
    declared on*.
  - **A duplicated emitter is load-bearing only because something upstream dropped a field.**
    `SqlIndexManager.ToSqlIndexDefinition` never copied `Unique`, and *that* is the sole reason a parallel
    `CreateUniqueIndexSql` existed on the base plus the PostgreSQL and MSSql managers — one of those copies
    carrying the quoted-column defect independently. Copying one property collapsed four emitters into one
    producer and fixed a second PostgreSQL path for free. **When you find the same statement written three
    times, look for the field that gets lost on the way in** rather than adding a fourth override, which is
    what this task's own filed plan proposed.
    **Second instance, one day later (TASK-246): the same property, one layer over.**
    `Birko.Data.Migrations.SQL`'s `SqlIndexBuilder.Build()` also dropped `Unique` on the way to the
    connector, so a migration's `.Unique()` built a **plain** index on all four providers — a missing
    *constraint*, silently accepting the duplicate rows the migration existed to forbid. **What hid it is the
    generalisable part: the builder has two branches**, a connector path (every production migration) and a
    raw-SQL fallback taken only when `connector == null` — and the fallback *did* honour the flag, while every
    test in that project constructed the builder with `null`. So the feature worked in the branch nobody uses
    and failed in the branch everybody uses, and a green suite said nothing. **Where a component has a
    fallback branch, a test that takes the fallback is not a test of the component** — check which branch your
    fixture selects before trusting it, and prefer a revert to a reading.
  - **A public contract can be *narrowed to what it meant* rather than preserved literally.** "The public
    `CreateIndexes` still throws" (TASK-204) is about an index that cannot be **built**; it was never about
    "already present", which the other three providers report as success. Making MySQL idempotent there
    makes it agree with them and lets a re-applied migration work — and both halves are now pinned by tests,
    where previously *nothing in the tree called `CreateIndexes` directly at all*.
  - **A silent divergence is allowed to stay if it is measured and named.** MSSql keeps bracket-quoted index
    columns (case-insensitive collation, no defect, no live measurement to justify churn), and a same-name
    index over *different* columns is silently accepted on **every** provider — measured, since PostgreSQL's
    own `IF NOT EXISTS` reports "already exists, skipping" and keeps the old definition. Faithful emulation,
    recorded rather than "fixed" into a divergence.
  - **Unquoting an identifier removes an accidental containment, so the sink that takes CALLER text needs
    the check.** The bare-column fix is required for correctness on PostgreSQL, and it landed on two sinks
    with different provenance: schema-ensure resolves `[IndexedField]` / `[CompositeIndex]` columns against
    mapped properties (safe), while `IIndexManager.CreateAsync` interpolates its field names straight from
    the caller. `QuoteIdentifier` had been *incidentally* containing a payload there; bare, it breaks out —
    measured, 9 of 14 tests, with `Rank); CREATE TABLE Pwned (x INTEGER); --` reaching the DDL exactly as
    SH-H023's rule field did. `SqlIndexManager` has a table name and no entity type, so it takes the
    sanctioned weaker fallback — `DataBase.ValidateIndexFieldIdentifier`, **sharing `_bareIdentifier` with
    `ValidateRuleFieldIdentifier`** so the two sinks cannot drift about what an acceptable identifier is.
    The general rule: **when you remove quoting from an interpolated identifier, enumerate that sink's
    callers by provenance** — metadata-derived needs nothing, caller-derived needs the check — and do not
    assume the quoting you deleted was decorative.
    **And the enumeration is only as good as the grep: TASK-245 wrote that rule and shipped a violation of
    it in the same commit** (TASK-249). There were **two** caller-derived sinks;
    `Birko.Data.Migrations.SQL`'s `SqlIndexBuilder.WithField` also takes free text, and `Build()`'s connector
    path hands it to `CreateIndexes` verbatim without ever touching the translator the guard was placed in —
    so a migration could append a second statement through a column name. Grep every **construction of the
    object that carries the identifier** (here `Tables.IndexColumn`), not only the translator you happen to
    be editing. Two further corollaries from the same review: the check must reject a **`Table.` qualifier**
    (a `CREATE INDEX` column list takes none, so `_bareIdentifier`'s optional-qualifier branch let the
    payload's harmless cousin through to break the statement — and the test written in that pass *pinned* the
    qualifier as acceptable, which is how a guard's own suite enshrines the guard's bug); and a **uniformity
    claim has to be checked one layer up**, because `IIndexManager.CreateAsync`/`DropAsync` bypass the
    `CreateIndexes` funnel by design and so needed their own answer rather than inheriting one.
  - **Check which twin the production path actually runs before believing a revert.**
    `AsyncDataBaseStore.InitCoreAsync` calls the **sync** `Connector.CreateTable` inside a `Task.Run`, so an
    async store's schema-ensure runs the sync index loop and `CreateIndexesAsync` has no store-level caller.
    Reverting only the async site fails **0 of 14**; the sync site fails exactly the boundary test. Fourth
    instance of TASK-243's "a funnel with four overrides is not a funnel", arriving as *the async path you
    patched may not be the one anything calls*.
- **A provider-specific ceiling is fixed at the provider, and "the declaration is wrong" is a claim to
  measure before acting on it.** Fourth member of the index-DDL family (TASK-248). MySQL maps an unbounded
  `string` to `LONGTEXT` and **cannot index a BLOB/TEXT column without a key length** (measured on 8.4 as
  ERROR 1170, UNIQUE and plain alike), so after TASK-245 fixed the statement syntax an index over a plain
  `string` still could not be built there — it merely failed with a different code, recorded and invisible.
  The honest-looking fix was to refuse the declaration at table load, per § SH-H037. **Measuring it inverted
  the answer:** 7 live consumer entities (Symbio's docnumber and e-mail UNIQUE composites) declare exactly
  that shape and work correctly on PostgreSQL *today*, and 0 framework domain models declare it at all — so
  refusing framework-wide would have converted seven working entities into start-up failures to fix a
  different provider. The fix is `AbstractField.IsIndexed` consulted by `MySQLConnector.ConvertType` alone,
  emitting `VARCHAR(255)`. Four parts generalise:
  - **Scope the change to the provider that is broken.** Bounding the column on all four would have imposed a
    255-character ceiling on columns that have none on PostgreSQL/SQLite/MSSql, so a value that writes fine
    today would start failing — breaking three working providers to fix one. The divergence being introduced
    is MySQL's own index-key limit, not a framework choice, and it is asserted per provider rather than
    assumed: each of the other three has a test that an indexed unbounded string is *still* TEXT.
  - **Prefer the loud narrow failure to the quiet weak one.** A prefix index (`ux(Col(64))`) would have made
    every one of those UNIQUE constraints **weaker than declared** — refusing two genuinely different values
    that share a prefix. A bounded column refuses the over-long *write* instead. When both options degrade
    something, degrade the thing the caller can see.
  - **A flag consumed in one place is set in as many places as the metadata is resolved.**
    `DataBase.LoadIndexes` resolves index columns to fields at **two** points, one per attribute form
    (`[IndexedField]`, `[CompositeIndex]`), and marking only one leaves half the declarations looking
    unindexed. The gap was invisible until a revert: dropping the per-property marking failed **0** tests,
    because every model in the new suite used the class-level form. **A revert that fails nothing is a missing
    test, not a redundant fix** — the same lesson as TASK-245's async-site revert, in a second shape.
  - **§ SH-H037's fail-fast rule still needs its blast radius measured, and here the measurement said no.**
    Refusing an unhonourable declaration is right when the declaration cannot work anywhere; this one works on
    three of four providers, so the unhonourable thing was the *provider's* limit and that is where it was
    absorbed. Note the survey itself had to be corrected twice — the consumer entities declare their
    attributes fully qualified (`[Birko.Data.SQL.Attributes.CompositeIndex(...)]`), which an unqualified grep
    misses entirely. **Verify a blast-radius count against one known instance before trusting it.**
- **A fallback branch nobody can reach is not a safety net — it is a second implementation that drifts, and
  it can invalidate the tests of the first.** TASK-247, closing the index-DDL family. `SqlSchemaBuilder` took
  an *optional* connector and carried a hand-written raw-SQL fallback in all eight of its methods for the null
  case. Two had drifted into being wrong on two providers, so the "connector-free" capability emitted DDL that
  MySQL and PostgreSQL reject — it was never portability, only the appearance of it. Deleted rather than
  repaired, and the connector made required. Four parts generalise:
  - **The fallback's real cost was to the test suite.** `connector == null` is how **every** test in that
    project constructed the builder, so six tests exercised only the dead branch — which is precisely how
    TASK-246's missing `Unique` flag on the *live* branch stayed green. A dependency that can be `null`
    silently selects a different implementation, and a test that passes `null` may be asserting about code
    nothing ships. Requiring it converted those six into real tests.
  - **Prove unreachability before deleting, and say how.** The only production construction is
    `SqlMigrationRunner` → `SqlMigrationContext`, which requires a non-null connector; the optional 4th
    argument was the sole door. A sweep of **all 16 consumer repos** found 0 hand-built contexts and 0 uses of
    `ISchemaBuilder` at all. "Nothing calls it" is a measurement, not an assumption — and the same sweep
    corrected TASK-246's blast radius from *shipping* to *latent*, which is a claim that had already been
    written into a commit message.
  - **The refusal names where to get the thing it is refusing.** § SH-H037 again: the `ArgumentNullException`
    says the removed fallback was wrong on MySQL and PostgreSQL *and* that `SqlMigrationRunner` already holds a
    connector, and both halves are asserted by the test. A guard whose message only says "no" gets reached
    around.
  - **Say what the deletion does not carry over.** The removed fallback emitted a composite
    `PRIMARY KEY (a, b)` that `AbstractConnector.CreateTable` does not, and `RenameField` had no connector
    equivalent at all so it stays hand-written. Neither is reachable by anything in the tree, but a deletion
    that quietly drops a capability is indistinguishable later from one that never had it — write down the
    difference.
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

- **An `## Out of scope` bullet that describes WORK gets an id before the task closes.** The generic
  `/tasks close` step 5d sweeps for this, and it exists because of this repo: the index-DDL thread
  (TASK-245 → 249) left **six** latent per-provider gaps as out-of-scope prose across five closed tasks,
  which nothing ranks — the same evaporation the § *findings become tasks* rule is about, wearing a
  different heading. They were eventually collected as [[TASK-252]]; the point is that they should each
  have been offered as a spawn when they surfaced. A bullet naming an owner (`TASK-NNN owns it`) is a
  boundary and belongs there; an unowned "Z is also broken" is a spawn that was skipped. Several small
  ones from the same thread → **one grouped task**, not six.
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

### No hypertable was ever created, and TimescaleDB never published the boundary (2026-08-18)

Consumer Symbio TASK-472 asked for the bulk-write boundary to be verified on TimescaleDB "separately, over a real
hypertable", because `TimescaleDBConnector : PostgreSQLConnector` overrides no bulk method and therefore
*inherits* TASK-242's fix — and *"it inherits, so it is covered"* was named in the task as the claim most worth
disproving. It disproved, twice, and both defects were silent. Verified against live **TimescaleDB 2 /
PostgreSQL 16**: **39 tests green in `Birko.Data.TimescaleDB.Tests`, up from 19**; 20 new. The standing rules are
in § Conventions. Five things worth carrying:

- **Establishing the premise is what found the bigger defect.** `create_hypertable` emitted its table **bare**
  inside a string literal, so the regclass folded to `tsschemarows` against the `"TsSchemaRows"` that
  `CreateTable` had created → `42P01` → which `IsMissingTableException` classifies as a missing table, so
  `OnException` ran `DoInit()` and **returned**. `CreateTable` reported success and **no hypertable existed** —
  for every PascalCase entity, which is all of them. Chunk routing, compression and retention silently absent
  while a plain PostgreSQL table served reads and writes and made the store look correct. **A suite written
  without checking the premise would have "verified the boundary over a hypertable" against a plain table.**
- **The two identifiers in one call needed opposite treatments**, which is the generalisable half: the table is
  a `regclass` and must carry its **own quotes**; the time column is a `name` compared literally against
  `pg_attribute.attname` and must be **pre-folded**, because column definitions are emitted bare (TASK-209).
  Neither travels as an identifier, so the parser's folding never runs — see § Conventions. The column half was
  hidden by the shipped default `TimeColumn = "timestamp"` matching a folded `Timestamp` property **by luck**.
- **TASK-242's own lesson had a ninth store nobody wired.** It taught that joining a boundary is only half of
  it — something must **publish** it — and put `EnterTransactionScope()` into the eight provider stores' bulk
  `*Core` overrides. TimescaleDB got none, so the inherited connector fix was **unreachable from this store**
  and `SetTransactionContext` was inert for every bulk write, which is the *only* door a sync store has.
  Fifth instance of "a funnel with four overrides is not a funnel". **And the broken door was the quiet one:**
  `SqlUnitOfWork` publishes the ambient itself, so it worked all along — a test written against the unit of
  work alone reports success either way.
- **Two traps that both read as product defects.** `timescaledb_information.hypertables` holds the name with its
  **case intact**, so a lowercased lookup returned 0 and briefly made a working fix look broken (*check whether
  a reproduction failed for the reason you think*). And `ModelMapRegistry` applies into process-wide state and
  **accumulates** — one model type mapped two ways merged both mappings into `42P16 multiple primary keys`.
- **A test that pinned the defect did so through its fixture's choice of name.**
  `BuildCreateHypertableSql_ComposesQuotedArgsAndInterval` asserted `create_hypertable('metrics', …)`, and for an
  already-lowercase name the missing quotes make no difference at all. Same family as TASK-246's fallback branch:
  **a fixture that cannot distinguish the fix from the defect is not coverage.** Reverts: bare table **5 of 5**,
  dropped fold **4 of 5** (survivor = the lowercase case, the correct discrimination), removed publications
  **4 of 13** (exactly the per-store-door tests). Spawned **TASK-253** (the migrations project duplicates the
  broken statement in three emitters with *no* escaping, and `CreateHypertableAsync` bypasses the DDL funnel) and
  **TASK-254** (the fix turns a silent no-op into a throw out of lazy schema-ensure, against TASK-204 — shipped
  only because the blast radius measured **0** entities today).

### MySQL could not index an unbounded string, and refusing the declaration was the wrong fix (2026-08-18)

TASK-248, the last of TASK-245's three spawns. MySQL maps a plain `string` to `LONGTEXT` and cannot index a
BLOB/TEXT column without a key length (**1170**), so after the statement-syntax fix an index over an unbounded
string still could not be built there. The obvious remedy — refuse the declaration at table load, per
§ SH-H037 — was **measured and rejected**: 7 live consumer entities declare exactly that shape and work
correctly on PostgreSQL today, while 0 framework domain models declare it at all. Fixed instead by bounding the
column on MySQL alone (`VARCHAR(255)` when `AbstractField.IsIndexed`). 1,170 tests green across 19 suites
including the five `Birko.Models.*.SQL` domain suites; 6 new. The standing rule is in § Conventions. Four
things worth carrying:

- **The measurement inverted the decision.** "This declaration is unhonourable, so refuse it" was right in
  the abstract and wrong here: it is unhonourable on one of four providers, so refusing framework-wide would
  have turned seven working entities into start-up failures. § SH-H037's fail-fast still requires the blast
  radius to be cleared first, and this is the case where clearing it says no.
- **A revert that fails nothing is a missing test.** `DataBase.LoadIndexes` resolves index columns at two
  points, one per attribute form; dropping the per-property marking failed **0 of 67** because every model in
  the new suite used `[CompositeIndex]`. Adding an `[IndexedField]` model made it fail exactly 1. Second
  instance of this shape in three days after TASK-245's async-site revert.
- **Degrade what the caller can see.** A prefix index would have made all seven UNIQUE constraints weaker
  than declared — silently refusing genuinely different values sharing a prefix. A bounded column refuses the
  over-long write instead, loudly.
- **The blast-radius survey was wrong twice before it was right.** The consumer entities declare their
  attributes fully qualified, so an unqualified `[CompositeIndex(` grep found none of them, and a hand-rolled
  property parser then mis-classified them as bounded. Both corrections came from checking one known instance
  by hand. Verify such a count against a known case before trusting it — a survey that under-reports reads
  exactly like a clean bill of health.

### The migration schema builder's connector-free fallbacks were deleted, not fixed (2026-08-18)

TASK-247, closing the index-DDL family TASK-245 opened. `SqlSchemaBuilder` took an **optional** connector and
carried a hand-written raw-SQL fallback in all eight methods for the null case; two had drifted into emitting
DDL that MySQL and PostgreSQL reject (`CREATE INDEX IF NOT EXISTS "Col"`, and a `DROP INDEX IF EXISTS … ON …`
that is wrong on both in opposite directions). So the connector-free path was never portability — only the
appearance of it. Deleted, connector required, 48 lines lighter. Build clean; **1,199 tests green across 17
suites**; the revert fails 2 of 47. Four things worth carrying:

- **The fallback's real damage was to the tests.** `connector == null` was how **every** test in that project
  built the schema builder, so six of them exercised only the dead branch — exactly how TASK-246's missing
  `Unique` flag on the live branch stayed green. Requiring the connector turned those six into real tests.
- **Unreachability was measured, not assumed** — and the same sweep corrected a claim I had already committed.
  All 16 consumer repos: 0 hand-built contexts, 0 uses of `ISchemaBuilder`, 0 migration-declared indexes. That
  last one means **TASK-246 was latent, not firing**; its entry below and its commit message said "silently
  accepting duplicates", which overstated the live impact. Corrected in place rather than left to read as data
  corruption.
- **The test that pinned the fallback became the test that pins its refusal.** TASK-246 had added one
  asserting the fallback honoured `_unique` correctly; rather than delete it, it now asserts the null connector
  is refused *and* that the message names both why the fallback was not a usable alternative and where to get a
  connector.
- **Two capabilities genuinely disappear with it**, written down so a later reader can tell a deliberate drop
  from an oversight: composite `PRIMARY KEY (a, b)` (the connector renders per-column primary keys only), and
  `RenameField` keeps its hand-written SQL because there is no connector equivalent — with `RENAME COLUMN` not
  being universal, a latent gap of the same family.

### A migration's `.Unique()` built a non-unique index on every provider (2026-08-18)

TASK-246, spawned by TASK-245's planning grill. `SqlIndexBuilder.Build()`'s connector path — the one every
production migration takes — constructed its `Tables.IndexDefinition` without copying `_unique`, so
`.Unique()` emitted a plain `CREATE INDEX` on SQLite, PostgreSQL, MySQL and MSSql alike. A missing
**constraint**, not a missing optimisation — any duplicate the migration was written to forbid would be
accepted. **Latent, not firing** (corrected 2026-08-18 by a sweep of all 16 consumer repos): no consumer
declares an index through a migration, and Symbio's unique docnumber indexes come from `[CompositeIndex]`
attributes via schema-ensure, which was never affected. The fix still matters — it is public surface, and
Symbio's own `UniqueIndexDataCheck` exists because they expect to add migrations — but no data was corrupted. One-line fix; the work was the test surface. 7 new tests, 46 green in
`Birko.Data.Migrations.SQL.Tests` with live PostgreSQL 16; the revert fails **3 of 46**, including the live
one. Three things worth carrying:

- **A fallback branch can make a green suite meaningless.** The raw-SQL fallback three lines below the defect
  *did* honour `_unique`, and it is taken exactly when `connector == null` — which is how **every**
  pre-existing test in that project built the schema builder. The feature was demonstrably working in the
  branch nobody uses and broken in the branch everybody uses. Check which branch your fixture selects.
- **Second instance of the lost-flag shape in two days**, after `SqlIndexManager.ToSqlIndexDefinition`
  dropped the identical property one layer over (TASK-245). Both were invisible for the same reason: the
  omission is in an object initialiser, where a missing line looks like nothing at all.
- **The enforcement assertion is the test; the DDL text is the companion.** Asserting `UNIQUE` appears in
  `sqlite_master` would also pass for an index the engine never applied, so every test here inserts the
  duplicate and requires the write to fail — and the without-`.Unique()` case is pinned too, so the fix
  cannot be "make everything unique".

### No declared index was ever created on MySQL or PostgreSQL (2026-08-18)

TASK-245, spawned by TASK-243. Two independent defects with one user-visible symptom — a declared
`[IndexedField]` / `[CompositeIndex]` producing **no index, and for a UNIQUE one no constraint** — on the two
providers most likely to be in production, silent since TASK-204 made schema-ensure record rather than throw.
MySQL rejected `CREATE INDEX IF NOT EXISTS` outright (`ERROR 1064`); PostgreSQL 16 could not resolve the
quoted index columns against the folded ones its bare-column `CREATE TABLE` actually creates (`42703`) —
seventh instance of the identifier family. `DROP INDEX` was wrong on MySQL twice over (`IF EXISTS` rejected,
mandatory `ON` clause missing). The standing rule is in § Conventions above. Verified against live **MySQL
8.4**, **PostgreSQL 16** and **SQL Server 2022** plus on-disk SQLite: **1,086 tests green across 14 suites**,
61 of them new. Six things worth carrying:

- **The gate found it, and the boundary of it, before the fix existed.** Written test-first, the new MySQL
  suite failed **12 of 14** against the unfixed tree; the 2 that passed were "still throws" pins that passed
  for the *wrong* reason (1064 rather than 1062/1091), so both were strengthened to assert the error code and
  now discriminate. Choosing the test model is also what exposed a **third** defect: an index over an
  unbounded `string` (→ `LONGTEXT`) fails with `1170` on MySQL — closed the same day by TASK-248, which
  bounds an *indexed* string to `VARCHAR(255)` on that provider only. That shape is exactly what the canonical
  SQLite example declares.
- **Revert splits, each isolating one claim:** MySQL statement **13 of 14** · base column unquote **6 of 6**
  (PostgreSQL) · tolerance filters **3 of 14** · predicate narrowing **4 of 14** · `DropIndexSql` **2 of 14**
  · `Unique` hand-off **1 of 6** · DDL funnel **1 of 14** — but only at the **sync** site.
- **The async path you patch may not be the one anything calls.** `AsyncDataBaseStore.InitCoreAsync` invokes
  the **sync** `Connector.CreateTable` inside a `Task.Run`, so `CreateIndexesAsync` has no store-level caller
  and reverting it alone fails **0 of 14**. Fourth instance of TASK-243's "a funnel with four overrides is
  not a funnel", and it was found by a revert measuring zero rather than by reading.
- **Three duplicate emitters existed because one property was dropped upstream.**
  `SqlIndexManager.ToSqlIndexDefinition` never copied `Unique`, which is the *only* reason
  `CreateUniqueIndexSql` existed on the base plus the PostgreSQL and MSSql managers — and the PostgreSQL copy
  carried the quoted-column defect independently, so `IIndexManager.CreateAsync` could never build a unique
  index there either. Copying one field collapsed four emitters into one producer. The filed plan had proposed
  adding a **fourth** override.
- **An opt-out only one provider can honour is a silent no-op.** `CreateIndexes(..., throwIfExists: true)`
  would have been meaningful on MySQL alone, so `CreateIndexSql` gained `conditional` too — base drops
  `IF NOT EXISTS`, MSSql drops its `sys.indexes` guard, MySQL stops tolerating 1061. Pinned per provider.
- **The close-gate review returned after the commits landed, and three of its four findings were defects in
  them** — filed and fixed as **TASK-249**, +14 tests, **1,100 green** with all four providers live. The
  serious one: this fix's own rule ("enumerate that sink's callers by provenance") applied to one of **two**
  caller-derived sinks, so `SqlIndexBuilder.WithField` in `Birko.Data.Migrations.SQL` still let a migration
  append a statement through a column name. Also: the new guard accepted a `Table.` qualifier *and its test
  pinned that as correct*; `IIndexManager` was left more divergent on MySQL than it started (it bypasses the
  funnel by design, so it needed its own tolerance on **both** verbs); and a comment asserted the very
  invariant the same commit reversed. A late review is still a review.
- **"Nothing in the tree called `CreateIndexes` directly"** — the TASK-204 contract it supposedly preserved
  was documented and entirely unasserted. Narrowing it to what it *meant* (unbuildable = 1062 still throws;
  "already present" = 1061 no longer does, matching the other three) is now pinned by tests in both
  directions. Also spawned **TASK-246**: a migration's `.Unique()` silently builds a NON-unique index on all
  four providers, because `SqlIndexBuilder.Build()` loses the flag the same way — and **TASK-247**, the same
  builder's raw-SQL fallbacks carrying a third and fourth copy of the broken clause.

### A store's first operation inside a boundary silently committed it on MySQL (2026-08-18)

TASK-243, spawned by TASK-242's own regression suite. Stores initialise lazily, so the first data access
issues `CREATE TABLE IF NOT EXISTS` — and after TASK-240 that DDL ran on the ambient boundary's connection.
**MySQL implicitly commits an open transaction on any DDL**, so the boundary was committed before the
caller's own write ran and the rollback undid nothing: 3 rows survived, no error either way. Fixed with
`SupportsTransactionalDdl` (false for MySQL alone) consulted by one DDL funnel; the standing rule is in
§ Conventions above. 24 new tests across four suites; reverts **7 of 38** (MySQL rejoins), **3 of 3**
(suppress unconditionally → SQLite deadlock), **7 of 38** (provider override bypasses the funnel); 19 SQL
suites / 1,129 tests green. Five things worth carrying:

- **The obvious provider-independent fix is a hang.** "Run schema-ensure outside any boundary" was this
  task's own filed first option; measured, it fails 3 of 3 on SQLite with `database is locked`. The two
  halves of the trade land on opposite providers — SQLite *needs* DDL on the boundary's connection, MySQL
  needs it off — which is exactly why the answer is a stated capability and not a rule.
- **A funnel with four overrides is not a funnel.** The base emitters were rewired and the fix measured as
  not working: all four server connectors override `CreateTable(string, IEnumerable<string>)` with their
  own `DoCommand`. Third instance in a fortnight after TASK-215 and TASK-242. Grep `override` before
  believing a funnel is wired, and prove it with a revert.
- **Measure the objection before mitigating it.** Issuing DDL on a second connection looked like a
  metadata-lock hazard worth a much larger fix. One `docker exec` settled it: on MySQL 8.4 an open
  transaction holding a row lock does not block a concurrent `CREATE TABLE IF NOT EXISTS` on that table
  (17 ms).
- **A warm-up in a test is a claim that needs an owner.** TASK-242 added `WarmUpAsync` to three MySQL
  tests with a comment naming this task; closing it removed the warm-up and all 38 pass cold. A warm-up
  whose reason is not written down is indistinguishable from a bug being hidden.
- **Two providers now answer oppositely and both are pinned.** A table created by schema-ensure inside a
  boundary survives the rollback on MySQL and dies with it everywhere else. Asserting both is the record
  of why they are allowed to differ. Also spawned **TASK-245**: MySQL cannot create *any* declared index —
  the base emits `CREATE INDEX IF NOT EXISTS`, a syntax error there, and MySQL is the one provider that
  neither overrides nor supports it. Silent since TASK-204 made schema-ensure record rather than throw.

### Bulk writes escaped every transaction boundary — silently on three providers (2026-08-18)

TASK-242, completing [TASK-240] which wired `AmbientSqlTransaction` into the single-command paths and left
the bulk ones behind. `BulkInsert` / `BulkUpdate` / `BulkDelete` + async twins opened their own connection
and their own transaction unconditionally, and every collection-shaped repository write routes through them
— so create-many, update-many, delete-many, delete-where and delete-all all happened *outside* the caller's
boundary. Measured in consumer Symbio (TASK-442): **20 of 158** boundary-wrapped service operations broke.
Verified here against live **PostgreSQL 16**, **MySQL 8** and **SQL Server 2022**, plus on-disk SQLite; the
standing rule is in § Conventions above. 21 connector methods + 24 store-level scope publications; 43 new
tests across four suites; 19 SQL suites / ~1,105 tests green. Five things worth carrying:

- **The loud provider is not the dangerous one.** On SQLite the escaping write cannot take the boundary's
  write lock, so it blocks for the command timeout and fails — survivable. On PostgreSQL / MySQL / MSSql two
  connections are perfectly legal, so it **committed and survived the owner's rollback with no error
  anywhere**: on the three providers most likely to be in production, the boundary read as working and was
  not. Every assertion counts committed rows after a rollback, because "no exception was thrown" passes
  against the broken code on all four.
- **A rule wired into one layer is not wired.** TASK-240 taught the connectors; the eight provider stores
  override the bulk `*Core` methods and call `Connector.Bulk*` directly, and the base was the only place
  that entered the scope — so `SetTransactionContext` was inert for every bulk write on every provider.
  Reverting just those 24 lines fails 4 of 10 (SQLite) and 3 of 11 (each server). Same shape as TASK-215's
  "wire it per backend does not mean wire it only in backends".
- **Two provider paths were dead on arrival, and only a live server said so.** PostgreSQL's binary COPY
  quoted its column list, so `BulkInsert` had **never** worked for a PascalCase column (sixth instance of
  the identifier family: bare DDL columns case-fold, a quoted `"Name"` cannot resolve). MSSql's
  `command.Prepare()` throws on placeholder parameters with no explicit type, so **`BulkUpdate` and
  `BulkDelete` have never worked on MSSql at all**. Both had to be fixed inline — a regression test that
  cannot reach the behaviour cannot distinguish a fix from a no-op.
- **A shared helper is where per-provider policy gets flattened by accident.** SQLite's bulk path retries
  (CR-M144) and the three servers' never did. `retryWhenOwned` makes that an explicit parameter rather than
  a silent change to three production write paths. Likewise `RunBulkOnConnection`, so COPY and
  `SqlBulkCopy` keep running without a transaction of their own on the owned path — the boundary is the fix,
  not their standalone atomicity.
- **Proving it found a third defect that is nobody's fault here.** On MySQL a store whose *first* operation
  happens inside a boundary silently commits it: lazy `CREATE TABLE` goes through the ambient connection and
  **MySQL implicitly commits on any DDL** (TASK-243, with TASK-244 for the ordering underneath it). The
  MySQL suite warms up and names the reason; PostgreSQL and SQL Server have transactional DDL and are
  unaffected.

### Every worker enqueued its own copy of every recurring job (2026-08-17)

TASK-237. `RecurringJobScheduler` kept `NextRunAt` in process memory, so N workers each concluded
independently that a job was due — N copies, on every backend **including the two that have a lock**,
because nothing consumed `IJobLockProvider`: it appeared in exactly three files, its declaration and its two
implementations. Now wired as leader election, opt-in: pass a provider and only its holder schedules; pass
nothing and behaviour is bit-for-bit unchanged, which is what lets the two shipped consumer call sites stay
untouched. Reverts: un-wiring **5 of 86** offline + **3 of 20** Redis + **3 of 25** PostgreSQL; removing only
the re-baseline **1 of 86**. 166 tests green across 9 job suites. Four things worth carrying:

- **The filed acceptance criterion had its own mechanism inverted, and implementing it literally would have
  shipped the bug it warned about.** It paired "skips enqueueing but still advances `NextRunAt`" with "fires
  immediately on becoming leader" — opposite halves. Advancing keeps a follower *in phase*; not advancing is
  what leaves it overdue. The answer was neither: a follower makes **no scheduling decision at all**, and the
  new leader **re-baselines** (`NextRunAt = now + interval`), because it cannot know what the previous leader
  enqueued. Re-derive a criterion's mechanism before implementing to it.
- **"Has this occurrence already been enqueued?" is an idempotency question, not a mutual-exclusion one.**
  Locking each individual decision cannot work — every process releases right after enqueueing, so one whose
  clock lags arrives later, finds the lock free and duplicates. Closing that means holding until the *next*
  due instant, which is a persistent record, not a lock. The right long-term answer is a unique key on the
  queue (job name + due instant), which all eight backends could enforce rather than the two that can express
  a lock. Recorded, not built: it is a queue-contract change.
- **A test that could not fail was caught by reading it, not by running it.** The cancellation test cancelled
  the loop from outside, which its own `Task.Delay` observes essentially every time — so it passed with and
  without the fix. Only a provider that cancels *during acquire* reaches the path. Same family as this
  epic's recurring finding, arriving in a test this time rather than a checker.
- **A guard's catch has to be narrowed as well as added.** Swallowing every `OperationCanceledException`
  would end the loop on a cancellation belonging to someone else's timeout — permanently stopping scheduling
  over a transient. And the *release* passes `CancellationToken.None` deliberately: the loop exits precisely
  because its token was cancelled, and both providers' `ReleaseAsync` open with `ThrowIfCancellationRequested`,
  so forwarding it would skip the release on the only path that ever runs.

### CosmosDB rendered `.Date` as a JSON sub-property and matched nothing (2026-08-16)

TASK-223 made `CosmosFilterMatrixLiveTests` runnable — it was gated *and* unreachable, because the
framework could not select Gateway mode and the emulator serves nothing else. Its first run ever reported
26 of 27. TASK-224 closed the 27th: `x.When.Date == d` emitted
`WHERE (root["CreatedAt"]["Date"] = "…")`, addressing a member of a *string* (Cosmos stores a DateTime as
ISO text), so the query ran and returned **zero rows with no error**. Now 27/27. Split: unwiring **1 of
54**, gutting the rewriter **3 of 86**; 1,168 tests green across 8 suites. Four things worth carrying:

- **The whole thread was one dark suite deep.** TASK-214 → 218 → 220 → 221 → 222 → 223 → 224, and every
  single defect was found by running a suite that had never run. The last two needed a Docker emulator
  that took a minute to start. **"Needs a live service" is a claim to test, not a reason to skip** —
  Cosmos renders SQL offline, Raven builds RQL offline, and both emulators run in one `docker run`.
- **A per-backend rewrite family now has three members**, all in `Birko.Data.Core/Expressions/` and all
  wired only where measured: `SpanContains` (Mongo, Cosmos), `RavenFilterRewriter` (Raven),
  `DateTruncation` (Cosmos). The shape is settled; the discipline is that the helper is available to
  everyone and the wiring follows a measurement.
- **Handle the whole operator family or none of it.** `.Date` needed all six comparisons plus operand
  mirroring — `d < x.When.Date` inverts silently if only `==` is rewritten, which is the same defect in
  a different coat. Same rule as TASK-215's "guard the whole verb family".
- **Two implementations of one semantics is recorded debt, not an oversight.** The SQL connector has
  had this exact rewrite since Symbio TASK-355 but emits `Condition` objects, so it could not be shared
  as-is. Both operator tables now name the other, and the consolidation (run the pre-pass before the SQL
  parser, delete the method) is written down rather than done as a side effect of a Cosmos fix.

### RavenDB dropped a boolean ternary's WHERE clause entirely (2026-08-16)

TASK-222, the last of the five TASK-214 spawned. RavenDB does not *reject* a boolean ternary — it emits
**no `where` clause at all** and returns every document, or emits malformed RQL like
`where Active = $p0 and`. `ExpressionNormalizer` already existed to desugar exactly that and its own doc
comment excluded the native-LINQ backends; running it for Raven fixes the whole silent class. Split:
Revert A **1 of 51**, Revert B **2 of 70**; 1,147 tests green across 8 suites. Four things worth carrying:

- **The filed hypothesis was wrong in the useful direction.** The task guessed the normalizer would close
  4 of 6 shapes; it closes **1** — the normalizer keeps non-boolean coalesce and arithmetic intact *by
  design*. But probing the silent class found **three more unfiled shapes**, all silent or malformed. The
  count went down and the coverage went up: **count the mechanism, not the symptoms.**
- **Silent beats loud, and they need opposite treatments.** One dropped predicate outranked five loud
  refusals, and the loud five were then *accepted* — computed operands need a Raven static index, not a
  tree rewrite. Fix what lies; document what refuses.
- **A ledger must fail in both directions.** The matrix's accepted-divergence list fails the run on an
  unlisted divergence **and** on a listed one that starts passing. Without the second half an entry
  silently becomes a blanket and masks the next regression in that shape.
- **A unit test proving a transform is not a test that anyone benefits.** Reverting the boolean-constant
  reduction failed 2 of 70 in Core while RavenDB's live suite stayed green at 51/51 — its shape list had
  no literal-branch ternary. Two shapes were added there. Same lesson as TASK-221's dead wiring, from the
  other end: there, the helper was tested and uncalled; here, the transform was tested and unexercised.

### RavenDB could not express `IN`, and its matrix suite was broken in its own setup (2026-08-16)

TASK-220's audit excluded RavenDB from the span rewrite because *every* `Contains` spelling fails there,
not just the array one — a different defect, filed as TASK-221 and fixed here. `IN` is the canonical
batch-load pattern, so the portable spelling that works on SQL, ElasticSearch, MongoDB and CosmosDB threw
on RavenDB alone. `RavenSetMembership` now rewrites it to Raven's own `.In()`. Split: **6 of 51**;
6 suites green. Four things worth carrying:

- **The suite that would have caught it was gated AND broken in setup.** `RavenFilterMatrixLiveTests`
  built its oracle with `ReadAsync(x => true)`, which RavenDB refuses outright — so even with
  `BIRKO_RAVEN_URL` set it threw before reporting a single shape. Worse than TASK-214's plain gating:
  there the suite would at least have run. Its first run ever, after both fixes, reports **21 of 27**;
  the other 6 are TASK-222, one of them a **silent wrong answer** (`ternary` returns 6 rows where C#
  says 1).
- **The live run caught dead wiring that 15 offline tests walked straight past.** The bulk
  `ReadCoreAsync` rewrite had been inserted *inside* `if (_documentStore == null) { return …; }` —
  unreachable. Every non-gated test passed because they call the rewriter directly. **Offline tests pin
  a helper; only an end-to-end run pins that anything calls it.** The Cosmos and MongoDB wirings were
  then checked for the same slip — both fine, verified rather than assumed.
- **The dangerous half of a rewrite is what it must NOT touch.** `x.Tags.Contains("red")` is membership
  in the opposite direction and already worked; a blanket `Contains` → `In` would have broken it. The
  discrimination — which operand references the lambda parameter — was already written in
  `ElasticSearch.ParseContains`, so it was reused rather than re-derived.
- **`ls *.cs` is not a survey of a test project.** I filed the task asserting Raven had no matrix suite
  at all and called that the larger finding. It had one, in a subdirectory. Corrected in the task rather
  than quietly dropped, because the claim had also been reported verbally and an acceptance criterion was
  written against it.

### An array-backed `IN` filter could not be translated on MongoDB (2026-08-16)

TASK-218, the last of the three tasks TASK-214 made visible. `MongoFilterMatrixLiveTests` had never run
until TASK-214 fixed serialization; on its first run it reported 26 of 27 shapes correct, and the 27th was
real. `x => arr.Contains(x.Amount)` over an `int[]` throws `NotSupportedException: Specified method is not
supported` on the driver, while `List<int>` renders `$in` — a look-alike one keystroke away and no warning
either way. The standing rule is in § Conventions above. Split: unwiring the rewrite **2 of 85**, gutting
it **2 of 85 + 2 of 59**; 5 suites green (85 + 59 + 500 + 129 + 69). Four things worth carrying:

- **The measurement was the deliverable, and it both shrank and widened the fix.** The task's first
  acceptance row demanded SQL and ElasticSearch be measured. Both were already correct, so a normalisation
  pass over every translator became one helper. The follow-up audit (TASK-220) then found CosmosDB
  *equally* broken — TASK-218 had waved it off as needing a live service, and it renders SQL offline.
- **A test that calls the helper is not a test that the wiring exists.** TASK-220's first three Cosmos
  tests invoked `SpanContains.Rewrite` in their own render helper; unwiring all six store entry points
  left the suite green at 47/47. Only running the revert exposed it. The fix discriminates on failure
  *phase* — unwired throws at translation before any I/O, wired reaches the network — which needs no
  server.
- **Three symptoms, one cause, found from three directions over three months.** The same overload change
  had already produced an unguarded destructive filter (`PredicateScope`) and a silently-empty SQL result
  (Symbio TASK-249/254). Only writing them down together made it obvious they were one thing — and that
  the unwrap should have one producer.
- **Nine entry points, not thirty hand-offs.** The stores pass a filter to the driver from ~30 call sites
  but the caller's expression *arrives* at nine methods. Normalising on arrival is what makes the guard,
  the whole-collection check and the driver agree, and what makes the next hand-off site correct for free.
- **A test pins the premise.** `arr.Contains(x)` binding `MemoryExtensions` is a runtime fact, not a law;
  if it reverts, the rewrite becomes dead code and that test is the only thing that would notice.

### Two answers for what MongoDB's `_id` is — so every view was silently wrong (2026-08-16)

TASK-219, spawned by TASK-214's close-gate review and picked immediately after it. Yesterday's fix left
`_id` to the driver as an auto-generated ObjectId with the canonical `Guid` beside it;
`Birko.Data.MongoDB.Views`'s translator had always rewritten the `Guid` property to `_id`. Each layer was
self-consistent; together they made **every Mongo view wrong** — measured on MongoDB 7, projecting the
canonical id **threw** and filtering on it returned **0 rows for a document that exists**. Settled by
making the canonical `Guid` **be** `_id`. The standing rule is in § Conventions above. Split: Revert A
(back to ObjectId `_id`) **6 of 84** + **1 of 12**, Revert B (drop the view class map) **1 of 12**;
ungated 84/84 + 12/12, 7 suites green. Four things worth carrying:

- **The cheaper edit was the wrong one.** Patching the translator would have satisfied the filed finding
  and left two ids per document — plus the framework-wide silent-drop reader that tolerating the second
  one required. Resolve the contradiction, don't patch the louder side.
- **The migration objection was measured away, and it was expiring.** Changing an id layout is normally
  expensive; TASK-214 had just proved no write had ever succeeded, so there was nothing stored to
  migrate. That is only true until the fixed stores are used — which is why this was worth doing
  *immediately after* TASK-214 rather than scheduling it.
- **Fixing the entity half did not fix the view half, and the probe said so.** After `SetIdMember` the
  projection worked but the filter still returned 0: `MongoViewStore` renders `$match` through the *view
  type's* class map, where a `Guid?` property still used the global binary serializer and compared
  BinData to a string. A second registration (`MongoViewSerialization`) was needed. **Re-run the whole
  probe after the fix — the first symptom clearing is not the finding clearing.**
- **A projection type is not an entity.** The same registration also clears the id member and pins
  element names, because the driver's `NamedIdMemberConvention` binds a view property called `Id` to
  `_id` — which the projection explicitly suppresses. Found by accident: my first probe named its view
  property `Id` and threw for that reason, which looked like the defect and was not. **Check whether a
  reproduction failed for the reason you think.**
