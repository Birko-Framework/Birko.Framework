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
      **⚠ Superseded by TASK-259: there is no longer a second thing to suppress.** This used to carve the
      legacy `ExternalConnection`/`ExternalTransaction` pair out of suppression, on the grounds that its only
      user was the migrations `SqlSchemaBuilder`, which owns its transaction. That was a blessing of the
      status quo, and the status quo was a defect — the builder published its connection and transaction onto
      a **process-wide cached** connector at three sites and never cleared them, so the runner's `using`
      disposed both and the next store's lazy schema-ensure ran on a dead connection and threw, leaving that
      store permanently uninitialised. `SqlSchemaBuilder` now enters an ambient boundary like everything else
      and the legacy pair is **deleted**, so a migration's DDL is suppressed here on exactly the same terms as
      any other boundary. **One mechanism, one rule.**
    - **The two providers now give opposite answers about whether a created table survives a rollback, and
      both are pinned.** On MySQL it survives (the DDL is no longer in the boundary); on PostgreSQL, MSSql
      and SQLite it is rolled back with it. Asserting both is what stops the next reader "unifying" them
      from symmetry. Whether schema-ensure should be in a caller's unit of work at all is TASK-244, still
      open — and the residue is that a store whose schema-ensure was rolled back still believes it is
      initialised.
- **Schema-ensure PARTICIPATES in the caller's boundary, and a participating schema-ensure is not
  remembered — because "initialised" must mean "the schema is durably there", not "I ran the DDL once".**
  TASK-244, the question TASK-243 deliberately left open, closed on a measurement rather than on taste. Two
  facts settle it and they point in the same direction. First, schema-ensure **cannot** be moved off the
  boundary: TASK-243's revert R2 made that suppression unconditional and all three SQLite lazy-init tests
  failed with `SQLite Error 5: 'database is locked'` — SQLite serialises at the file level, so the DDL has
  to run on the boundary's own connection. Second, the alternative was already broken in the shipped code:
  because `EnsureInitialized()` runs in the public CRUD wrapper and `EnterTransactionScope()` lived only in
  `*Core`, the **`SetTransactionContext` door** never had the boundary published while its schema-ensure
  ran — measured, the DDL took `RunCommandTransaction` (its *own* connection) and on SQLite could not even
  begin: `SQLite Error 5` after the command timeout, from `InitCoreAsync` → `CreateTable` → `DoDdlCommand` →
  `DoCommandWithTransaction`. So the two doors gave different answers and one of them could not work at all.
  Both now enter the scope in `InitCore`/`InitCoreAsync`. Seven parts generalise:
  - **The residue was the actual defect, and it is what a consumer sees.** `AbstractAsyncStore` set
    `_initialized = true` the moment `InitCoreAsync` returned. Inside a boundary that later rolls back, the
    table goes with it and the flag does not — so the same store instance never schema-ensures again and
    writes against a table that is not there, **for the life of the process**. Now
    `_initialized = CanRememberInitialization`, a `protected virtual` hook defaulting to `true` (a backend
    with no caller-owned transaction has nothing that could undo an init) which the SQL stores answer from
    `AbstractConnector.DdlSurvivesRollback`.
  - **The durability question is the provider switch, asked from the other side.**
    `DdlSurvivesRollback => AmbientTransaction == null || !SupportsTransactionalDdl` — no boundary, nothing
    can undo it; a boundary on **MySQL**, where `DoDdlCommand` suppresses the ambient because MySQL
    implicitly commits around every DDL statement (TASK-243), so it is durable *and the store legitimately
    does remember there*; a boundary on PostgreSQL / SQL Server / SQLite, so it is not. Expressed from the
    same two facts `DoDdlCommand` consults, so the two cannot disagree — the one-producer rule applied to a
    *lifetime* rather than to a name.
  - **The four rollback pins now have a fifth sibling per provider, and MySQL's asserts the opposite.**
    That pair of opposite assertions is the record of why the providers are allowed to differ; a suite where
    they all agreed would mean the switch had been "unified" by someone reasoning from symmetry.
  - **Answering "don't remember" costs one idempotent re-run; answering it wrongly costs a database.** The
    asymmetry is the whole design and it is written on the hook: a false negative re-issues
    `CREATE TABLE IF NOT EXISTS` on the boundary's own connection, a false positive leaves a store
    permanently broken. So it errs toward re-running. The invalidate-on-rollback alternative (register a
    callback with the boundary and clear the flag) has no steady-state cost and was rejected here: it is
    correct only if **every** path that ends a boundary without committing is caught, and a missed path
    silently restores the defect — the shape § Conventions keeps recording as "a rule enforced in one of two
    places". Reach for it only with a measured cost to justify it.
  - **⚠ It composes with a swallow, and that is why the consumer saw 200 instead of an error.**
    `SqLiteConnector.OnException` answers "no such table" by calling `DoInit()` and **not rethrowing**, and
    `DoInit` only raises `OnInit`, which nothing in the framework subscribes to. So a write against the
    missing table reports success, stores nothing, and does not create the table — measured. The residue
    loses one operation; the swallow makes that operation answer *success*. Owned by [[TASK-277]], pinned
    meanwhile by a test that asserts the defect so it cannot be believed fixed.
  - **A consumer's one-off observation was reproduced from the ordering, not chased.** Symbio (its TASK-527)
    reported a wiped SQLite database where `POST /api/auth/setup` returned 200 while the `Users` table was
    never created, every table written *after* it existed and was populated, and a restart did not recover
    it — and it did **not** reproduce on demand (four from-scratch bring-ups succeeded). Rather than hunt
    the trigger, the ordering was traced in source and the symptom rebuilt deterministically in
    `SchemaEnsureRollbackResidueTests`. **When an observation cannot be reproduced, reproduce the mechanism
    it implies instead** — and note which half explains which symptom: the residue explains the lost row,
    the swallow explains the 200, and neither explains it alone.
  - **The per-store door's own failure mode is loud, so it was NOT the consumer's.** On SQLite it throws
    `SQLite Error 5` — a 500, not a 200. Worth stating because the tempting conclusion ("the DDL ran on
    another connection, that's the bug") is measurably the wrong half.
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
    `'Ts'`. It is the only case-folding in the SQL connectors, and correctly so: everywhere else an
    identifier is emitted *as* an identifier and PostgreSQL folds it, so there is nothing to pre-fold. Grep
    for that asymmetry before assuming a new sink belongs to the quoting rule. **The fold now lives in
    `AbstractConnectorBase.CatalogueNameLiteral`, gated on `FoldsUnquotedIdentifiers`, not inline in
    `TimescaleDBConnector`** — see the entry below.
  - **The tell that a sink belongs here is a quoted literal, not a name.** Ask whether the parser will ever
    see the text as an identifier; if it will not, neither half of the bare/quoted convention applies and the
    sink needs its own answer. And **a default value can hide the folding half indefinitely** — this one was
    masked because the shipped `TimeColumn` was already lowercase and matched a folded property by luck, so
    the only configuration anyone ran was the one that worked.
- **Those two treatments have ONE producer each, on the connector — and the escaping underneath them has one
  producer for the whole framework.** The rule above was correct and stated in exactly one method; TASK-253
  found `Birko.Data.Migrations.TimescaleDB` had independently written the same `create_hypertable` call with
  **no escaping at all**, plus eight sibling emitters nobody had looked at. A rule with one statement and two
  implementations is a rule that will be got wrong again, so:
  - **`AbstractConnectorBase.RegclassLiteral(name)`** — quote as an identifier, then escape for the literal.
    For any argument the parser re-reads as an identifier *after* unwrapping the literal: `create_hypertable`,
    TimescaleDB's four policy functions, `refresh_continuous_aggregate`.
  - **`AbstractConnectorBase.CatalogueNameLiteral(name)`** — pre-fold, and **never** quote. For a `name`
    compared textually against a catalogue column. Quoting here is not merely redundant but wrong: the
    comparison is textual, so `'"Ts"'` is looked up *with* its quotes and matches nothing.
  - **`FoldsUnquotedIdentifiers`** is the provider capability the fold consults — `true` for PostgreSQL alone,
    in the same family as `SupportsTransactionalDdl` and `IsMissingTableException`: stated once, consulted by
    one producer, never re-derived per call site. It reads `true` at every sink that exists today, so **assert
    the `false` side on a non-folding provider** or the capability is indistinguishable from an unconditional
    fold and can be deleted with no test noticing.
  - **`Birko.Data.SQL.SqlLiteral.EscapeLiteral`** is the `''`-doubling rule, once, for the framework. It was
    hand-written in 21 places (four index managers, `MSSqlConnector`, `DataBase.InlineConstant`,
    `SqlBuilderContext.EscapeValue`, `ViewSelectSqlBuilder`); 18 were converged and the one left in
    `Birko.Data.Migrations.CosmosDB` is named in the helper's doc, so a later audit can tell a decision from
    an oversight.
  - **It covers TWO kinds of text, and conflating them is what nearly kept them apart.** A name the grammar
    only accepts as a literal, *and* a constant in a statement that takes no parameters at all — `CREATE VIEW`
    is the one that matters, so `InlineConstant` and `FormatJoinConditionValue` have nothing to bind to. The
    escaping rule is identical for both, which is why one producer serves them; the plan for TASK-253 said to
    keep the value sites separate and **reading them inverted that**, because three of the four *document*
    that parameters are unavailable to them. It is still not a licence to interpolate a value that could be
    parameterised.
  - **The escaper refuses null rather than escaping it to empty.** Returning `string.Empty` looks
    accommodating and is the silent half of § SH-H037: converged onto 18 sinks it would turn a null identifier
    into an *empty* one — a malformed statement where the hand-written `Replace` threw. It is reachable
    (`Tables.IndexDefinition.Name` is declared `= null!`), and nothing passes null legitimately.
  - **A fourth position exists and is not an identifier at all.** `compress_orderby`, a time bucket, an
    INTERVAL — these are expression fragments, so they get escaping **only**: not folded (the parser folds
    them itself) and not identifier-validated, because `ts DESC` and `date_trunc('day', x)` are legitimate
    values. Sitting inside a literal, escaping contains them completely. Two arguments in
    `BuildContinuousAggregateSql` are **raw SQL in statement position** and cannot be contained at all — that
    is a property of the parameters, not a gap, and [[TASK-260]] owns changing the API's shape rather than
    bolting a validator onto it.
  - **Complete containment rests on `standard_conforming_strings = on`** (PostgreSQL's default since 9.1, and
    the ANSI behaviour elsewhere). With it off, backslash escapes revive and `\'` breaks out. Every
    literal-interpolating sink has always depended on this; it is written down on `SqlLiteral` rather than
    assumed.
- **A provider whose paging syntax has a precondition needs that precondition supplied where it is KNOWN,
  not where the clause is rendered — and a feature nobody tested is a feature nobody has.** TASK-278.
  `MSSqlConnector.LimitOffsetDefinition` emitted `FETCH NEXT n ROWS ONLY` and prepended `OFFSET` only when
  the caller supplied one. Measured on SQL Server 2022: `FETCH` alone is **Msg 153**, and
  `OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY` without a sort is **Msg 102** — while `TOP (n)` and any real
  `ORDER BY` both work. So **every limited read on SQL Server emitted invalid T-SQL**, including
  `ReadFirstAsync`, which `Birko.Data.SQL/CLAUDE.md` § Conventions actively tells consumers to use for a
  single row. Five parts generalise:
  - **The information lives one layer up, so the fix goes one layer up.** Whether the caller supplied a sort
    is known to `CreateSelectCommand`, not to the tail emitter — so `RequiresOrderByForPaging` (false;
    **true on MSSql alone**) makes the *composer* synthesise `ORDER BY (SELECT NULL)` when there is a limit
    and no sort. Threading "was there an ORDER BY" into `LimitOffsetDefinition`'s signature was rejected for
    a specific reason: it is `public virtual`, so adding a parameter would leave any existing override of
    the old signature **silently no longer overriding anything** — the quiet half of § SH-H037, arriving
    through a signature change.
  - **`TOP (n)` was the tempting fix and it is a second code path.** It needs no sort and would have covered
    the no-offset case, but it lives in the SELECT list rather than the tail, and an offset still forces the
    `OFFSET`/`FETCH` form and therefore the sort. One mechanism that covers both beats two that split by
    argument shape.
  - **Synthesising a sort preserves cross-provider behaviour rather than inventing one.** A limited read with
    no `ORDER BY` returns arbitrary rows on SQLite, PostgreSQL and MySQL too; SQL Server just refuses to
    pretend otherwise. So the placeholder makes the four providers agree instead of making one of them
    special — and a caller who cares which rows they get must pass a sort everywhere.
  - **There was NO paging coverage in any suite, and that is why this survived.** Not thin coverage —
    none. A defect that makes a documented API unusable on a whole provider had no test to fail. When a
    provider-specific clause has no test anywhere, assume it is broken somewhere until measured.
  - **The capability's false side is what catches an over-broad fix.** Making `RequiresOrderByForPaging`
    unconditionally true leaves the MSSql suite **green** and fails SQLite's and the base's assertions —
    which is the only signal that the flag became a blanket. Both sides are asserted per provider, in the
    family of `SupportsTransactionalDdl` / `FoldsUnquotedIdentifiers` / `SupportsPartialIndexes`.
- **A WRITE that cannot be applied must never report success — and "recover and continue" is not a thing a
  handler can do if it neither repairs nor retries.** TASK-277, the sibling of the rule below and the half
  that turns a lost operation into a lie. All four providers' `OnException` handlers answered a missing
  table with `DoInit()` and a **return**: the statement was discarded and the caller told it had worked.
  Measured on SQLite as `CreateAsync` returning a non-empty `Guid` against a table that does not exist and
  is not created; the same shape on PostgreSQL, MySQL and SQL Server. Now
  `AbstractConnector.EnsureSchemaAndReport` — one producer, called by all four handlers — ensures the schema
  and then **always throws**. Six parts generalise:
  - **A recovery branch that neither repairs nor retries is only a swallow.** `DoInit()` raises the
    `OnInit` event and **nothing in the framework subscribes to it** (only a consumer can, via
    `IDataBaseRepository.AddOnInit`), and the failed statement was never re-executed either way. So the
    branch could not fix anything even in principle. **Check what a recovery call actually does before
    treating it as recovery** — the giveaway here was an event with no framework subscriber.
  - **`DoInit()` is still called, and then it throws.** A consumer that registered a handler gets its schema
    ensured, so the caller's *next* attempt can succeed, while this attempt is reported. Keeping the
    extension point is free; keeping the silence was not.
  - **The read side is a DIFFERENT decision and is untouched — because the read path never reaches this
    handler.** `RunReaderCommandOn` catches `IsMissingTableException` itself and yields break, so an empty
    result for a read is TASK-211's contract with its own stated callers (lazy create-on-first-use,
    view-existence probing, CR-M149). The asymmetry — write throws, read answers empty — is pinned by a test
    on each side, so unifying them means deleting an assertion that says why they differ.
  - **The blast radius was measured before shipping, and it was zero.** Making writes throw broke **1** test
    across twelve suites: the defect-pin written under TASK-244, which this task **inverted rather than
    replaced** — the before/after pair on one test is the record. Compare TASK-211, whose narrowing broke
    two suites that asserted the wide behaviour; a removal that breaks nothing is a swallow nothing relied on.
  - **MSSql's handler was still classifying by raw message substring** (`"Invalid object name"`,
    case-sensitively) — the shape TASK-211 removed from PostgreSQL and MySQL and never got to. Routing all
    four through `IsMissingTableException` is narrower *and* case-correct, and it means the reader and the
    handler cannot disagree about what a missing table is.
  - **⚠ `Should().NotBeNull()` on a bulk-store read is a vacuous assertion, and it hid a live defect.** The
    bulk `Read(filter)` overload hides the single-result one and returns the **collection** (§ Conventions),
    so that assertion passes on an empty enumerable. Strengthening it to assert the row surfaced
    [[TASK-278]] immediately: on SQL Server a limit with no offset is `Msg 153` and offset+limit without an
    `ORDER BY` is `Msg 102`, so `ReadFirstAsync` — the call § Conventions recommends for a single row —
    **cannot work there at all**. Grep for that assertion shape; where it appears on a collection-returning
    read it is measuring nothing.
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
- **A value that a driver INFERS a type for and a value the framework types EXPLICITLY are two producers, and
  the inferring one fails quietly.** Same one-producer family as the identifier rules above, at the layer where
  a *value* is bound rather than a name emitted. A Birko `DateTime` maps to `TIMESTAMP` — timezone-less — on
  PostgreSQL, and the two write paths disagreed about a `Kind=Utc` value: `AddParameter` binds **no** `DbType`,
  so Npgsql infers `timestamptz` and the server casts it into the column **through the session's `TimeZone`**,
  while the binary `COPY` writer passes `NpgsqlDbType.Timestamp` explicitly and Npgsql **refuses** the value.
  Measured on PostgreSQL 16 / Npgsql 10.0.3: `CreateManyAsync` **threw** for every UTC-kinded entity, and
  `CreateAsync` **silently stored 11:30 for a 10:30 UTC value** on a UTC+1 server (TASK-256). The rule is now
  stated once and enforced at both **un-prepared** boundaries by
  `PostgreSQLConnector.NormalizeTimestampValue`: **a Birko `DateTime` column on PostgreSQL stores the
  wall-clock components of the value as supplied; `Kind` is not persisted and every read returns
  `Unspecified`.** Eight parts generalise:
  - **The loud path is not the dangerous one, and the filed finding will name the loud one.** The task scoped
    itself to `COPY` and asserted `CreateAsync` "works" — true only on a UTC-configured server. Fixing the
    named half alone would have left the two paths storing **different instants**, so a bulk-written row would
    not match a filter bound through the parameterised path. **When two paths bind the same value differently,
    fix both or neither**; the quiet one is where the wrong answer lives.
  - **Where a provider infers, the framework must decide — a driver's inference is a second producer.** Nothing
    in `AddParameter` sets a `DbType` (deliberately, so enums bind as their underlying integral), so the
    driver's own type inference silently became the framework's type policy. That is the same shape as
    § *where a driver has no usable default, the framework picks one*, arriving through inference rather than
    absence.
  - **On a correctly-configured server the silent half is UNOBSERVABLE, so it needs a deliberately
    misconfigured one.** Both paths store `10:30` on a UTC server whatever the code does, so reverting the
    parameter fix fails **nothing** there — a revert that fails nothing is a missing test (§ TASK-248). The
    test stands up a **dedicated throwaway database** with `TimeZone` set non-UTC, because
    `PostgreSqlSettings.GetConnectionString()` emits no `Timezone` key and offers no raw escape hatch, so
    `SET TimeZone` on a test's own connection **cannot** reach the store's. **`NpgsqlConnection.ClearAllPools()`
    after the `ALTER DATABASE` is mandatory** — measured, a pooled connection otherwise keeps `Etc/UTC` and the
    test silently measures nothing. Dedicated rather than shared so no concurrent suite inherits the GUC.
  - **The framework's own base model produced the value its own connector refused.**
    `Birko.Data.SQL/Models/AbstractLogModel.cs` initialises `CreatedAt`/`UpdatedAt` from `DateTime.UtcNow`, so
    this was never a consumer's exotic choice — it was every `AbstractDatabaseLogModel` descendant. **Check what
    the framework's own canonical models emit before calling a value shape unusual.**
  - **`TIMESTAMPTZ` is the semantically honest type and was still the wrong answer — measured, twice over.**
    It round-trips `Kind=Utc` correctly, and it was reopened precisely because it is cheapest while no
    PostgreSQL data exists. Rejected because it makes PostgreSQL the **only** tz-aware provider (SQLite numeric,
    MySQL `DATETIME`, MSSql `DATETIME2` all store wall clocks), so a **SQLite-green test would stop proving
    PostgreSQL behaviour** for a product that tests on one and deploys on the other; because it **breaks the
    `Unspecified` cell** (`10:30` in → `09:30Z` back); and because `ALTER COLUMN … TYPE TIMESTAMPTZ`
    reinterprets stored values in the session TZ *at ALTER time*, silently shifting every existing row unless
    run under `SET TimeZone TO 'UTC'`. **Uniformity across providers beat per-provider correctness**, because
    the test provider and the production provider differ.
  - **Fail-fast was the wrong instinct here, and § SH-H037's own precondition is what said so.** Binding an
    explicit type so the parameterised path *throws* like `COPY` did would refuse a write from **every**
    framework entity. § SH-H037 requires the blast radius to be cleared first; here it says no — the second
    inversion of that rule after TASK-248.
  - **A rule is cheap to adopt when the consumer already assumes it.** Symbio's `UtcDateTimeJsonConverter`
    already treats an `Unspecified` value from storage **as UTC** and converts for display through
    `Birko.Time`'s `ITimeZoneConverter` from a UTC baseline — so the "caller re-attaches the `Kind`" half was
    written and shipped before this task existed. **Read the consumer's own conversion layer before choosing a
    storage contract**; it may already have picked one.
  - **The fix rests on an uncompiled premise, so it is written down and pinned.** Stripping `Kind` from *every*
    bound `DateTime` is safe only because `DateTimeField` hardcodes `DbType.DateTime`, no attribute in
    `Attributes/Field.cs` can override a field's `DbType`, and no field class produces `DbType.Date`, `Time` or
    `DateTimeOffset` — so `ConvertType`'s `TIMESTAMPTZ` arm is **unreachable from a model** and there is
    currently no way to persist an instant with its offset at all. [[TASK-263]] adds that opt-in and
    **falsifies the premise**, so it must revisit the helper; a test asserts the premise so the failure lands
    there rather than in a shifted timestamp.
  - **A sink can be correct by a DIFFERENT mechanism, and then that mechanism is what needs the test.**
    Found at this task's own close gate: the bulk update and delete paths bypass `AddParameter` entirely
    (pre-create parameters holding `DBNull.Value`, `command.Prepare()`, then assign `.Value` per row), so
    six binding sites are *structurally* outside the funnel. They are nevertheless unshifted, because
    `Prepare()` pins each parameter to the target column's real type before any value is assigned and the
    driver therefore never re-infers `timestamptz`. **The right response was neither to wire them nor to
    wave them off, but to pin the mechanism** — a test asserts the un-shifted write, so dropping `Prepare()`
    or adding a seventh site without it fails loudly instead of silently shifting. The first draft of this
    rule claimed "every write boundary strips `Kind`", which the measurement falsified; **the word that made
    it true was `un-prepared`, and it was earned by measuring rather than reasoning** (§ TASK-258). Note the
    mechanism is provider-specific: on MSSql `Prepare()` throws on untyped placeholders, which is why those
    same paths have never worked there.
- **A column has ONE meaning, so a type that can mean two things needs an opt-in — and the opt-in promises only
  what the weakest provider can keep.** The pair to the rule above, and only comprehensible with it. A plain
  Birko `DateTime` column is a **wall clock**; a `[UtcField]` one is an **instant**, stored in the provider's
  timezone-aware type where one exists and read back as `Kind=Utc` on every provider (TASK-263). Both meanings
  coexist per property on one entity. `ConvertType` had mapped `DbType.DateTimeOffset` to `TIMESTAMPTZ`
  (PostgreSQL) and `DATETIMEOFFSET` (MSSql) since long before, but **nothing could reach it** —
  `CreateAbstractField` had no arm and no attribute can override a `DbType` — so TASK-256's rule named an escape
  hatch that did not open. Seven parts generalise:
  - **State the promise the weakest provider can keep, not the one the best column type suggests.** MySQL's
    `DATETIME` and SQLite's numeric affinity cannot carry an offset, and **a field cannot behave differently per
    provider** — `Tables.Table` holds no connector and `AbstractField.Read` is reached through the
    provider-blind `DataBase.Read`. So the promise is *the instant is exact and reads back as UTC*, and a
    caller's original offset is normalised away **uniformly, everywhere, including on the two providers that
    could have kept it**. Deliberate: the product tests on SQLite and deploys on PostgreSQL, so a behaviour that
    differed between them would make a green test meaningless — the same trade TASK-256 recorded when it
    rejected mapping every `DateTime` to `TIMESTAMPTZ`. **Uniformity beat per-provider fidelity twice in a row,
    for the same reason.**
  - **That is also why the opt-in is an attribute on a `DateTime` and not a `DateTimeOffset` property.** The CLR
    type would *advertise* an offset that half the supported providers cannot honour, and an API that
    over-promises is worse than one that states its limit. It is cheaper for consumers too — an existing
    `DateTime` model opts in per property with no type change. **When a type would lie about the contract, put
    the contract in an attribute and leave the type honest.**
  - **Two features composed only because of a bound value's CLR TYPE, and that is now load-bearing.**
    TASK-256's `NormalizeTimestampValue` strips `Kind` from *every* bound `DateTime` on PostgreSQL, on the
    premise that none could target a `timestamptz` column — which this task falsified. `AddParameter` takes
    `(command, name, value)` and cannot know the target column, so the resolution is that
    `UtcDateTimeField.Write` returns a **`DateTimeOffset`**, which that helper's `is DateTime` test does not
    match. No signature change, no field context, no per-provider plumbing. Measured cost of getting it wrong:
    reverting to a bare `DateTime` stores an instant **an hour out, silently**, and only a non-UTC server can
    see it. **Where a fix rests on a value's type rather than on a check, assert the type in a test** — an
    invariant nothing enforces is a comment.
  - **A read path is where providers diverge, so pick the spelling that works on all of them and say why.**
    `GetDateTime` is wrong or fatal on three of four: it **throws** `InvalidCastException` on MSSql's
    `datetimeoffset`, returns `Kind=Local` on SQLite and `Unspecified` on MySQL. Only
    `GetFieldValue<DateTimeOffset>(i).UtcDateTime` is exact everywhere. The obvious implementation would have
    passed on PostgreSQL and failed outright on MSSql — **measure the read, not just the write.**
  - **An attribute that cannot be honoured must refuse, not be ignored.** `[UtcField]` on a non-`DateTime`
    property throws `FieldAttributeException` naming the property and its CLR type. Silently dropping it leaves
    the model declaring an instant while the column stores a wall clock, with nothing to notice — § SH-H037 in
    its quietest form.
  - **Record which arms of a public mapping are deliberately unreachable.** `DbType.Date` (a `DateTime` is a full
    timestamp; truncating it is the CR-H086 bug), `DbType.Time` (`TimeOnly` maps to `DbType.String` — see
    `TimeOnlyField`) and `DbType.DateTimeOffset` from a CLR `DateTimeOffset` property are all unreachable **by
    design**, written down at `CreateAbstractField` because that dispatch is the only producer of fields.
    `ConvertType` keeps answering them because it is public surface a consumer may call directly. **A gap that
    is a decision reads exactly like an oversight unless you say so.**
  - **A declared column type that disagrees with what is stored is recorded, not quietly fixed.** SQLite declares
    `INTEGER` for this and Microsoft.Data.Sqlite stores ISO-8601 *text*. Misleading, and left alone: plain
    `DbType.DateTime` declares `INTEGER` and stores text too, so changing only the new one would make it diverge
    from its neighbour. Pinned by a test asserting both the declaration and `typeof()`, so a later change in
    either surfaces there rather than as a wrong instant downstream. Same discipline as the accepted-divergence
    ledgers in § TASK-222 and § TASK-245.
- **A query against another product's catalogue has an expiry date, and nothing in the type system says so —
  write the version down.** `GetChunkInterval` read `chunk_time_interval` from
  `timescaledb_information.hypertables`, which was presumably right on TimescaleDB 1.x. **2.0 moved the value
  to `timescaledb_information.dimensions` and renamed it `time_interval`**, so the method raised `42703` on
  every 2.x server — i.e. every supported version — and it is not swallowed, so a migration calling it failed
  outright (TASK-261, measured on 2.29.2 / PostgreSQL 16.15). Distinct from the identifier family: not quoting
  or folding, but **catalogue drift**, which is why it was filed as its own task rather than absorbed into
  TASK-253. Four parts generalise:
  - **The version the query targets belongs in the remark.** Nothing marked the old spelling as having an
    expiry, and a catalogue column is exactly the kind of dependency that changes under you between minor
    releases of somebody else's product. Naming the measured server turns a silent future break into a
    readable one.
  - **A view with one row per sub-object needs its row pinned, and `ExecuteScalar` will not tell you.**
    `dimensions` holds one row per dimension: for a space-partitioned hypertable, dimension 1 is the time
    column with the interval and dimension 2 is the space column with **both** interval columns NULL. So the
    unrestricted query returns 2 rows of which 1 has a value, and `ExecuteScalar` silently takes the first.
  - **⚠ And the restriction is DEFENSIVE, not witnessed — say which, because a revert that fails nothing is a
    missing test.** Removing `AND dimension_number = 1` fails **0** tests: measured, the view carries its own
    `ORDER BY`, so the right row comes back first today. The clause is kept because correctness without it
    rests on an ordering the query does not state and the catalogue does not promise — the same bet that
    produced this defect. The hazard is pinned by asserting the **catalogue shape** (2 rows, 1 interval)
    rather than by pretending the reader witnesses it. Compare § TASK-248, where a revert failing 0 meant a
    genuinely absent test; here it means the clause guards a future, and the distinction has to be written
    down or the next reader deletes it as dead weight.
  - **A NULL from a catalogue means "not this shape", not "not configured" — and the discriminator may not be
    the column that names the shape.** An integer-partitioned hypertable has `time_interval` NULL and its
    width in `integer_interval`, so returning null would claim no chunk interval is configured when one is;
    the reader coalesces. But it must not branch on `dimension_type`: measured, an integer-partitioned
    dimension still reports `dimension_type = 'Time'` on 2.29.2. **Check that the column which appears to
    describe the shape actually discriminates it.**
- **A name a CALLER supplies may be qualified; a name the framework resolved never is — and quoting the whole
  string conflates them.** Tenth instance of the identifier family (TASK-262), and the one that arrives from
  reuse rather than from a new sink. `QuoteIdentifier` quotes its argument as **one** identifier, which is
  right for a column and for a table name taken from `Table.Name` — that is never qualified. TASK-253 routed
  the TimescaleDB migration emitters through it, and a migration author's `reporting.evts` became
  `'"reporting.evts"'`: a request for a single table whose name literally contains a period. Measured on
  TimescaleDB 2.29.2 / PostgreSQL 16.15 — `42P01`, which `IsMissingTableException` classifies as a missing
  table, so the handler can **swallow it and report success**. Six parts generalise:
  - **The tell is provenance, not position.** Both callers of these producers emit the same statements; what
    differs is where the name came from. The store passes `Table.Name` (framework-resolved, never qualified);
    a migration passes author text (qualification is idiomatic there and nowhere else in the changed surface).
    **When a helper gains a second caller, ask what its argument now means** — TASK-253 carried a premise one
    layer up without noticing it stopped holding.
  - **`AbstractConnectorBase.QualifiedIdentifier` is the one producer, and it splits on UNQUOTED dots only.**
    Per-part quoting is *strictly more capable* than the bare name that preceded TASK-253: measured,
    `'"reporting"."Evts4"'` and `'"Rep Ort"."Ev ts"'` each created a hypertable, and a bare qualified name
    reaches neither mixed case nor spaces. `RegclassLiteral` composes on top of it; the three bare-SQL
    positions (`ALTER TABLE`, `CREATE MATERIALIZED VIEW`, `FROM`) were retargeted to it. **`QuoteIdentifier`
    itself is unchanged** — it is the single-identifier producer framework-wide, and splitting there would
    alter column and table quoting everywhere.
  - **State the trade and measure it.** Splitting gives up a table whose name literally contains a dot —
    *unless the caller quotes it*, which the unquoted-dot rule preserves, so `"a.b"` stays addressable as one
    part. That escape hatch is the reason to split on unquoted dots rather than on every dot. Cost measured
    rather than assumed: **0 of 317** `[Table("…")]` declarations across the framework, its tests and all 16
    consumer repos contain a dot.
  - **A scanner over quoted text must know the provider's delimiters, and asymmetric ones are the case it
    gets wrong.** `IdentifierQuoteOpen`/`Close` are exposed once and overridden beside `QuoteIdentifier` —
    ANSI `"` by default, backticks on MySQL, `[`/`]` on MSSql, where open and close **differ** so a
    same-character scanner mis-detects the quoted part. All three shapes are covered offline even though only
    the PostgreSQL family has callers today, because a helper on the shared base gets used by the layer you
    did not think about. A test fake that overrode `QuoteIdentifier` without the delimiters was corrected in
    the same change: an unfaithful fake is how such an override goes untested.
  - **A shared producer can make an opt-in impossible, and that is the honest reason to document a limit
    rather than build one.** `CatalogueNameLiteral` pre-folds a column name because framework DDL emits
    columns bare — so a hand-created *quoted mixed-case* column is unreachable through these emitters
    (`42703`, measured). The fix cannot be "fold only when the caller wrote it unquoted", because the same
    producer serves the store, where an unquoted name means *the quoted identifier this framework created*
    (TASK-472). Teaching unquoted to mean "fold me" would re-break that defect — which was invisible
    precisely because its failure is swallowed. With **0** emitter call sites across 16 consumer repos, an
    explicit opt-out on seven methods is speculative API; the remarks name it as the shape to add when a real
    caller appears. **Record the limit and why it is a limit, so the next author meets a decision.**
  - **Supporting a qualified string is not the same as having a schema, and conflating them would have turned
    a P2 regression fix into a feature.** The framework has no schema concept at all — measured:
    `Attributes.Table` takes only `Name`, `Tables.Table` holds none, no `Settings` class has one. Where that
    goes is settled but separate ([[TASK-272]]): **identity on the table, rendering and capability on the
    connector**, because a connector is cached per (type, settings id) and can hold only *one* schema (a
    search path, not qualification), while `Tables.Table` holds no connector and so cannot quote per provider
    — the seam `Table.GetSelectFields(…, Func<string,string>? quoteTable)` already demonstrates. And
    `SupportsSchemas` would be genuinely two-sided: true on PostgreSQL and MSSql, false on SQLite and on
    MySQL, where `CREATE SCHEMA` creates a **database** (measured: it produced a sibling of `birkoview`).
- **A bare-emitted identifier has no enclosure, so its containment is REFUSAL — the third mechanism, and the
  guard that provides it is separated from its sibling by the MESSAGE, not by the check.** Eleventh instance
  of the identifier family (TASK-255), and the one that names the mechanism the previous ten kept implying.
  `BuildContinuousAggregateSql` hardcoded its bucketing column as the literal `time` — CR-H070's defect,
  fixed in `BuildCompressionPolicySql` four methods above and left in this one by the very commit that fixed
  it (`531d816` edited the defect line while naming the finding). No framework-created table can have such a
  column, since column definitions are emitted bare and every Birko entity is PascalCase, so **no continuous
  aggregate over a framework-created table could ever be built** — measured latent: 0 of 16 consumer repos
  call it, 1 compiles it. Six parts generalise:
  - **Ask which of FOUR positions the argument occupies, never which sibling it resembles.** This file now
    holds all four: a `regclass` inside a literal (`RegclassLiteral`), a `name` inside a literal compared
    against a catalogue column (`CatalogueNameLiteral`, pre-folded), a real *table* identifier
    (`QualifiedIdentifier`, quoted), an expression fragment (`EscapeLiteral` only) — and now a real *column*
    identifier, which § *quote tables, never columns* requires to be **bare**. Bare means no quote character
    encloses it, so escaping contains nothing and the only containment left is a whitelist that refuses.
    **Measured, not reasoned**: rendering it with `QuoteIdentifier` fails the live tests, because
    `CreateBaseTable` emits `(Ts timestamptz …)` bare inside a quoted table and PostgreSQL stores `ts`.
  - **`DataBase.ValidateColumnIdentifier` shares `_unqualifiedIdentifier` with
    `ValidateIndexFieldIdentifier` and differs only in what it says.** Reusing the index guard was the
    obvious move and it ships a refusal telling a migration author about `CREATE INDEX` and an index column
    list — § SH-H037/TASK-215's *"a refusal names the door THIS caller has"*, the rule about an async twin
    naming `DeleteAll()`. So the **regex is the shared thing and the wording is the separate thing**, and
    both halves are asserted: one test proves the two guards accept and reject identically, another proves
    the message does not say "index". Pointing one at the other reds exactly the second.
  - **The tier is honest about what it cannot do.** This is the *weaker* fallback, used where there is no
    entity type to resolve against (`TimescaleDBMigration` holds a table name only), so it cannot fix a
    `[NamedField]` remapping. What it guarantees is that a bare identifier reaches `CommandText` — every
    payload carries a space, operator, parenthesis or separator — and that a bare identifier naming no
    column is *a wrong answer that reports itself*. Do not oversell a whitelist as resolution.
  - **A default the COMPILER can forbid beats a default a test forbids.** Placing the required parameter
    *before* the existing optional one makes `timeColumn = "time"` a `CS1737` — not expressible at all. So
    the reflection pin on `HasDefaultValue` is **defensive, not witnessed** (§ TASK-261), guarding a future
    reordering; say which, or the next reader deletes it as dead weight. **The cost is the other half:**
    every parameter here is `string`, so a pre-existing *5*-argument call fails loudly (`CS7036`) while a
    *6*-argument one **silently rebinds**. Measured on this very change — 6 of 8 call sites failed loudly,
    **2 rebound silently**. Affordable only because the blast radius was 0 non-test callers; it is not a
    manoeuvre to copy where consumers exist.
  - **Measure a precedent's MOTIVATION, not its shape.** Criterion-shaped reasoning said "follow the
    sibling exactly", and the sibling defaults `orderByColumn` to `"time"`. `git show 531d816` shows the
    parameter *did not exist* before that commit, so the default was a **source-compatibility artefact**,
    not a judgement that the value is good — and there was nothing left to stay compatible with. The file's
    convention for a *time-dimension column* is the opposite and older: `CreateHypertable` /
    `CreateHypertableWithSpace` require theirs, in four signatures. **Two conventions in one file split by
    what kind of argument it is**, so "imitate the neighbour" is only safe once you know which neighbour.
    The sibling's own default is the same defect and is owned by [[TASK-279]] rather than fixed from
    symmetry.
  - **A guard declared in `Birko.Data.SQL` is tested in `Birko.Data.SQL.Tests`, not only from its
    consumer's suite.** TASK-257's close gate caught the identical omission for `AbstractField.IsInIndexKey`;
    a guard whose only coverage lives downstream is one a downstream cleanup can delete silently.
- **A statement the server refuses inside a transaction is a provider limit the framework must ROUTE
  AROUND, and the statements in one family will not all need the same treatment — one may be fixable in
  place and its neighbour not at all.** TASK-281. `SqlMigrationSettings.UseTransaction` defaults to `true`,
  and the runner therefore opens a transaction around every migration — so any statement PostgreSQL or
  TimescaleDB refuses in a transaction block could only ever fail through the only path a real migration
  takes. Measured on TimescaleDB 2.29.2 / PostgreSQL 16.15, both continuous-aggregate statements raise
  **SQLSTATE `25001`**, and the two need opposite answers. Six parts generalise:
  - **Measure the whole family before designing, because the members diverge.**
    `CREATE MATERIALIZED VIEW … WITH (timescaledb.continuous)` refuses only because it performs an *initial
    refresh*, so **`WITH NO DATA` fixes it in place**; `refresh_continuous_aggregate()` has no such escape
    and **cannot be made transactional at all**, so it is refused with a message instead. Treating them
    alike — either both refused, or both "fixed" — would have shipped a needless limitation or a statement
    that still fails.
  - **The idiomatic third mechanism is the one to look for, and its absence is what makes a limit look
    fatal.** The plan concluded a transactional migration *could never populate an aggregate*, which was
    false: `add_continuous_aggregate_policy` **is** transaction-safe (measured, job survives the commit),
    and a refresh *policy* — not a manual refresh — is how TimescaleDB intends an aggregate to be kept
    current. The framework simply had no emitter for it. **When a fix appears to cripple a feature, check
    whether the provider offers a different mechanism before accepting the limit** — and if it does, the
    emitter for it belongs in the same change, because documenting a remedy the framework cannot perform is
    TASK-263's *"named an escape hatch that did not open"*.
  - **Emit the safe form UNCONDITIONALLY, not "only when a transaction is present".** The conditional
    version makes one migration yield a populated or an empty view depending on a settings flag, with
    nothing at the call site saying which — § TASK-274's *two doors onto one feature must give one answer*.
    Uniformity costs the non-transactional caller its populated view and buys a rule that can be held in
    the head; the cost was affordable because it was **measured at 0 callers across 16 consumer repos**
    before it was chosen.
  - **A refusal here is justified by ROUTING, never by the server's message being poor.** PostgreSQL's
    *"refresh_continuous_aggregate() cannot run inside a transaction block"* is excellent and needs no
    improvement. What the server cannot know is `UseTransaction`, or that this framework has a policy
    emitter — so the guard's whole value is naming **both** doors (§ SH-H037 / TASK-215). Do not write a
    doc comment claiming the underlying error is unclear; that is an overclaim, and the guard does not need
    it.
  - **Stamp the measured version on any guard that encodes a provider restriction.** If TimescaleDB ever
    relaxes this, the guard becomes a **false refusal** — which this codebase rates worse than the hole
    (`PredicateScope`: *a false refusal breaks working code*). Recording *measured on 2.29.2 / PG 16.15*
    makes that findable instead of mysterious, the catalogue-drift rule from TASK-261 applied to a
    behaviour rather than to a column.
  - **Cover the EXECUTION MODEL, not only the SQL — and check which table your fixture resets.** Every
    prior test of these emitters ran the statement on a fresh connection in autocommit, so the suite tested
    the SQL thoroughly and the execution model not at all; the defect lived entirely in the gap. Compare
    TASK-246, where a feature worked in the branch nobody used. **And the runner-path fixture has its own
    trap**: resetting the wrong version-table name (`__BirkoMigrations` for the real `__Migrations`) let a
    recorded version survive, so `Migrate()` found nothing to do and **returned success having created
    nothing** — a false green in one ordering and a false red in another. Run such a class in isolation
    **twice** before believing it; once is indistinguishable from first-run luck.
- **Per-caller, per-operation state never goes on a process-wide cached object — and when the last user of
  such a mechanism moves off it, the mechanism goes too.** Connectors are cached process-wide per
  (type, settings id) by `DataBase.GetConnector`, and three separate features have written one caller's state
  onto that shared object: the stores' unit-of-work transaction (fixed by TASK-240's `AmbientSqlTransaction`),
  the index-failure list that grew one entry per HTTP request forever (fixed by keying it), and
  `SqlSchemaBuilder`'s connection **and** transaction (TASK-259). The third is the one that shows why the rule
  needs stating rather than assuming: the builder called `SetExternalTransaction` at three sites and **never
  called it again with nulls**, so the migration runner's `using` disposed both objects and left them on the
  shared connector, where `DoCommand` preferred them over opening its own. Measured on SQLite with the default
  `UseTransaction = true`: the next store's **lazy schema-ensure** ran on the dead connection and threw — and
  per the rule above, a store whose schema-ensure throws is left permanently uninitialised, so every later read
  and write on that entity threw too. Six parts generalise:
  - **The replacement is flow-scoped and self-restoring, which is what makes the leak impossible rather than
    merely fixed.** `AmbientSqlTransaction` lives in an `AsyncLocal` cell, is keyed by settings id, nests as a
    stack and restores exactly what was there on dispose. Both stores moved to it in TASK-240 and left comments
    explaining why; the schema builder was simply not migrated with them. **When a mechanism is abandoned for a
    stated reason, grep for its remaining callers in the same change** — a rule enforced in two of three places
    is a rule that will be got wrong in the third.
  - **Deleting the superseded mechanism is part of the fix, not a follow-up.** TASK-247's rule — *a fallback
    nobody can reach is not a safety net, it is a second implementation that drifts* — applies to boundary
    mechanisms as much as to raw-SQL fallbacks. With `SqlSchemaBuilder` migrated, `SetExternalTransaction` had
    **zero** production callers (measured across all 16 consumer repos), so it and its four read branches in
    `DoCommand` / `DoCommandWithTransaction` / `RunBulk` / `RunReaderCommand` were removed. Leaving a public,
    reachable, process-wide setter in place would have preserved the exact trap just closed. **Do not
    reintroduce it: putting per-operation state on a cached connector is the defect, not the spelling.**
  - **Behaviour-preserving in both configurations, and the null case is the one to check.** The legacy branch
    required `ExternalConnection` **and** `ExternalTransaction` to be non-null, so a migration run with
    `UseTransaction = false` never routed connector commands onto the migration's connection — it used the
    connector's own. `AmbientSqlTransaction.Enter` refuses a null transaction, so **declining to enter
    reproduces that exactly**; the shared producer returns `null` and `using var` accepts it. Both shipped
    consumers run with transactions disabled for unrelated reasons (Symbio's DDL goes through the connector's
    own connection, so an outer runner transaction deadlocks single-writer SQLite), which is the only reason
    this was never seen in production — **the default was the dangerous value.**
  - **One producer, or the revert proves nothing.** The first draft wrote the same three-line helper in
    `SqlSchemaBuilder`, `SqlCollectionBuilder` and `SqlIndexBuilder`. Reverting one left the regression test
    **green**, because the migration path runs through the nested collection builder — the copy under test was
    not the copy that mattered. Fourth instance of § TASK-243's *"a funnel with four overrides is not a
    funnel"*, and the first where the duplication defeated the **proof** rather than the fix. Collapsed to one
    `internal static` producer; the revert then failed 2 of 49.
  - **A guard's own test can be measuring the wrong thing in two ways at once.** The first version of the
    regression test failed *after* the fix, for a fixture reason — the migration declared `Guid`+`Name` while
    the probe entity derived from `AbstractLogModel` and therefore also had `CreatedAt`/`UpdatedAt`, which
    `CREATE TABLE IF NOT EXISTS` will not add. A missing-column error from a mismatched fixture looks exactly
    like the defect under test. **Match the probe entity to the migration, and read the failure rather than
    the pass/fail bit.**
  - **A count worth re-measuring: TASK-247's "0 uses of `ISchemaBuilder` across 16 consumer repos" was
    stale.** Re-running it found `Symbio.Tests.Unit/MigrationRuntimeTests` genuinely using
    `context.Schema.CreateCollection(…).Build()`. The conclusion it supported still holds (no *production*
    consumer code uses it), but the claim as written was too strong — and this task existed partly because that
    file itself said to re-run the sweep rather than cite it.
- **A column type is only correct for the operations the provider allows ON it — and where a key restricts the
  type, "is this column an index key" is resolved ONCE, never OR'd at a connector.** Same one-producer family
  as the identifier rules above, arriving at the layer where a *column* is declared rather than where a name is
  emitted. `MSSqlConnector.ConvertType` mapped every `DbType.String` field that is not a `CharField` to the
  deprecated `TEXT`, and a `TEXT` column on SQL Server **cannot take a parameter comparison at all**. Measured
  on 2022 (16.0.4265.3): `=` / `<>` / `IN` raise **402**, `LOWER(col)` **8116**, `ORDER BY` and `GROUP BY`
  **306**, `DISTINCT` **421**; only `LIKE` and `IS NULL` were legal. So a plain
  `public string Name { get; set; }` — the common consumer shape, present on essentially every consumer
  entity — broke **every** `Find`/`Count`/`DeleteWhere` predicate and every `SortBy` over that column, on
  the one provider nobody ran string predicates against (TASK-257, filed by consumer Symbio TASK-472). The rule
  is now: **an unlengthed `string` on MSSql declares `NVARCHAR(MAX)`; where an index key names the column it
  declares `NVARCHAR(255)`; nothing alters an existing column.** Eight parts generalise:
  - **`NVARCHAR(MAX)` over any bounded default, because the broken direction was READS.** `TEXT` accepts 2 GB
    writes today, so a bounded default would start refusing values the same code stored yesterday — trading a
    fixed read defect for a new write defect. It also keeps MSSql in step with SQLite `TEXT`, PostgreSQL `TEXT`
    and MySQL `LONGTEXT`, all unbounded, so no provider gains a silent write ceiling. Third consecutive task
    where **uniformity across providers beat per-provider fidelity** (TASK-256, TASK-263), for the same reason:
    the product tests on SQLite and deploys elsewhere. The precedent is two arms up in the same `switch` —
    `DbType.Binary` → `VARBINARY(MAX)` (CR-M137), chosen because a bare `BINARY` defaults to `BINARY(1)`.
  - **Fixing the predicate class does NOT fix the index, and measuring is what says so.** An index over
    `NVARCHAR(MAX)` raises the **same 1919** as one over `TEXT` — `MAX` is not permitted in a key at all — so
    the obvious one-line fix would have left every `[IndexedField]`/`[CompositeIndex]`/`[UniqueField]`
    unlengthed string exactly as broken, and *reported success*. Both halves were measured against the live
    server **before** the code was written, which is what makes the two reverts below meaningful rather than
    decorative.
  - **`AbstractField.IsInIndexKey`, not three flags OR'd at the connector.** `IsIndexed` (TASK-248) answers only
    *"does a declared index name this column"* — `LoadIndexes` marks nothing else — while `FieldDefinition`
    emits `UNIQUE` and `PRIMARY KEY` as **inline column constraints on all four providers**. So a
    `[UniqueField] public string Code` is an index key that `IsIndexed` reports as `false`, and on SQL Server
    that killed **the whole `CREATE TABLE`**, not merely an index. Two providers need this answer and MySQL
    currently gives a too-narrow one, so a computed property is one producer rather than speculative generality
    — a third spelling is how a rule gets got wrong again.
  - **The asymmetry with MySQL is deliberate, recorded, and TESTED.** MySQL has the identical hole
    (`LONGTEXT UNIQUE` → ERROR 1170) and deliberately still reads the narrow `IsIndexed`, because switching it
    changes DDL on a provider this task did not measure. **A test asserts MySQL still reads the narrow flag** —
    otherwise the asymmetry is indistinguishable from an oversight and the next reader unifies it from
    symmetry, exactly as § TASK-263 had to pin its own deliberate gaps.
  - **255 because MySQL already picked 255, not because SQL Server's limit is 255.** The real ceiling here is
    1700 bytes nonclustered / 900 clustered = 850 / 450 characters, so there was room for more. A value that
    indexes on one server must index on the other, since the same model runs on both; per-provider headroom
    would buy nothing real (document numbers, codes, e-mail addresses) and cost a divergence. Overridable via
    `protected virtual int IndexedStringColumnLength`, and **the override has its own test** — "the real ceiling
    is the key limit, not this number" is otherwise a comment nothing enforces.
  - **Bounding a UNIQUE column is only safe because the over-long write is REFUSED, and that rests on
    `ANSI_WARNINGS`.** Measured on 2022: with it **ON** — Microsoft.Data.SqlClient's default, and the framework
    never changes it — a 300-character insert into `NVARCHAR(255)` raises **Msg 2628** and stores nothing. With
    it **OFF** the value is silently **truncated to 255**, and a second, genuinely different value sharing that
    prefix is then rejected as **Msg 2627, a duplicate key** — i.e. the constraint becomes *weaker than
    declared*, which is precisely the outcome TASK-248 rejected prefix indexes for, arriving through a session
    setting instead. Not a defect here (nothing in the framework sets it, and the identical property has always
    held for `[MaxLengthField(n)]` on MSSql, MySQL and PostgreSQL), but it is the load-bearing reason the
    bounded branch is acceptable at all, so it is written down rather than left as luck. A consumer that turns
    `ANSI_WARNINGS` off trades a loud refusal for a silent collision on every bounded unique column it owns.
  - **Nothing repairs an existing database, and that is stated rather than left to be discovered.**
    `CreateTable` is guarded by `IF NOT EXISTS` and schema-ensure never reconciles the columns of an existing
    table, so a pre-fix database keeps its `TEXT` columns and keeps failing every predicate. The remedy is a
    hand-run `ALTER TABLE [T] ALTER COLUMN [C] NVARCHAR(MAX) NULL` (supported, preserves data, no index to drop
    since `TEXT` could never have one). Auto-`ALTER` on schema-ensure was rejected: store init rewriting
    existing production columns is the quiet destructive write these rules exist to forbid. Blast radius
    measured as **zero** — no deployment selects MSSql — and, as in TASK-219/256, that window closes the moment
    one does.
  - **A "green" test list can be the wrong list, and only the measurement tells you which.** The task's own
    acceptance criteria named `Contains`/`StartsWith`/`EndsWith` among the things to prove — but `LIKE` is
    **legal** on `text`, so those three passed *before* the fix and are contract pins, not provers. Confirmed by
    revert (a): 25 of 85 fail and the LIKE test is **not** among them. Had they been treated as provers, a
    revert taking them down would have been read as success. **Establish which half of a criterion carries the
    proof before writing the assertions.**
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
- **TASK-204's degrade-and-report rule has a SECOND sink, and the test for "may I degrade this?" is
  whether anything DECLARED it — not whether it sounds important.** TASK-254. After TASK-472 fixed the
  identifier defect, a hypertable conversion that cannot succeed stopped being silently swallowed and
  started throwing out of `TimescaleDBConnector.CreateTable` → `InitCore` — and stores set `_initialized`
  only *after* schema-ensure returns, so one unconvertible entity took its **whole surface, reads
  included**, down permanently. Exactly the failure TASK-204 removed for indexes, arriving at a second
  place. Six parts generalise:
  - **The licence to degrade came from provenance, not from importance.** The tempting argument —
    "partitioning is only an optimisation" — is weak and arguable. The decisive fact is that **nothing
    ever declares an entity to be a hypertable**: `CreateTable` converts *every* table it creates whenever
    `TimescaleDBSettings.TimeColumn` is set, and there is **no per-entity attribute** anywhere. So a
    failure is a connector-wide default that did not apply, not a broken per-entity contract. **Before
    degrading anything, find out who asked for it** — if the answer is "a global setting", degrading is
    safe; if an entity declared it, think again.
  - **⚠ The premise degrading rests on is that the WRECKAGE IS USABLE, and it must be measured.** Here:
    does `base.CreateTable` commit the table before the conversion fails? Measured on TimescaleDB 2.29.2 /
    PostgreSQL 16.15 — **yes**: the plain table survives a `TS103` and is fully writable and readable. Had
    it not, degrading would leave the store *initialised over a table that does not exist*, which is
    **worse than the throw it replaces** and would have invalidated the whole task. A degrade whose
    remains are unusable is not a degrade, it is a silent corruption; measure before assuming the ordering.
  - **Extract the MECHANISM when the type is public surface a consumer names.** The bookkeeping —
    keyed-not-list, transition-fired, clear-on-repair, locked, stably ordered — is subtle and TASK-204 got
    it wrong first time (an append-only list growing one entry per HTTP request, because connectors are
    cached process-wide while `_initialized` lives on the store). So it wants one implementation. But
    *generalising `IndexCreationFailure` itself* was rejected on measurement: Symbio names it in production
    code, two test files, its `CLAUDE.md` and its specs, including the "not an inventory" property.
    `SchemaEnsureFailureLog<T>` is the resolution — one implementation underneath, **byte-identical public
    surface above**. Third time measurement has vetoed the obvious reshape (TASK-248, TASK-256).
  - **Say when a shared helper is NOT built for reuse.** It has exactly two callers and no more are
    coming — compression and retention are migration-path only (see below) — so it is justified by *the
    logic was got wrong once*, not by future callers. Written on the class: **if it acquires configuration
    or a type hierarchy, that is the signal two copies would have been right.**
  - **A sentinel value in somebody else's collection is not a cheap reuse, it is a contract change.**
    Recording the hypertable failure as an `IndexCreationFailure` with `indexName = "(hypertable)"` was the
    cheapest option and is wrong: it injects foreign entries into a collection a consumer reads in
    production and documents. Reuse the *mechanism*, never the *published record*.
  - **A criterion that says "decide whether X is owed the same treatment" can be answered NO, and that is
    a result rather than a spawn.** Compression and retention live only in
    `Birko.Data.Migrations.TimescaleDB`, are never called from schema-ensure, and are explicit calls that
    *should* throw. Nothing is owed to them, so filing a task would file work that does not exist —
    § *findings become tasks* is about work that exists, not about symmetry.
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
- **A hypothesis you cannot reproduce gets FALSIFIED or recorded — never quietly adopted, and never
  "confirmed" by a run of green.** TASK-276, worked and deliberately **not closed**. Two rare cross-class
  failures had been seen in SQLite-backed suites (~1 in 16 runs each), one of them named
  (`ObjectDisposedException: 'SQLitePCL.sqlite3'`). Three things generalise from the attempt:
  - **Kill the leading hypothesis with a measurement before building on it.** The obvious cause was the
    **24** process-wide `SqliteConnection.ClearAllPools()` calls sitting in per-class teardowns — the only
    obvious way to get a disposed *handle* rather than a SQLite error code. Measured: an open connection
    survives a foreign clear, a pooled connection reopens fine after one, and 400 interleavings against
    in-flight commands produced nothing. Wrong, and now recorded as wrong so the next attempt does not spend
    its session there.
  - **A rate change is not a mechanism, and both halves of that must be said.** 0 failures in 95 runs against
    3 in ~35 before is inconsistent with the old rate at p≈0.3%, so the trigger is very likely gone — and
    nothing captured *why*, so the task stays open rather than being closed on a statistic. The same
    scepticism the task demanded of "it passed now" applies to "it passes a lot now".
  - **When the interleaving cannot be forced, pin what IS deterministic — and prefer the shared thing every
    hypothesis rested on.** Nothing asserted what `DataBase.GetConnector` shares, while several suites
    reasoned about its keying in prose. It is now pinned, including the edge that matters: the id is
    `Location:Name` and nothing else, so two settings objects differing in `CommandTimeout` share one
    connector and the **first** caller's value wins for everyone. That is TASK-270's subject, which now
    starts from a measurement rather than a reading.
- **A builder whose every method returns `this` has a silent option at every step — so it must honour a
  declaration or refuse it, never accept one and do nothing.** TASK-274, closing the index family. Measured
  2026-08-23: `IIndexBuilder.Sparse()` was `=> this` in **all six** schema builders, `WithProperty()` in all
  six, and `SqlIndexManager.ToSqlIndexDefinition` dropped `Sparse`, `ExpireAfter` and `Properties` — the same
  lost-flag defect TASK-245 fixed for `Unique` *in that very method*. Worst of all, the ElasticSearch,
  RavenDB and CosmosDB builders had **no `Build()` override at all**: they accumulated fields and a
  `Unique()` flag, held a live client, and inherited the interface's no-op default, so a migration read as
  though it had declared an index and the database never got one. Six parts generalise:
  - **A default no-op on an interface method is a silent-drop generator.** `IIndexBuilder.Build() { }` was
    added so eagerly-creating providers need not implement it — and three providers that create *nothing*
    inherited it and looked implemented. When a default implementation means "nothing to do here", check
    every implementer for whom it means "not done".
  - **Two doors onto one feature must give one answer.** `MongoDBIndexManager` honoured
    `IndexDefinition.Sparse`; the migration builder discarded it, so the same declaration meant different
    things depending on the door. Third instance after TASK-214 (Mongo's id) and TASK-244 (the transaction
    doors) — and the tell is always that both doors look correct in isolation.
  - **Where the semantics genuinely differ between backends, refuse rather than pick.** Mongo's sparse
    *compound* index includes a document when **any** key is present; a SQL partial index over
    `a IS NOT NULL AND b IS NOT NULL` requires **all** of them. `IIndexBuilder` does not say which
    `Sparse()` means, so single-column is honoured (the two readings coincide) and compound is refused, with
    the message naming `[CompositeIndex(..., WhereNotNull = ...)]` as the way to say it explicitly.
  - **Reuse the machinery, and the guard comes with it.** SQL expresses `Sparse` through TASK-273's
    `WhereNotNull` predicate, which means this lane can now carry predicates — and
    `SqlIndexManager.CreateAsync` calls `CreateIndexSql` **directly**, bypassing the funnel that checks them.
    `RequireExpressiblePredicates` therefore moved to `AbstractConnectorBase` and became public: one
    producer, two callers, no second copy to drift. **When a lane gains a capability, re-check the guards
    that capability implies** — TASK-273's own out-of-scope note had recorded this bypass a day earlier.
  - **Refusing is affordable exactly when nothing calls it — so measure that first.** 0 uses of `.Sparse()`
    and 0 of `.WithProperty(` across the framework, its tests and all 16 consumer repos. Contrast TASK-248
    and TASK-256, where the same instinct was vetoed because the blast radius was real.
  - **A live suite needs the right SERVER SHAPE, not merely a server.** Starting a standalone `mongod` to
    cover the Mongo half made five untouched `MongoTransactionBoundaryLiveTests` fail — MongoDB transactions
    require a replica set, which that suite's own first test says. The fix was `--replSet` plus
    `rs.initiate()`, after which all 97 passed. A container that satisfies "is it up?" can still make a
    green suite report a defect that is not there.
- **A deliberately-unfixed gap is closed by the measurement it was waiting for — and the test that recorded
  it is inverted, not deleted.** TASK-265, filed by TASK-257 which had closed the identical hole on MSSql and
  explicitly refused to "unify" MySQL from symmetry. Measured on 8.4.11 before a line changed:
  `LONGTEXT UNIQUE` and `LONGTEXT PRIMARY KEY` are **both `ERROR 1170`** at `CREATE TABLE`, so an
  `[UniqueField]`/`[PrimaryField]` unlengthed string meant the table could not be created **at all**;
  `VARCHAR(255)` accepts both. `MySQLConnector.ConvertType` now reads `AbstractField.IsInIndexKey` instead of
  the narrow `IsIndexed` — the one-word change the filing predicted, made only once the server had confirmed
  it. Four parts generalise:
  - **A filed gap should carry the measurement that would close it, and a test that fails when it does.**
    TASK-257 left `IndexKeyPredicateScopeTests.A_unique_or_primary_unlengthed_string_is_NOT_yet_bounded_here`
    asserting `LONGTEXT`, with "do not fix this test by switching the connector from symmetry — measure a live
    8.4 first, then change both together" in its own failure message. That is what happened, and the test was
    **inverted** in the same commit. A gap recorded as prose evaporates; a gap recorded as a passing test with
    instructions is a task that closes itself when someone finally measures.
  - **Re-scope a task before working it when a sibling has moved underneath it.** TASK-275 had already moved
    every *nullable* `[UniqueField]` column onto a synthesised index, which set `IsIndexed` and bounded it
    through the old branch. What remained was only the shapes that keep an **inline** constraint — a
    `[RequiredField]` unique column and a `[PrimaryField]` one — so the task's own description of its scope
    was stale by a day. Measuring the four shapes separately is what showed which two were still broken.
  - **Bounding is only acceptable because the over-long write is REFUSED, and that rests on a session
    setting.** Measured: 300 characters into `VARCHAR(255)` is `ERROR 1406` with **0 rows stored**, because
    `sql_mode` carries `STRICT_TRANS_TABLES` (the 8.x default, which this framework never changes). Without
    it MySQL truncates with a warning — and a truncated value makes a UNIQUE constraint quietly *weaker* than
    declared, the exact outcome TASK-248 rejected prefix indexes to avoid. Same shape as TASK-257's
    `ANSI_WARNINGS` note on SQL Server: the loud refusal is load-bearing, so it is pinned by its own test.
  - **Assert the constraint from the catalogue, not from the absence of an exception.** `CreateTable`
    swallows and records, so "it did not throw" would have passed against a table with no constraint at all;
    these tests read `information_schema.statistics` for a non-unique-zero index and then prove enforcement
    with a duplicate insert.
- **A constraint whose SHAPE cannot express the rule must change shape — and the change is scoped to the
  declarations that are actually broken.** Sixth member of the index family (TASK-275), and the one that
  completes it: `[UniqueField]` produces no index at all, so TASK-273's predicate could not reach it.
  `FieldDefinition` emits `UNIQUE` as an **inline column constraint**, which on SQL Server admits **one**
  NULL row and rejects every later row that leaves the column unset (measured: `Msg 2627`) — and a predicate
  is syntactically impossible there (`Msg 156`). So a nullable `[UniqueField]` column now drops the inline
  constraint and gains a synthesised `CREATE UNIQUE INDEX … WHERE col IS NOT NULL` instead. Six parts
  generalise:
  - **Scope the shape change by the property that makes it necessary, not by the attribute.** Only a
    **nullable** unique column moves; `[RequiredField]` and `[PrimaryField]` keep the inline form, whose DDL
    is byte-identical. `AbstractField.UsesInlineUniqueConstraint` states it once and all four
    `FieldDefinition`s consult it — the alternative, four providers each deciding, is how this family has
    repeatedly drifted.
  - **The new shape must be behaviour-preserving where nothing was wrong, and that is measurable.** A partial
    unique index admits many NULLs on MSSql, PostgreSQL and SQLite; MySQL's unfiltered one does too. So the
    emitted DDL changes on all four providers while the observable rule changes on **only the broken one** —
    which is what a per-provider suite has to assert, because "the DDL changed here" is otherwise
    indistinguishable from "the behaviour changed here".
  - **Reuse the machinery rather than the mechanism.** The synthesised index is routed through TASK-273's
    `WhereNotNull` accumulator, so it inherits the rendering, the refusal, the per-provider policy (MySQL
    drops the term because NULLs are already distinct there) and the tests — instead of a second code path
    that renders `IS NOT NULL` its own way.
  - **A synthesised name is database-global on two providers, so it carries the table — and a collision
    throws.** `ux_{table}_{column}`; if a declared index already owns that name this refuses rather than
    merging into it, because silently adding a column to somebody else's index changes *their* constraint.
  - **Moving a constraint into an index changes which per-provider column rules apply, and that is the part
    to re-measure.** The column becomes `IsIndexed`, so MySQL bounds an unlengthed string to
    `VARCHAR(255)` — which is what makes the synthesised index buildable there at all (`LONGTEXT` cannot be
    indexed, ERROR 1170). `IsInIndexKey` is unchanged (still true via `IsUnique`), so TASK-257's MSSql
    bounding still applies — asserted, because an unbounded column would make the new index impossible
    (`Msg 1919`) and the constraint would vanish into a recorded failure.
  - **Two of TASK-257's own pins changed meaning, and were updated rather than deleted.** Its claim — *a
    `UNIQUE` column is an index key that `IsIndexed` cannot see* — is now true only of the shapes that keep
    the inline form. Both tests moved to `[RequiredField]` columns (preserving exactly what they were
    about) and a new test asserts the other side: a nullable unique column **is** visible to `IsIndexed`,
    because it now has a real index. The pair is the record of where the line moved.
- **A constraint whose scope is "some rows" has to be DECLARED, and where a provider cannot express it each
  polarity is answered separately, on measurement.** Fifth member of the index-DDL family, and the first that
  arrives from a *missing* capability rather than a broken emitter. A unique index over a nullable column was
  undeclarable, and the obvious full index is not merely weaker — it is wrong on one provider: SQL Server
  treats NULLs as **equal** for uniqueness, so `UNIQUE (TenantGuid, ExternalId)` admits one NULL row per
  tenant and **rejects the second ordinary row** (measured on 2022/16.0.4265.3 as `Msg 2601`), while
  PostgreSQL 16.15, MySQL 8.4.11 and SQLite 3.53.3 treat NULLs as distinct and admit any number. Invisible on
  the three providers consumers test on, fatal on the one they do not — and TASK-257 is what made it reachable
  at all, since before it an unlengthed indexed string was `TEXT`, which SQL Server refuses as an index key
  (`Msg 1919`). `[CompositeIndex]` and `[IndexedField]` now take `WhereNotNull` / `WhereNull`: lists of
  **property names**, rendered as ` WHERE <col> IS [NOT] NULL` (TASK-273). Nine parts generalise:
  - **Two column lists, not a predicate string, because the alternative is an uncontainable sink.** A general
    `WHERE IsActive = 1` reaches `CREATE INDEX` by interpolation, cannot be parameterised, and
    `DataBase.ValidateIndexFieldIdentifier` validates *one bare identifier* — so it needs an expression
    validator this framework does not have, i.e. it is SH-H023 with a fresh coat. Names here resolve against
    the same `fields` map as `CompositeIndex.Properties`, so `[NamedField]` / `ModelMap` remaps are honoured
    and **no caller text reaches the DDL**. The wider feature is deferred rather than forgotten, and the two
    lists do not block it.
  - **Both polarities were needed, and the second is not symmetry — it is the framework's own soft-delete
    contract.** `ISoftDeletable` is `DateTime? DeletedAt` with *null means active*, so "unique among rows that
    are not deleted" is `WHERE DeletedAt IS NULL`: **opposite polarity, over a column that is not part of the
    key.** Measured legal on all three providers with partial indexes — which is also why `Predicates` sits on
    `IndexDefinition` rather than on `IndexColumn`, a shape that structurally cannot express it. **Read the
    framework's own base models and decorators before scoping a constraint feature**; the second caller was
    already in the tree.
  - **Where a provider cannot express the predicate, the question is not "which polarity" but "does the
    unfiltered index still enforce what was declared" — and the answer is a THREE-way classification.**
    MySQL supports no partial index (`ERROR 1064`), so `CanDropIndexPredicate` decides: a **non-unique**
    index is droppable (it constrains nothing, so a wider index is identical); a **unique** index is
    droppable only for an `IS NOT NULL` term over **one of its own key columns** (NULLs are distinct there,
    so such a row is already exempt); **everything else is refused**. Over-enforcing is the failure mode this
    family had not yet seen — it inverts the usual instinct that the quiet option is the safe one — and this
    rule shipped WRONG in its first form: "drop any `IS NOT NULL` term" is the natural generalisation of the
    NULL-distinctness argument and it is invalid the moment the predicate column is not part of the key.
    `UNIQUE (TenantGuid, Number) WHERE ApprovedAt IS NOT NULL` dropped to `UNIQUE (TenantGuid, Number)`
    rejects two *unapproved* drafts sharing a number, which the declaration permits. **A justification that
    holds for one shape of declaration is not a rule — check it against every shape the feature accepts**;
    this one was caught by the close-gate review, after two of its own tests had already encoded it.
  - **The capability is stated once and its `false` side is asserted.** `SupportsPartialIndexes` joins
    `SupportsTransactionalDdl` / `FoldsUnquotedIdentifiers` / `IsMissingTableException`: one producer,
    consulted by the funnel and by the emitter, never an inline `is MySQLConnector`. A test pins `false` on
    MySQL and `true` on the other three — unasserted, the flag is indistinguishable from an unconditional
    emit. Forcing it `true` fails **5 of 6** MySQL tests, including the droppable polarity, which is the
    server rejecting the `WHERE` it was wrongly told it supports.
  - **Refuse BEFORE the command wrapper, or the exception type is destroyed.** The guard runs in
    `CreateIndexes` / `CreateIndexesAsync` *ahead of* `DoDdlCommand`, because `InitException` re-wraps a
    callback exception as a bare `Exception` no `catch (InvalidOperationException)` can select — the same
    reasoning SH-H002 records for `AddRequiredWhere`. Schema-ensure's per-index catch still records it
    (TASK-204 degrade-and-report, untouched) and an explicit call still throws; the emitter keeps its own
    throw as the backstop for a direct caller (§ TASK-137). **Both funnels need the guard and only the sync
    one has a store-level caller** (TASK-245: an async store's schema-ensure calls the *sync* `CreateTable`),
    so the async one needs a test of its own or the rule is enforced in one of two places.
  - **Merge, then validate — the contradiction is invisible before the merge.** `[IndexedField]` aggregates
    by index name across properties, so predicates are unioned and de-duplicated exactly as `IsUnique` is,
    and *two properties can each name the same column in the opposite list*. Validation therefore runs after
    the merge, and the order is deterministic (`WhereNotNull` terms, then `WhereNull`, each in declaration
    order) because the emitted statement is compared byte-for-byte.
  - **The nullability test is `IsNotNull || IsPrimary`, and the second arm is not decoration.**
    `AbstractField.IsNotNull` is derived from the CLR type for value types, but for **`string`** and
    **`byte[]`** it is set *only* by `[RequiredField]` / `[Required]` — so a `string` primary key reads
    `false`, and a `WhereNull` on it would be accepted and index zero rows. State the other half where a
    consumer meets it, too: C# nullable-reference annotations are **not** read, so `string` and `string?` are
    the same thing here and a vacuous `WhereNotNull` over an always-populated string is accepted rather than
    refused. **A metadata flag's provenance decides what it can be trusted for.**
  - **A predicate column is not an index key, so do not bound it.** TASK-257 bounds an indexed unlengthed
    string to `NVARCHAR(255)` via `IsInIndexKey`, and the obvious inference is that a predicate column needs
    the same. Measured: a filtered predicate over `NVARCHAR(MAX)` is **legal**, and only a *key* column
    raises `Msg 1919`. Marking predicate columns would change MSSql column DDL for a case the server accepts
    as it stands.
  - **⚠ Limit, documented and pinned rather than fixed: a CHANGED predicate is not applied to an existing
    index.** Schema-ensure matches by index **name** and never alters one, so editing these lists on an entity
    whose index already exists is silently ignored on every provider (measured on SQL Server: the guard skips
    and `sys.indexes.filter_definition` keeps its original value). Drop the index by hand. Same position as
    TASK-257's columns and TASK-245's same-name-different-columns case; reporting such drift is TASK-269's
    family.
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

### A continuous aggregate could not be created through the migration runner at all (2026-08-24)

TASK-281, spawned by `code-review` at TASK-255's close gate hours earlier and rated the most valuable thing
that review produced. `SqlMigrationSettings.UseTransaction` defaults to `true`, and both continuous-aggregate
statements raise **SQLSTATE `25001`** inside a transaction block — so through the only path a real migration
takes, `CreateContinuousAggregate` and `RefreshContinuousAggregate` could only ever fail. Verified against
live **TimescaleDB 2.29.2 / PostgreSQL 16.15** with `BIRKO_REQUIRE_LIVE` set: **71 passed, 0 failed,
0 skipped** (61 → 71, +10). The standing rule is in § Conventions. Seven things worth carrying:

- **The task was planned AFTER Step 0, not before it** — its own criterion 1 said the hypothesis might be
  false, and the finding came from a reviewer that had already been wrong once in the same pass. Planning
  first would have invited the plan to assume the defect. It was confirmed, and the same probes priced both
  remedies.
- **The two statements needed opposite answers.** `CREATE … WITH (timescaledb.continuous)` refuses only
  because it performs an initial refresh, so `WITH NO DATA` fixes it in place; `refresh_continuous_aggregate`
  has no such escape and is refused with a message instead. Treating the family alike would have shipped
  either a needless limitation or a statement that still fails.
- **The grill found the mechanism the plan had missed, and it changed the severity.** The plan concluded a
  transactional migration could *never* populate an aggregate. False: `add_continuous_aggregate_policy` **is**
  transaction-safe, and a refresh *policy* is how TimescaleDB intends an aggregate to be kept current — the
  framework simply had no emitter. It was added in the same change, because documenting a remedy the
  framework cannot perform is TASK-263's *"named an escape hatch that did not open"*.
- **`WITH NO DATA` is emitted unconditionally**, not only inside a transaction — § TASK-274's *two doors, one
  answer*. The cost (an empty view until populated) was measured at **0 callers across 16 consumer repos**
  before it was chosen, and is asserted by a test so nobody "fixes" the emptiness and silently reintroduces
  `25001`.
- **The refusal is justified by routing, not by the server's message being poor.** PostgreSQL's own error is
  excellent; what it cannot know is `UseTransaction` or this framework's policy emitter, so the guard names
  **both** doors — and the opt-out has its own end-to-end test, per § SH-H037, because a guard whose escape
  hatch does not open is a wall wearing a door's label.
- **The prover was watched red before a line changed**, with the failure landing on the aggregate statement
  rather than an earlier one — which is what probe F (`create_hypertable` is transaction-safe) was measured
  for. Mutations: drop `WITH NO DATA` → 5 red; disable the refusal → 1; drop `RegclassLiteral` from the new
  emitter → 3, including the **live** policy test, since a bare PascalCase view folds and the policy cannot
  find its aggregate.
- **⚠ A fixture fault, and a wrong prediction, both recorded.** `Reset()` dropped `__BirkoMigrations` when the
  real version table is `__Migrations`, so a recorded version survived, `Migrate()` found nothing to do and
  **returned success having created nothing** — false green in one ordering, false red in another (§ TASK-259).
  Fixed, then proven by running the class green in isolation *twice*. And the plan's claim that CR-H071's two
  tests would stay green as untouched controls was **false**: they assert the semicolon adjacent to
  `GROUP BY bucket`, and `WITH NO DATA` now sits between. The assertions were re-expressed to preserve the
  dangling-comma intent, not weakened.

### No continuous aggregate over a framework-created table could ever be built (2026-08-24)

TASK-255, filed by TASK-253's audit of the same file. `BuildContinuousAggregateSql` hardcoded its bucketing
column as the literal `time` — CR-H070's defect, fixed in `BuildCompressionPolicySql` four methods above and
left here by **the very commit that fixed it**: `531d816` edited the defect line while naming the finding.
No framework-created table can have such a column, since column definitions are emitted bare and every Birko
entity is PascalCase. Verified against live **TimescaleDB 2.29.2 / PostgreSQL 16.15** with
`BIRKO_REQUIRE_LIVE` set: `Birko.Data.Migrations.TimescaleDB.Tests` **61 passed** (56 → 61) and
`Birko.Data.SQL.Tests` **653 passed** (+20), 0 failed, 0 skipped, no new nullable warning. The standing rule
is in § Conventions. Six things worth carrying:

- **Latent, and said so up front.** 0 of 16 consumer repos call it; 1 (`Birko.Sandbox`) merely imports the
  `.projitems`. The fix removes a trap rather than repairing damage — TASK-246 had to be corrected after the
  fact for claiming live impact it did not have, so the number came first.
- **The containment mechanism is REFUSAL, and that is new.** A column reference in real identifier position
  must be emitted **bare**, so no quote character encloses it and escaping contains nothing. The only
  containment left is a whitelist — `DataBase.ValidateColumnIdentifier`, sharing `_unqualifiedIdentifier`
  with its index sibling and differing **only in the message**, because the index guard's wording would tell
  a migration author about `CREATE INDEX` (§ SH-H037/TASK-215). Pointing one at the other reds exactly one
  test; the other 19 stay green, which is the claim.
- **Bare is witnessed, not argued.** Rendering with `QuoteIdentifier` fails both live tests: the fixture
  creates `(Ts timestamptz …)` bare inside a quoted table, so PostgreSQL stores `ts`. TASK-129 got this
  exact question backwards by reasoning from one sink, which is why it was measured.
- **Two conventions live in one file, split by what kind of argument it is.** The sibling's
  `orderByColumn = "time"` looked like the precedent to copy — until `git show 531d816` showed the parameter
  did not exist before that commit, so the default was a **source-compatibility artefact**, not a judgement.
  The older convention for a *time-dimension column* is the opposite: `CreateHypertable` requires it, in four
  signatures. **Measure a precedent's motivation, not its shape.** The sibling's default is the same defect
  and is spawned as [[TASK-279]] rather than fixed from symmetry.
- **A default the compiler can forbid beats one a test forbids.** Placing the required parameter before the
  existing optional one makes a default `CS1737` — not expressible. So the reflection pin is **defensive,
  not witnessed**, and is labelled that way. The cost is the other half, measured on this change: 6 of 8
  call sites failed loudly (`CS7036`) and **2 rebound silently**, because every parameter is `string`.
  Affordable at 0 non-test callers; not a manoeuvre to copy where consumers exist.
- **Five mutations, disjoint.** Restore the literal → 3 red (both live + the rendering pin) with the CR-H071
  pair and the literal-`time` test **green**, which is the discrimination control the task demanded. Drop the
  guard → 3. Quote instead of bare → 3. Give the parameter a default → **does not compile**. Delegate the new
  guard to the index one → 1, the message test alone.

### Three migration index builders created nothing, and six dropped their flags (2026-08-23)

TASK-274, the last of the index family. Filed as "the second lane drops `Sparse`", and the measurement found
much more: `Sparse()` and `WithProperty()` were `=> this` in **all six** schema builders, and the
ElasticSearch, RavenDB and CosmosDB builders had no `Build()` override at all — inheriting
`IIndexBuilder.Build()`'s no-op default while accumulating fields, a `Unique()` flag and a live client. A
migration declared an index there and nothing was ever created. Verified against live MongoDB 7 (as a
single-node replica set), SQL Server 2022, PostgreSQL 16.15, MySQL 8.4.11 and on-disk SQLite with
`BIRKO_REQUIRE_LIVE` set: **1,704 tests, 0 failed, 0 skipped** across eighteen suites, 24 new. The standing
rule is in § Conventions. Six things worth carrying:

- **The filed defect was the smallest part of it.** "Sparse is a no-op" was true; "three of six builders
  create nothing at all" was the finding, and it only appeared because the task's first step was to read
  every implementation rather than the one the title named.
- **A default no-op on an interface method is a silent-drop generator.** `Build() { }` exists so
  eagerly-creating providers need not implement it — and three providers that create nothing inherited it
  and looked implemented.
- **Two doors, one answer.** Mongo's index manager honoured `Sparse` while its migration builder discarded
  it. Third instance after TASK-214 and TASK-244, and the tell is always that both doors look right alone.
- **Where backends genuinely disagree, refuse rather than pick.** Mongo's sparse compound index includes a
  document when ANY key is present; a SQL partial index requires ALL of them. Single-column is honoured
  (the readings coincide), compound is refused, and the message names the explicit alternative.
- **Gaining a capability means re-checking the guards it implies.** SQL expresses `Sparse` through
  TASK-273's predicates, so this lane now carries predicates — and `SqlIndexManager.CreateAsync` bypasses
  the funnel that validates them. `RequireExpressiblePredicates` moved to the base and became public: one
  producer, two callers. TASK-273 had recorded that bypass as an out-of-scope note the day before, which is
  why it was not re-discovered the hard way.
- **⚠ A live suite needs the right server SHAPE.** Starting a standalone `mongod` made five untouched
  transaction tests fail, because MongoDB transactions need a replica set — as that suite's own first test
  states. `--replSet` + `rs.initiate()` fixed the fixture and all 97 passed. "Is it up?" is not "is it the
  server these tests describe?".

### MySQL could not create a table with an unlengthed unique or primary string (2026-08-23)

TASK-265, filed by TASK-257 in August and closed once the live measurement it was waiting for existed.
`MySQLConnector.ConvertType` read the narrow `IsIndexed`, which `LoadIndexes` sets only for
`[IndexedField]`/`[CompositeIndex]` — so an inline `UNIQUE` or `PRIMARY KEY` over an unlengthed string emitted
`LONGTEXT`, and MySQL cannot use a BLOB/TEXT column in a key without a key length. Verified on live MySQL
8.4.11 plus the other three providers with `BIRKO_REQUIRE_LIVE` set: **1,433 tests, 0 failed, 0 skipped**
across eighteen suites, 3 new. The standing rule is in § Conventions. Five things worth carrying:

- **The task was re-scoped before it was worked, and it had shrunk by a day.** TASK-275 (closed hours
  earlier) moved every *nullable* `[UniqueField]` column onto a synthesised index, which bounded it through
  the existing branch. Measuring the four shapes separately showed the two that were still broken: a
  `[RequiredField]` unique column and a `[PrimaryField]` string. Both `ERROR 1170`.
- **The one-word fix was predicted in the filing and still not made until measured.** TASK-257 wrote down
  that `field.IsIndexed → field.IsInIndexKey` was "very likely" the answer and refused to do it, because
  this epic's recurring defect is a change believed correct on a provider nobody ran. The prediction was
  right; making it wait cost nothing and would have caught it had it been wrong.
- **The test that recorded the gap was inverted, not deleted.** It asserted `LONGTEXT` and said in its own
  failure message not to "fix" it by flipping the connector. A gap recorded as prose evaporates; a gap
  recorded as a passing test with instructions closes itself when somebody finally measures.
- **The over-long write is refused, not truncated — measured, and pinned.** `ERROR 1406`, 0 rows, because
  `sql_mode` carries `STRICT_TRANS_TABLES`. Without it MySQL truncates with a warning, which would make the
  UNIQUE constraint quietly weaker than declared. Same load-bearing session setting as TASK-257's
  `ANSI_WARNINGS` on SQL Server.
- **Mutation: reverting to the narrow flag fails 5 of 97** — the two inverted theory cases and all three new
  live tests — while the other three providers stay green, since they never read this branch.

### A unique column that was allowed to be empty rejected the second empty row (2026-08-23)

TASK-275, the last member of the index/NULL family that TASK-273 opened. `[UniqueField]` produces no index
definition at all — `FieldDefinition` emits `UNIQUE` inline — so TASK-273's predicate could not reach it, and
on SQL Server an inline `UNIQUE` over a nullable column admits one NULL row and rejects every later row that
leaves the column unset (`Msg 2627`). Nullable unique columns now carry a synthesised
`CREATE UNIQUE INDEX … WHERE col IS NOT NULL` instead. Verified against live SQL Server 2022, PostgreSQL
16.15, MySQL 8.4.11 and on-disk SQLite with `BIRKO_REQUIRE_LIVE` set: **1,430 tests, 0 failed, 0 skipped**
across eighteen suites, 11 new. The standing rule is in § Conventions. Six things worth carrying:

- **The blast radius decided the shape, and it was measured first.** Exactly **2** production
  `[UniqueField]` declarations sit on a nullable column across the framework, its tests and all consumer
  repos (BardStudio `MusicTrack.FilePath`, Presenter `PresentationEntity.Slug`) — and note they are
  *non-nullable in C#*, which the framework does not read, so they are nullable in the database. Every
  `[PrimaryField]` hit is the framework's own `Guid?` key, where `PRIMARY KEY` implies NOT NULL and the
  question cannot arise. Small enough to change the shape; the alternative was documenting a limit that
  consumers plainly do not read.
- **Scoped to nullable columns only**, so `[RequiredField]` and `[PrimaryField]` keep byte-identical DDL. One
  producer (`AbstractField.UsesInlineUniqueConstraint`), consulted by all four `FieldDefinition`s.
- **DDL changes on four providers; behaviour changes on one.** That is the claim a per-provider suite has to
  make, and each one does: SQLite and PostgreSQL and MySQL assert the rule is *unchanged*, MSSql asserts it
  is *fixed*, with the error code moving from 2627 (constraint) to 2601 (index) as the task predicted.
- **It reuses TASK-273 rather than re-implementing it** — the synthesised index goes through the same
  `WhereNotNull` accumulator, so MySQL's drop-the-term policy applies for free. Mutation: removing the
  predicate leaves **MySQL green** while MSSql and SQLite go red, which is the provider-correct signature.
- **⚠ It incidentally fixes part of TASK-265, and that is recorded rather than claimed.** An unlengthed
  `[UniqueField]` string is `LONGTEXT` on MySQL and `LONGTEXT UNIQUE` is ERROR 1170, so such a table could
  not be created at all; moving the constraint to an index makes the column an index key, which bounds it to
  `VARCHAR(255)`. TASK-265 still owns the `[PrimaryField]` and non-nullable cases, which keep the inline
  form.
- **Two of TASK-257's pins changed meaning and were updated, not deleted.** Its point — a `UNIQUE` column is
  an index key invisible to `IsIndexed` — now holds only for the inline shapes; both tests moved to
  `[RequiredField]` columns and a new one asserts that a nullable unique column *is* visible, because it now
  has a real index.

### Every limited read on SQL Server emitted invalid T-SQL (2026-08-23)

TASK-278, spawned an hour earlier by TASK-277 and closed the same day. `LimitOffsetDefinition` emitted
`FETCH NEXT n ROWS ONLY` with the `OFFSET` present only when the caller supplied one, and T-SQL accepts
neither that nor `OFFSET`/`FETCH` without an `ORDER BY`. So `ReadFirstAsync` — the single-row read this
framework's own guide recommends — could not work on that provider at all, and no unsorted page could be
fetched. Verified against live SQL Server 2022, PostgreSQL 16.15, MySQL 8.4.11 and on-disk SQLite with
`BIRKO_REQUIRE_LIVE` set: **1,389 tests, 0 failed, 0 skipped** across twelve suites, 16 new. The standing
rule is in § Conventions. Five things worth carrying:

- **It was found by strengthening a vacuous assertion, not by looking for it.** TASK-244's tests said
  `read.Should().NotBeNull()` after a write, which passes on an empty enumerable because a bulk store's
  `Read(filter)` returns the collection. Asserting the row instead failed instantly on MSSql. A test that
  cannot fail is worse than no test, because it occupies the space where a real one would go.
- **There was no paging coverage anywhere in the tree.** Not thin — none, on any provider. That is the whole
  explanation for how a documented API stayed unusable on a whole provider. This task adds it on SQLite as
  well as MSSql, so the shared base path is pinned by the provider everything else inherits.
- **The precondition is supplied where it is known.** `RequiresOrderByForPaging` lets
  `CreateSelectCommand` — which knows whether the caller passed a sort — synthesise
  `ORDER BY (SELECT NULL)`. Threading that fact into the emitter's `public virtual` signature was rejected
  because adding a parameter silently orphans existing overrides.
- **`TOP (n)` was the tempting alternative and would have been two code paths**: it lives in the SELECT list,
  not the tail, and an offset still forces the OFFSET/FETCH form and therefore the sort.
- **Mutations, disjoint by provider:** omitting the offset again → 3 red on MSSql, others green; removing the
  synthesised sort → the same 3; making the capability unconditionally true → MSSql **green** while SQLite
  and the base pins go red, which is the only way that over-broad fix shows up.

### A write to a missing table reported success on every provider (2026-08-23)

TASK-277, spawned by TASK-244 an hour earlier and closed the same day. It is the half that turned that
task's residue into consumer Symbio's report: the residue lost one operation, this made the operation answer
**200**. All four `OnException` handlers answered a missing table with `DoInit()` and a *return*, so the
statement was discarded and reported as successful. Verified against live PostgreSQL 16.15, SQL Server 2022,
MySQL 8.4.11 and on-disk SQLite with `BIRKO_REQUIRE_LIVE` set: **1,384 tests, 0 failed, 0 skipped** across
twelve suites, 5 new. The standing rule is in § Conventions. Five things worth carrying:

- **My own task file understated the scope, and reading the siblings corrected it.** It said SQLite's
  handler was the one TASK-211 had not reached. True of the *narrowing* — but the **swallow itself was on all
  four providers**, so this was framework-wide silent data loss, not a SQLite quirk.
- **The recovery branch could never have worked.** `DoInit()` raises `OnInit`, which nothing in the framework
  subscribes to, and the failed statement is never retried. A branch that neither repairs nor retries is
  just a swallow with a reassuring name.
- **The read path never reaches that handler**, which is what made the fix surgical: `RunReaderCommandOn`
  catches `IsMissingTableException` itself. So TASK-211's "a read of a missing table answers empty" is
  untouched, and both sides of the asymmetry now have a test.
- **Blast radius measured before shipping: exactly one test**, the defect-pin from TASK-244, which was
  inverted rather than replaced. A removal that breaks nothing is a swallow nothing relied on — the opposite
  of TASK-211, whose narrowing broke two suites.
- **⚠ A vacuous assertion was hiding a second defect, and strengthening it found it.**
  `read.Should().NotBeNull()` after a write passes on an empty enumerable, because a bulk store's
  `Read(filter)` returns the collection (§ Conventions). Asserting the row instead failed immediately on
  MSSql: `Invalid usage of the option NEXT in the FETCH statement`. Measured — a limit with no offset is
  Msg 153 and offset+limit without `ORDER BY` is Msg 102, while `TOP (n)` and an explicit sort both work —
  so `ReadFirstAsync` cannot work on SQL Server at all. Filed as [[TASK-278]] (P1) with the measurement
  table and the fix shape.

### A rolled-back schema-ensure left a store believing it was initialised (2026-08-23)

TASK-244, raised P3 → P1 the day before by consumer Symbio (its TASK-527) after a live instance left a
database unbuildable. The task had been open since TASK-243 as a design question — *should schema-ensure
participate in the caller's transaction?* — and it is now answered, in the direction the measurements forced:
**it participates, and a participating schema-ensure is not remembered.** Verified against live PostgreSQL
16.15, SQL Server 2022, MySQL 8.4.11 and on-disk SQLite with `BIRKO_REQUIRE_LIVE` set throughout: **1,292
tests, 0 failed, 0 skipped** across nine suites — SQL 624, SQLite 236 (+3), PostgreSQL 88 (+3), MySQL 90
(+3), MSSql 96 (+3), Migrations.SQL 49, InMemory 69, JSON 23, XML 18 — 12 new. The standing rule is in
§ Conventions. Seven things worth carrying:

- **The consumer's symptom was not reproducible, so the mechanism was reproduced instead.** Four
  from-scratch bring-ups had succeeded, so nothing was gained by chasing the trigger. Tracing the ordering
  in source gave a chain that rebuilds the symptom deterministically in three assertions, and the first
  test written was a *reproduction*, not a fix.
- **It takes two framework behaviours, and each alone is survivable.** The residue — `_initialized = true`
  after a schema-ensure the boundary then rolled back — loses one operation. The swallow —
  `SqLiteConnector.OnException` answering "no such table" with `DoInit()` and no rethrow, where `DoInit`
  raises an event nothing subscribes to — is what makes that operation report **200**. Fixed the first,
  filed the second as [[TASK-277]] with a test that pins the defect so it cannot be believed fixed.
- **The obvious reading of the evidence was the wrong half, and measuring said so.** "The DDL ran on a
  connection other than the boundary's" is true — of the `SetTransactionContext` door, which was never
  publishing the boundary before schema-ensure ran. But on SQLite that path throws `SQLite Error 5` after
  the command timeout: a 500, not a silent 200. Real defect, fixed in the same change, *not* the consumer's
  mechanism.
- **Both doors now agree, which is what the acceptance asked for.** `InitCore`/`InitCoreAsync` enter the
  transaction scope, so the per-store door lands the DDL exactly where the ambient door does.
- **MySQL keeps the opposite answer and now has a test saying so.** `DdlSurvivesRollback` is true there
  because `DoDdlCommand` suppresses the ambient (TASK-243), so the store legitimately remembers its
  initialization — and the mutation that makes the capability ignore the provider switch fails **only** the
  MySQL suite. Without that pin the flag would have been indistinguishable from "never remember".
- **Three mutations, disjoint and provider-correct.** Remembering unconditionally: 1 red each on SQLite,
  PostgreSQL and SQL Server, MySQL green. Dropping the scope from `InitCore`: the same three, with SQLite
  taking 3s — the lock timeout, i.e. the deadlock hazard the task warned about, arriving as a test failure
  instead of an outage. Capability ignoring the switch: MySQL alone.
- **⚠ A flake was found and is not being reported as clean.** `Birko.Data.Migrations.SQL.Tests` failed
  `SchemaBuilderBoundaryLeakTests.Without_a_runner_transaction_nothing_is_published_either` once in 16 runs
  with `ObjectDisposedException: 'SQLitePCL.sqlite3'`. Measured against the pre-fix code (0 in 10) and after
  (0 in a further 10), so not attributable to this change; the test takes a **process-wide cached connector**
  via `DataBase.GetConnector`, which is [[TASK-270]]'s subject and [[TASK-276]]'s hypothesis. Recorded on
  TASK-276 with the test name and the exception, which is more than that task had.

### A unique index over a nullable column was undeclarable, and broke ordinary inserts on MSSql (2026-08-22)

TASK-273, raised by consumer Symbio while ruling its v1 frozen schema. `CompositeIndex` carried `Name`,
`Properties` and `IsUnique` and nothing else, so "unique **where** the column is not null" could not be said —
and the obvious full index is not merely weaker there, it is wrong: SQL Server treats NULLs as equal, so
`UNIQUE (TenantGuid, ExternalId)` admits one NULL row per tenant and rejects the second ordinary account.
`[CompositeIndex]` and `[IndexedField]` now take `WhereNotNull` / `WhereNull`. Verified against live
**SQL Server 2022 (16.0.4265.3)**, **PostgreSQL 16.15**, **MySQL 8.4.11**, **TimescaleDB 2/PG16** and on-disk
**SQLite 3.53.3** with `BIRKO_REQUIRE_LIVE` set throughout: **1,164 tests, 0 skipped** across six suites —
`Birko.Data.SQL` 619 (+22), MSSql 93 (+6), MySQL 85 (+7), PostgreSQL 85 (+3), SQLite 233 (+4),
Migrations.SQL 49 (unchanged) — 42 new, no new nullable warning. **Not a clean sweep: one unidentified
single-test failure in the offline suite, seen twice in ~19 runs and never reproduced** — see the last bullet.
The standing rule is in § Conventions. Nine things worth carrying:

- **Step 0 measured before a line was written, and it corrected the plan twice.** The code is **`Msg 2601`**,
  not the `2627` its TASK-257 neighbours cite; and the `Msg 1934` risk — SQL Server requires a specific
  SET-option state for *any* DML against a table carrying a filtered index, which this framework never sets —
  is **absent** on `Microsoft.Data.SqlClient` defaults (`ARITHABORT` reads 0 and it works anyway, because
  `ANSI_WARNINGS ON` implies it). That was the one finding that could have made the approach unusable here.
- **The task named one polarity; the framework's own code demanded two.** `ISoftDeletable` is
  `DateTime? DeletedAt` with null meaning active, and Symbio's `BaseEntity` carries it on every entity — so
  "unique among live rows" (`WHERE DeletedAt IS NULL`, over a **non-key** column) was the next caller before
  this task existed. Reading the consumer's own decorators is what turned a bool into two column lists.
- **Dropping an unexpressible term is safe only sometimes, and the first version of this rule got it wrong.**
  It shipped as "drop `IS NOT NULL`, refuse `IS NULL`" — the natural generalisation of NULL-distinctness, and
  invalid as soon as the predicate column is not one of the index's key columns:
  `UNIQUE (TenantGuid, Number) WHERE ApprovedAt IS NOT NULL` dropped to `UNIQUE (TenantGuid, Number)` rejects
  two unapproved drafts sharing a number, which the declaration permits. The close-gate review caught it —
  after two of my own tests had encoded it — and the rule is now a three-way classification: non-unique →
  drop, unique with the term over a key column → drop, otherwise refuse. It also found the mirror: a
  non-unique partial index was being refused on MySQL although dropping its term is harmless, so a declared
  optimisation was simply absent there.
- **MySQL could have honoured it and deliberately does not.** A functional key part —
  `UNIQUE (TenantGuid, (CASE WHEN DeletedAt IS NULL THEN Number END))` — was measured enforcing exactly the
  right rule on 8.4.11. Declined for four stated reasons and recorded on the property, so the next author
  meets a decision rather than a gap.
- **The plan review caught a wrong claim no later test would have caught.** It said "reject a predicate column
  whose field `IsNotNull`" — but that flag is set from the CLR type only for value types, so a `string`
  primary key reads `false` and a `WhereNull` on it would have indexed zero rows silently. Now
  `IsNotNull || IsPrimary`, and the mutation narrowing it back fails **exactly one** test.
- **Six mutations, each isolating one claim, all disjoint** — MSSql tail 3 of 6; base tail 2 of 3 (PG) and
  2 of 4 (SQLite); MySQL capability forced true 5 of 6; the two `LoadIndexes` resolution points 3 and 7 of 22;
  the async funnel guard exactly 1, failing on the exception **type**, which is why the refusal happens before
  `DoDdlCommand`.
- **Three fixture faults, each indistinguishable from the feature failing** — MySQL `1364` (an INSERT omitting
  `AbstractLogModel`'s NOT NULL timestamps), a PostgreSQL catalogue query lower-casing a table name
  `CreateTable` quotes, and a full-run 46-of-84 MySQL failure that was purely a missing `BIRKO_MYSQL_PASSWORD`
  in my environment. Read, not dismissed; that suite's password default now matches its siblings.
- **The close gate found two things the implementation had not asked about, and produced two spawns.** The
  async funnel guard had no coverage at all (TASK-245: an async store's schema-ensure runs the *sync*
  `CreateTable`, so nothing reaches `CreateIndexesAsync`) — now tested. And `SqlIndexManager.CreateAsync`
  calls `CreateIndexSql` **directly**, bypassing the guard: harmless today because that lane carries no
  predicates, which is exactly why it is recorded on [[TASK-274]]. Spawned [[TASK-274]] (the second index lane
  drops `Sparse` — `IIndexBuilder.Sparse()` is `=> this` in all six schema builders) and [[TASK-275]]
  (`[UniqueField]` on a nullable column is an inline constraint with the identical defect and no predicate can
  attach to it — `Msg 2627` / `Msg 156`, measured). Also confirmed live: this repo's project-local
  `verify-conventions` still does not shadow the generic one at the gate ([[TASK-267]]).
- **⚠ And it did not end clean, which is recorded rather than rounded off.** `Birko.Data.SQL.Tests` failed
  **1 of 619 twice in ~19 full-suite runs** and the identity was never captured: 8 further runs with a trx
  logger were clean, the new class alone is clean 6 of 6, and the suite *without* it is clean 5 of 5 — so the
  evidence points at a cross-class interaction through `DataBase`'s static table cache under xUnit's parallel
  collections (this task adds four deliberately-invalid entity types, whose `LoadTable` throws), and no
  further. Precedent for writing it down is TASK-259's own unreproducible SQLite failure; the difference is
  that this one is plausibly caused by the change, so it is [[TASK-276]] rather than a footnote.

### A chunk-interval reader had been broken for the whole TimescaleDB 2.x line (2026-08-21)

TASK-261, found while writing TASK-253's live suite — the first draft of a chunk-interval assertion failed for
the same reason the product code did. `GetChunkInterval` read `chunk_time_interval` from
`timescaledb_information.hypertables`; 2.0 moved that value to `timescaledb_information.dimensions` and renamed
it `time_interval`, so the method raised `42703` on every supported server, unswallowed. Latent — the only
references in the tree were its own declaration and that test, and no consumer calls it. Verified on live
**TimescaleDB 2.29.2 / PostgreSQL 16.15** plus the four SQL providers: **1,229 tests green, 0 failed** across
nine suites, 4 net new. The standing rule is in § Conventions. Five things worth carrying:

- **The task began with a test asserting the defect, so the work was to invert it, not to write it.** The
  `42703` assertion is deleted rather than kept beside the new one — two tests asserting opposite things about
  one method is a contradiction for the next reader, not extra coverage.
- **One of the three reverts fails nothing, and that is reported rather than hidden.** Restoring the old column
  fails 4 of 56; dropping the `COALESCE` fails exactly the integer-partitioned case; dropping
  `AND dimension_number = 1` fails **0**, because the view carries its own `ORDER BY` and the right row comes
  back first. So that clause is **defensive, not witnessed** — kept because correctness without it rests on an
  ordering the catalogue does not promise, and the hazard is pinned by asserting the catalogue's shape
  (2 dimension rows, 1 carrying an interval) instead of pretending the reader can see it. My own doc comment
  initially claimed the failure did occur; the revert is what caught that.
- **The column that names the shape does not discriminate it.** An integer-partitioned hypertable reports
  `dimension_type = 'Time'` on 2.29.2, with `time_interval` NULL and the width in `integer_interval` — so
  branching on the type would have been wrong, and the discriminator is which interval column is populated.
  The reader coalesces, because returning null would claim no interval is configured when one is.
- **Another product's catalogue is a dependency with an expiry, and nothing typed says so.** The old spelling
  was presumably correct on 1.x and nothing recorded that it could lapse. The remark now names the version the
  query targets, so the next break is readable rather than silent.
- **The out-of-scope survey was actually run, and it is clean.** The framework holds exactly two
  `timescaledb_information` queries — this one and `IsHypertable`, whose column still exists and has live
  coverage. The remaining `chunk_time_interval` occurrences are `create_hypertable`'s named argument, which is
  current. No internal `_timescaledb_catalog` use anywhere. Nothing spawned.

### A migration could not name a table in another schema, because one helper served two kinds of name (2026-08-21)

TASK-262, both halves filed by TASK-253's own close-gate review as regressions that task introduced. Routing
the TimescaleDB migration emitters through `QuoteIdentifier` — correct for a name from `Table.Name` — turned an
author's `reporting.evts` into `'"reporting.evts"'`, one identifier containing a period. Verified against live
**TimescaleDB 2.29.2 / PostgreSQL 16.15**, the exact version the task's original measurements used, plus SQL
Server 2022, PostgreSQL 16, MySQL 8.4 and on-disk SQLite: **1,225 tests green, 0 failed** across nine suites,
16 new. The standing rule is in § Conventions. Six things worth carrying:

- **Measure the fix's shape, not just the defect.** Reverting to a bare name would have restored the qualified
  case; per-part quoting is *strictly better*, and only measuring showed it — `'"reporting"."Evts4"'` and
  `'"Rep Ort"."Ev ts"'` each created a hypertable, and neither is reachable bare. The fix ends up more capable
  than the code TASK-253 replaced.
- **The trade is real, lopsided, and measured.** Splitting on unquoted dots gives up a table literally named
  `a.b` — kept reachable as `"a.b"`, since caller-quoting suppresses the split. Cost: **0 of 317**
  `[Table("…")]` declarations anywhere contain a dot.
- **A quoted-text scanner needs the provider's delimiters, and MSSql's are asymmetric.** `[`/`]` differ, so a
  same-character scanner mis-detects the quoted part. Exposed once as `IdentifierQuoteOpen`/`Close` beside
  `QuoteIdentifier`, covered offline for all three shapes despite only PostgreSQL having callers — and a test
  fake that overrode the quoting without the delimiters was corrected in the same change, because an
  unfaithful fake is how an override goes untested.
- **The second half was documented rather than fixed, and the reason is structural.** The column-name fold
  cannot become conditional on the caller's spelling, because the same producer serves the *store*, where an
  unquoted name means the quoted identifier the framework created (TASK-472). Making unquoted mean "fold me"
  would re-break that defect — the one whose failure is swallowed. With 0 emitter call sites, an opt-out on
  seven methods is speculative API; the limit and the shape of the eventual fix are written on the class.
- **Answering "should this support schemas?" surfaced that the framework has no schema concept at all** —
  nothing on `Attributes.Table`, `Tables.Table` or any `Settings`. Kept out of this task deliberately: it is a
  feature touching ~10 emitters, not a P2 regression fix. Filed as [[TASK-272]] with the design settled —
  identity on the table, rendering and capability on the connector, because a connector holds one schema at
  most while `Tables.Table` holds no connector.
- **Reverts:** whole-name quoting fails **4 of 52** — all four new live tests — while the 48 pre-existing tests
  stay green, which is what proves the fix additive and the new tests the thing that witnesses it. Also
  measured for the spawn: on MySQL 8.4 `CREATE SCHEMA reporting` created a *sibling database*, so
  `SupportsSchemas` will be genuinely two-sided rather than an unconditional flag.

### A migration left its disposed connection on the shared connector, and the next store died of it (2026-08-21)

TASK-259, found while grilling TASK-253's plan. `SqlSchemaBuilder` called
`_connector.SetExternalTransaction(_connection, _transaction)` at three sites and **never called it again with
nulls** — and it was the framework's last user of that mechanism, which both stores had abandoned in TASK-240
with comments explaining why. Connectors are cached process-wide per (type, settings id), so a migration's
connection and transaction stayed on the shared object; the runner's `using` then disposed both. Verified
against live **SQL Server 2022**, **PostgreSQL 16**, **MySQL 8.4**, **TimescaleDB 2/PG16** and on-disk SQLite
with `BIRKO_REQUIRE_LIVE` set throughout: **1,165 tests green, 0 failed** across eight suites, 2 new. The
standing rule is in § Conventions. Seven things worth carrying:

- **"Measure before fixing" inverted the task's own hypothesis — twice.** Its title said the defect *may be
  entirely latent*. Measured: **firing on the default configuration**, and worse than described. Not just "a
  subsequent command takes the wrong branch" — the first thing a store does is its lazy schema-ensure, so the
  `CREATE TABLE IF NOT EXISTS` ran on the dead connection and threw, which leaves the store permanently
  uninitialised. One migration kills every store sharing that (type, settings id) for the process lifetime.
- **Latent in production for a reason unrelated to the defect.** The legacy branch needed `ExternalConnection`
  **and** `ExternalTransaction` non-null, and both shipped consumers set `UseTransaction = false` deliberately
  (Symbio: its DDL goes through the connector's own connection, so an outer runner transaction deadlocks
  single-writer SQLite). So the guard never fired — but the **connection was leaked on every configuration**;
  only the consequence was gated on the transaction. The dangerous value was the default.
- **The last caller moving off a mechanism is when the mechanism goes.** With `SqlSchemaBuilder` on
  `AmbientSqlTransaction`, `SetExternalTransaction` had **0** production callers (measured across 16 consumer
  repos), so it, its two properties and its four read branches were deleted — TASK-247's rule applied to a
  boundary mechanism instead of a raw-SQL fallback. Leaving a public process-wide setter in place would have
  preserved the trap just closed.
- **My own duplication defeated my own proof.** The first draft wrote the helper three times; reverting one
  copy left the regression test **green**, because the migration runs through the nested collection builder.
  Fourth instance of *"a funnel with four overrides is not a funnel"* — and the first where the duplication
  broke the *revert* rather than the fix. One `internal static` producer; the revert then failed 2 of 49.
- **A fixture mismatch is indistinguishable from the bug.** The regression test failed *after* the fix because
  the migration declared `Guid`+`Name` while the probe derived from `AbstractLogModel` and so also had
  `CreatedAt`/`UpdatedAt` — which `CREATE TABLE IF NOT EXISTS` will not add. Read the failure, not the bit.
- **I fell into the skip-as-failure trap I had documented one task earlier.** Running with
  `BIRKO_REQUIRE_LIVE=1` reported 14 failures in `Birko.Data.Migrations.TimescaleDB.Tests` — no TimescaleDB was
  running. 48/48 without the flag. Started the container rather than narrowing the flag, which is what makes
  the 1,165 figure mean something. Also recorded: one **unreproducible** SQLite failure (228/229) seen once and
  green on four subsequent runs; noted rather than dismissed.
- **Two decisions elsewhere rested on this defect and are now reopened.** TASK-253 rejected routing the
  TimescaleDB migration emitters through the connector *because* doing so required `SetExternalTransaction`;
  that constraint is gone, so the choice is open again (recorded at `TimescaleDBMigration`, not acted on —
  it is a live-path behaviour change wanting its own measurement). And § Conventions' *"the legacy pair is
  deliberately **not** suppressed"* was a blessing of the status quo, where the status quo was this bug; it now
  reads one-mechanism-one-rule. Spawned **[[TASK-270]]**: three separate features have now put per-caller state
  on the process-wide cached connector, so the pattern — not the instances — needs an answer, and the cheapest
  valuable piece is a test that fails when someone adds the fourth.

### On MSSql no predicate on an unlengthed `string` worked, and the obvious fix would not have fixed the index (2026-08-21)

TASK-257, filed by consumer Symbio (TASK-472) after `DeleteWhereAsync(x => x.Label == "a")` failed on MSSql
16.00.4265 with *"The data types text and nvarchar are incompatible in the equal to operator"*. The cause was
column typing, not the boundary being verified: `ConvertType` mapped every string that is not a `CharField` to
the deprecated `TEXT`. Verified against live **SQL Server 2022 (16.0.4265.3)** — the reported build — plus live
**PostgreSQL 16**, live **MySQL 8.4** and on-disk SQLite — the three this change must not disturb — with
`BIRKO_REQUIRE_LIVE` set throughout so no gated suite silently skipped: **1,117 tests green, 0 failed** across
seven suites — MSSql 87 (was 61), `Birko.Data.SQL` 575 (was 565), SQLite 229, MySQL 78 (was 74),
PostgreSQL 82, MSSql.View 19, Migrations.SQL 47 — **40 new**. No new nullable warning in any touched project (the 64
standing ones in `Birko.Data.SQL.Tests` are in its own test files and are unchanged by this work — measured
by stashing the change, not assumed). The standing rule is in § Conventions. Eight things worth carrying:

- **The blast radius was the whole provider, not an exotic shape.** A plain `public string Name { get; set; }`
  with no length attribute is what consumers write, and it is on essentially every consumer entity —
  so every `Find`/`FindAll`/`Count`/`DeleteWhere` predicate and every `SortBy` over a string column threw on
  MSSql. Never noticed because consumers test on SQLite and the MSSql live suites added by TASK-242/243 assert
  on bulk writes and lazy init, never on a string predicate.
- **Step 0 measured what the plan intended to cite, and two of the four premises were wrong.** Against a `TEXT`
  column: `=`/`<>`/`IN` → **402** (the draft said 8116); `LOWER` → **8116**; `ORDER BY` *and* `GROUP BY` → **306**;
  `DISTINCT` → **421**, a fourth code the draft did not predict; `LIKE` and `IS NULL` → **legal**. Skipping that
  step would have shipped a test asserting the wrong error number and a comment stating it.
- **`NVARCHAR(MAX)` alone would have fixed the predicates and reported success on the indexes.** Measured: an
  index over `NVARCHAR(MAX)` raises the **same Msg 1919** as one over `TEXT`. Worse for `UNIQUE`/`PRIMARY KEY`,
  which `FieldDefinition` emits as **inline column constraints** — those took down the entire `CREATE TABLE`.
  And since TASK-204 schema-ensure *records* an unbuildable index rather than throwing, with nothing subscribing,
  the half-fix would have been silent.
- **`IsIndexed` could not see the constraint case, so the predicate was resolved once.**
  `AbstractField.IsInIndexKey => IsIndexed || IsUnique || IsPrimary` — `LoadIndexes` marks only
  `[IndexedField]`/`[CompositeIndex]`, so a `[UniqueField]` string is an index key the old flag called `false`.
  MSSql reads the new property; **MySQL deliberately still reads the narrow one** (it has the identical
  `LONGTEXT UNIQUE` → 1170 hole, but changing its DDL needs a live 8.4 run), and a test pins that asymmetry so
  nobody unifies it from symmetry.
- **255 matches MySQL on purpose, and SQL Server had room for more.** Its key limit is 1700 bytes nonclustered /
  900 clustered — 850 / 450 characters — so 450 was available and was rejected: the same model runs on both
  servers, and a value that indexes on one must index on the other. Overridable, and the override has its own
  test, since "the real ceiling is the key limit, not this number" is otherwise unenforced prose.
- **Reverts — four, each isolating one claim.** (a) restore `return "TEXT"` → **25 of 85**, and the LIKE test is
  **not** among them, which is what confirms those three assertions were pins rather than provers. (b) always
  `NVARCHAR(MAX)`, bounded branch unreachable → **11 of 85**, with **all 9 predicate tests still green** — the
  two halves proven independent rather than argued. (c) drop each `IsIndexed` marking site in turn → **4** and
  **2**, in disjoint sets; TASK-248's equivalent revert failed **0**, which is why this suite declares one entity
  per attribute form. (d) narrow `IsInIndexKey` back to `IsIndexed` → **4**, exactly the UNIQUE/PRIMARY cases.
- **Two fixtures were rigged and a third file's doc was stale.** `BulkTransactionBoundaryLiveTests` carried
  `map.Property(x => x.Name).HasPrecision(100)`, which *looks* like a length and is not one —
  `ModelMapRegistry` keeps fluent length as mapping metadata and never applies it to the SQL field, so that
  column had always been `TEXT`. Removed rather than corrected, since unlengthed is the honest shape. The old
  `IndexedStringColumnTypeTests` asserted `TEXT` for indexed *and* unindexed and called the consequence a known
  out-of-scope divergence; its premise is gone, so it was rewritten. Also corrected: `AbstractField.IsIndexed`
  and `MySQLConnector` both claimed "the other three providers index a TEXT column happily" — **MSSql cannot**,
  and it was the only one of the three that couldn't.

- **The close gate found something the implementation had not asked about, and it was a security property.**
  Bounding a UNIQUE column is only acceptable because the over-long write is *refused*, and that rests entirely
  on `ANSI_WARNINGS`. Measured: **ON** (SqlClient's default; the framework never sets it) → Msg 2628, nothing
  stored. **OFF** → the value is silently truncated to 255 and a second, genuinely different value sharing that
  prefix comes back as **Msg 2627, duplicate key** — the constraint quietly weaker than declared, which is the
  exact failure TASK-248 rejected prefix indexes to avoid, arriving through a session setting instead. No defect
  (and `[MaxLengthField(n)]` has always had the identical property on three providers), but it is the reason the
  bounded branch is safe, so it is recorded rather than relied on by luck. The gate also caught a convention
  miss of its own: `IsInIndexKey` is declared in `Birko.Data.SQL` and was tested only from the two provider
  suites, so § Testing's *"tests in `Birko.{Project}.Tests`"* wanted a suite in its own project — now
  `IsInIndexKeyTests` (10).

### `[UtcField]` opened the door TASK-256's rule had named (2026-08-19)

TASK-263, spawned by TASK-256's planning grill. That task settled what a plain `DateTime` column means on
PostgreSQL — a wall clock — and named an opt-in for the case it deliberately does not serve, a value whose
**instant** must be unambiguous. The opt-in did not exist: `ConvertType` had always mapped
`DbType.DateTimeOffset` to `TIMESTAMPTZ` (PostgreSQL) and `DATETIMEOFFSET` (MSSql), but `CreateAbstractField`
had no arm and no attribute can override a `DbType`, so the rule pointed at a mapped, walled-off door.
`[UtcField]` on a `DateTime` property is that door. Verified against live **PostgreSQL 16**, **SQL Server
2022**, **MySQL 8.4** and on-disk SQLite: **1,011 tests green across five suites** (565 · 82 · 61 · 74 · 229),
31 new. The standing rules are in § Conventions. Six things worth carrying:

- **The promise is the weakest provider's, not the best column type's.** MySQL's `DATETIME` and SQLite's
  numeric affinity cannot carry an offset, and a field cannot behave differently per provider — `Tables.Table`
  holds no connector and `AbstractField.Read` goes through the provider-blind `DataBase.Read`. So the contract
  is *the instant is exact and reads back as UTC*, and a caller's original offset is normalised away
  **everywhere, including on the two providers that could have kept it**. Uniformity beat per-provider fidelity
  for the second time in two tasks, for TASK-256's reason: the product tests on SQLite and deploys on
  PostgreSQL.
- **An attribute, not a `DateTimeOffset` CLR property** — that type would advertise an offset half the providers
  cannot honour, and an API that over-promises is worse than one stating its limit. It is also cheaper: an
  existing `DateTime` model opts in per property with no type change.
- **The two features compose because of a bound value's CLR TYPE, and that is now load-bearing.** TASK-256
  strips `Kind` from every bound `DateTime` on PostgreSQL, on a premise this task falsified; `AddParameter`
  cannot know the target column, so `UtcDateTimeField.Write` returns a **`DateTimeOffset`**, which the
  stripper's `is DateTime` test misses. Reverting to a bare `DateTime` stores the instant **an hour out,
  silently** — 1 of 82, and only the non-UTC database sees it.
- **Measure the read, not just the write.** `GetDateTime` is wrong or fatal on three of four providers: it
  **throws** `InvalidCastException` on MSSql's `datetimeoffset`, returns `Kind=Local` on SQLite and
  `Unspecified` on MySQL. Only `GetFieldValue<DateTimeOffset>(i).UtcDateTime` is exact everywhere — the obvious
  implementation would have passed on PostgreSQL and failed outright on MSSql.
- **The delegated survey is answered, and the answer is "change nothing".** A plain `Kind=Utc` `DateTime` is
  stored unshifted on MySQL, MSSql and SQLite, and shifted on PostgreSQL — which reproduces the pre-TASK-256
  defect and so validates the probe. TASK-256's normalisation stays PostgreSQL-only on evidence rather than
  being spread on symmetry.
- **Two gaps are now recorded as decisions rather than left looking like oversights.** `DbType.Date`,
  `DbType.Time` and a CLR `DateTimeOffset` property are unreachable from a model **by design**, written down at
  `CreateAbstractField` since that dispatch is the only producer of fields; and SQLite's declared-`INTEGER` /
  stored-ISO-text mismatch is pinned by a test rather than "fixed" into a divergence from plain
  `DbType.DateTime`, which declares `INTEGER` and stores text too.

### A UTC `DateTime` could not be bulk-inserted, and was silently shifted one row at a time (2026-08-19)

TASK-256, filed by consumer Symbio (TASK-472) after a real entity hit `BulkInsertAsync`. The filed defect was
loud and real — PostgreSQL's binary `COPY` passes `NpgsqlDbType.Timestamp` explicitly and Npgsql **refuses** a
`Kind=Utc` value, so `CreateManyAsync` threw for every UTC-kinded entity. Measuring it found **a second defect
on the path the task had cleared as working**: `AddParameter` binds no `DbType`, so Npgsql infers `timestamptz`
and the server casts it into the timezone-less column **through the session's `TimeZone`** — storing `11:30`
for a `10:30` UTC value on a UTC+1 server, with no error. Verified against live **PostgreSQL 16** and
**TimescaleDB 2 / PG16** (Npgsql 10.0.3): PostgreSQL **76** tests (was 67), TimescaleDB **44** (was 42), plus
MySQL 8.4, SQL Server 2022, SQLite and `Birko.Data.SQL` (555) all green; 0 nullable warnings. The standing rule
is in § Conventions. Seven things worth carrying:

- **The task named the loud half and cleared the quiet one.** Its scope note said *"the binary COPY path
  only… `CreateAsync` works"* — true only on a UTC-configured server. Fixing the named half alone would have
  left the two write paths storing **different instants**, so a bulk-written row would not match a filter bound
  through the parameterised path. Both are now normalised by one producer,
  `PostgreSQLConnector.NormalizeTimestampValue`.
- **The framework's own base model emitted the value its own connector refused.**
  `Birko.Data.SQL/Models/AbstractLogModel.cs:17-18` initialises `CreatedAt`/`UpdatedAt` from `DateTime.UtcNow`,
  so this was every `AbstractDatabaseLogModel` descendant on every consumer — not a Symbio quirk. That also
  killed the fail-fast option: refusing the value, per § SH-H037, would have refused a write from every
  framework entity. Second inversion of that rule after TASK-248.
- **`TIMESTAMPTZ` is the honest type and still the wrong answer.** Reopened during the grill precisely because
  it is cheapest now — no PostgreSQL data exists and models are about to freeze. Rejected on measurement: it
  makes PostgreSQL the **only** tz-aware provider (SQLite numeric, MySQL `DATETIME`, MSSql `DATETIME2`), and
  since the product **tests on SQLite and will deploy on PostgreSQL**, a green test would stop proving
  production behaviour. It also breaks the `Unspecified` case (`10:30` in → `09:30Z` back) and its `ALTER`
  silently reinterprets existing rows in the session TZ. **Uniformity across providers beat per-provider
  correctness.**
- **The chosen rule was already the consumer's rule.** Symbio's `UtcDateTimeJsonConverter` treats an
  `Unspecified` value from storage **as UTC** and converts for display via `Birko.Time`'s `ITimeZoneConverter`
  from a UTC baseline. So "the caller re-attaches the `Kind`" was written and shipped before the task existed —
  reading the consumer's conversion layer is what made the decision cheap.
- **On a correctly-configured server the silent half is unobservable, so the test misconfigures one.** Both
  paths store `10:30` on a UTC server whatever the code does — reverting the parameter fix fails **nothing**
  there. `PostgreSqlSettings.GetConnectionString()` emits no `Timezone` key, so `SET TimeZone` on a test's own
  connection cannot reach the store's; the test creates a **dedicated database** with
  `ALTER DATABASE … SET TimeZone` and calls **`NpgsqlConnection.ClearAllPools()`**, without which a pooled
  connection keeps `Etc/UTC` and the test silently measures nothing. Measured both ways.
- **Reverts:** COPY writer **16 of 76** · `AddParameter` **1 of 76** — and that one is exactly the non-UTC test
  · base COPY vs the TimescaleDB suite **15 of 44**, which is how the inherited fix was proven rather than
  read. The blast radius was measured before the parameterised path was touched: `DataProviders.Default` is
  `SQLite` and **no deployment holds PostgreSQL data**, so no stored row could be orphaned — a window that
  closes when production moves to PostgreSQL, which is why both halves landed now (same shape as TASK-219).
- **The close gate caught my own rule overstating itself.** The first draft said "every write boundary strips
  `Kind`". The bulk update/delete paths bypass `AddParameter` entirely — pre-create parameters with
  `DBNull.Value`, `Prepare()`, assign `.Value` per row — so six binding sites sit structurally outside the
  funnel; measured, they are unshifted anyway, because `Prepare()` pins each parameter to the column's real
  type before a value is assigned. The fix was neither to wire them nor to wave them off but to **pin the
  mechanism**, so dropping `Prepare()` fails a test rather than silently shifting. The word that made the rule
  true — *un-prepared* — was earned by measuring, not by reasoning (§ TASK-258).
- **Two fixtures were rigged, one by omission.** TimescaleDB's built rows `Unspecified` with a comment
  explaining the refusal — a defect worked around in a fixture. PostgreSQL's `BulkRow` carried **no `DateTime`
  property at all**, which is the same avoidance with nothing to notice. De-rigging both is what makes the COPY
  revert fail 16 rather than a handful. Spawned **TASK-263**: `ConvertType` maps `DbType.DateTimeOffset` to
  `TIMESTAMPTZ` (and `DATETIMEOFFSET` on MSSql) but **nothing can reach it** — no `DateTimeOffset` arm in
  `CreateAbstractField`, no attribute can override a `DbType` — so there is no way to persist an instant with
  its offset, and the new rule's named escape hatch does not exist yet.
