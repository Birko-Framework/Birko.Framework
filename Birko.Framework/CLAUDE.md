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

⚠ **This is a SEPARATE hierarchy — it does not derive from `AbstractBulkStore` / `AbstractAsyncBulkStore`
above.** `DataBaseStore<DB,T>` descends from `AbstractStore<T>` / `AbstractAsyncStore<T>`, but the *bulk*
tier implements `IBulkStore<T>` / `IAsyncBulkStore<T>` **directly** and carries its own copies of the
collection and filter-based overloads — which is why it also declares its own private `RequireFilter`. The
two diagrams sitting next to each other read as one chain and are not: anything wired "into the base bulk
store" reaches the portable backends (JSON, XML, InMemory, MongoDB, RavenDB, CosmosDB, InfluxDB) and
**not** the SQL ones. Measured cost of assuming otherwise: TASK-215's whole-table write guard was absent
from every SQL provider for a fortnight, so `Update(x => !empty.Contains(x.Name), action)` rewrote every
row of the table with no exception on the framework's default provider (TASK-329, § Conventions).

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

Birko.Health (IHealthCheck, HealthCheckResult, HealthCheckRunner — zero deps)
  -> Birko.Health.Data (SQL, Mongo, Raven, SMTP, MQTT, TCP … — still zero Birko deps, BCL + delegates only)
  -> Birko.Health.Data.SQL (SchemaDriftHealthCheck) + Birko.Data.SQL
     (a per-dependency sibling, like .Redis and .Azure, so the Health leaf stays dependency-free)

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
    - **And "the shared base" is a claim about a TYPE HIERARCHY, so it has to be checked against the
      concrete types rather than against the interface.** TASK-215 put the guard on `AbstractBulkStore` /
      `AbstractAsyncBulkStore` and read as complete, because every backend in the sentence derives from
      one of them. **The SQL bulk stores derive from neither** — `DataBaseBulkStore<DB,T>` and
      `AsyncDataBaseBulkStore<DB,T>` implement `IBulkStore<T>` / `IAsyncBulkStore<T>` **directly** and
      carry their own copies of the six filter-based overloads, which is also why they declare their own
      private `RequireFilter`. So a third hierarchy had no guard for a fortnight on the framework's
      **primary** provider: measured on SQLite,
      `Update(x => !empty.Contains(x.Name), r => r.Name = "OVR")` gave `thrown=NONE | overwritten=3 of 3`,
      sync and async (TASK-329). Sixth instance of § TASK-243's *a funnel with four overrides is not a
      funnel*, arriving as **a base nothing derives from is not a base**. Four parts generalise:
      - **Grep the DECLARATION, not the call.** `grep -rn RequireBoundedFilter Birko.Data.SQL/` returned
        nothing, and that one command was the whole diagnosis. When a guard is described as living "on the
        base", ask which file declares it and which files can see it.
      - **The scope of the wiring is measured per overload, and it was 2 of 6 rather than 6 of 6.**
        `Delete(filter)` and `Update(filter, PropertyUpdate<T>)` reach the connector, where SH-H002's
        `AddRequiredWhere` already refuses the identical predicate — so four of the six were correct **by
        a different mechanism** and blanket-wiring all six would have been indistinguishable from wiring
        the two that mattered. Only `Update(filter, Action<T>)` was defective, because that path emits a
        `SELECT` (where an always-true predicate is legitimate) followed by per-row
        `UPDATE … WHERE Guid = @g`, each individually bounded, so **no conditionless statement is ever
        emitted** for a statement-level guard to see. The task file's own guess ("very likely present on
        the PropertyUpdate and Delete overloads") was wrong in the safe direction; measuring is what
        turned a 6-line fix into a 2-line one and told the spec which half was which.
      - **Two implementations plus one uncovered hierarchy is a fix at the WRONG LAYER, and the cheap diff
        is the tell.** Adding copies three and four in the SQL layer was four lines and is precisely the
        shape this file keeps recording as the cause. The rule now lives once, in
        `Birko.Data.Core/Expressions/BoundedFilterGuard.cs`, and all three declaration sites forward to it
        passing **only the door name** — the one thing that genuinely differs per caller. Behaviour
        preservation is measured, not inspected: making the new producer a no-op reds 18 pre-existing
        tests across three suites (SqLite, InMemory, JSON), so the delegation is live rather than merely
        compiling.
      - **A decorator that DELEGATES inherits a guard; one that re-implements silently loses it.**
        `CachedAsyncDataBaseBulkStore.UpdateAsync(filter, Action<T>)` is the only override of a guarded
        overload anywhere in the framework, and it wraps `base.UpdateAsync`, so it was correct the moment
        the base was. That is luck until it is pinned — an edit inlining the loop to save a call would
        reopen a whole-table rewrite behind a decorator whose own tests all stayed green — so the
        delegation has its own test.
- **A nullable filter argument means "do not filter" or it means a value — decide once, and check what the
  backend's query language does with null, because one dialect will silently turn it into MATCH NOTHING.**
  TASK-309 / SH-H013, and the first member of the one-producer family to arrive at a *value* that is absent
  rather than at an identifier. `ISyncKnowledgeStore` takes `Guid? tenantId` and documents it as "optional
  tenant"; RavenDB applied the predicate under `if (tenantId.HasValue)` and CosmosDB applied it
  unconditionally as `x.TenantId == tenantId`. Five parts generalise:
  - **The contract is settled by the CALLER, not by the parameter's type.** `Guid?` is equally readable as
    "no filter" and as "the rows whose tenant is null", and only the caller disambiguates:
    `TenantSyncProvider.ResolveTenantScope` returns null in exactly two cases — an explicit
    `ITenantContext.IsAllTenantsScope`, and an entity type with no `TenantGuid` property — and **both mean
    the whole scope**. So "do not filter" is the contract, and it was Cosmos that was wrong. Read the
    producer of the null before deciding which of its two readings a backend should implement.
  - **A dialect's null-equality is a silent match-nothing, and it is invisible in the C#.** Measured offline
    on Microsoft.Azure.Cosmos 3.63.0: `x.TenantId == tenantId` with a null tenant renders
    `root["TenantId"] = null`, which in Cosmos SQL is **Undefined**, not true — so it matched no document,
    **including the documents whose own `TenantId` is null**. LINQ-to-Objects answers the opposite
    (`null == null` is true), so an in-memory test *understates* the defect rather than reproducing it. Same
    family as `{ "F": { "$nin": [] } }` under § TASK-137: a one-term predicate that looks ordinary and means
    something the author did not write.
  - **Two backends answering one contract differently is the defect, whichever is right.** The finding filed
    both as wrong "in opposite directions"; measurement refuted the Raven half, and fixing both would have
    **broken** the all-tenants path on the backend that had it right. A per-backend audit has to establish
    the contract first, or it converges the two onto the wrong answer.
  - **One producer per backend for the predicate.** `CosmosSyncKnowledgeQuery.ApplyScope` replaced four
    inline copies (`GetKnowledge` / `GetKnowledgeItem` × sync and async). Extracting it is also what made
    the defect testable — see the next bullet.
  - **⚠ "It needs a live server" is a claim to measure, and here it was false for the half that mattered.**
    `CosmosSyncTenantScopingTests` states in prose that *"the LINQ filter itself needs a live Cosmos DB"*,
    which is why that area had **no** filter coverage at all. Measured: the provider renders query SQL with
    no account and no network — `ToQueryDefinition().QueryText` off a container built from an unreachable
    connection string. Only *executing* a query needs a server, so the exact artefact the defect lived in
    was assertable all along. Before accepting that a sink is untestable offline, try rendering it.
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
- **A blast radius is a measurement with an expiry date, and a stale one can argue for the wrong decision
  in either direction.** TASK-283 existed, and TASK-254 deliberately left its channel unhardened, because
  `OnIndexCreationFailed` was recorded as *"consumed by Symbio in production code, its host, two test files
  and its specs"* — so changing whether a handler's exception propagates would be a behaviour change on
  consumed surface. Re-measured at TASK-283's own criterion 1: **zero** `+=` subscriptions across all 16
  consumer repos. The "consumers" were doc comments, one of them (`Program.cs`) explaining why it does
  **not** read the channel. Five parts generalise:
  - **Grep for the SUBSCRIPTION, not for the identifier.** Every one of the cited references matched a
    search for the name; none of them was a handler. A channel's consumers are its `+=` sites, and a
    documented contract is not a subscriber.
  - **The stale count conflated two contracts that need separating.** The *collection*
    `IndexCreationFailures` genuinely has a reader; the *event* has none. They are different surfaces with
    different obligations, and the fix hardens the second while leaving the first byte-identical — with a
    test asserting the collection is unaffected by whatever subscribers do.
  - **§ TASK-259's rule cuts both ways.** It was written because a stale count let a wrong claim reach a
    commit message; here a stale count kept a P2 defect open for nine days and made it look blocked on a
    consumer decision that did not exist. **Re-measure before deciding you are blocked, not just before
    claiming you are safe.**
  - **Consistency is a reason to widen a task's stated scope, and saying so is the price.** This task's
    "out of scope" excluded the hypertable channel because TASK-254 had fixed it — true, but *differently*:
    a single `try { Invoke } catch { }` that swallows without recording. Leaving it would have left two
    policies side by side, which the same task's criterion 6 forbids. Both moved onto TASK-289's
    `RaiseDiagnostic`, so all three channels share one implementation. **When an out-of-scope bullet and an
    acceptance criterion disagree, the criterion is the one that was thought about.**
  - **⚠ `pg_isready` answers DURING initdb.** A readiness loop built on it hands back a database that is
    about to restart: the TimescaleDB suite reported 15 of 17 failing, entirely from the fixture, and gave
    56/56 twice against a genuinely-up server. Wait on an actual query (`psql -c "SELECT 1"`). Same class
    as § TASK-259's skip-as-failure trap — a readiness check that answers the wrong question is
    indistinguishable from a broken change.
- **An exception's TYPE is a contract three mechanisms select on, so rewrap only where the rewrap earns
  something — and enumerate the filters before you replace an exception in flight.** TASK-291 + TASK-294,
  filed apart and closed as one change because they were one line. `EnsureSchemaAndReport` rewrapped
  **every** exception from every provider's `OnException` as `new Exception(DescribeSchemaEscape(ex, …), ex)`.
  For a missing table that is the point (TASK-286's annotation rides on the message deliberately). For
  everything else `DescribeSchemaEscape` returns the command text unchanged, so the rewrap contributed the
  SQL and the **loss of the type**. Now a non-missing-table failure is reported as itself, with the
  statement on `Exception.Data`. Six parts generalise:
  - **The third consumer of the type was found by grepping the filters, not by reading the task files.**
    Neither task mentioned retry. `AbstractConnectorBase.ExecuteWithRetry` filters on
    `IsTransientException(ex)` — the **direct** predicate, not a chain walk — so a rewrapped
    `SQLITE_BUSY` stopped being transient and **a `RetryPolicy` a consumer had configured silently never
    fired** for any failure raised inside the try. § TASK-289's rule ("grep the filters before adding
    anything that can replace an exception") read in reverse: it applies to *removing* a replacement too,
    and to finding out what the replacement was already breaking.
  - **Two tasks that name the same line are one change.** Filed separately for good reasons — a
    cancellation is the caller's own decision, a lock timeout is not — and fixing them apart would have
    meant reasoning about the same catch twice and shipping the second on top of the first's assumptions.
    **When two filed defects quote the same statement, price them together before splitting the work.**
  - **Keep what the rewrap was actually adding.** For these shapes it was the command text, so that moves
    to `Exception.Data[AbstractConnector.CommandTextDataKey]`. Dropping it would have traded one
    diagnostic for another and called it a fix.
  - **`ExceptionDispatchInfo.Capture(ex).Throw()`, never `throw ex`** — and this one is **witnessed**
    rather than defensive (§ TASK-261): the mutation reds exactly one test, and the stack head visibly
    degrades from `Microsoft.Data.Sqlite.SqliteException.ThrowExceptionForRC` to `EnsureSchemaAndReport`.
    Say which, because the two look identical in a diff.
  - **⚠ The first version of the end-to-end test measured the one path that was already fine.**
    `RunCommandTransaction` opens its connection and calls `BeginTransaction()` **outside** the `try`, and
    Microsoft.Data.Sqlite issues `BEGIN IMMEDIATE` for its default isolation level — so a lock contended
    *before* the statement never reaches this funnel, already surfaces with its type, and already retries
    (measured: **0** `OnExecute` for the INSERT, raw `SqliteException` code 5). The defect is only on
    failures raised *inside* the try. **Before provoking a condition, check which side of the `try` it
    lands on**; the contrast is now pinned so the next reader does not repeat the mistake.
  - **A remedy's reachability can drop without its wrongness changing, and both belong in the record.**
    TASK-296 put SQLite on WAL, where readers do not block writers, so the contention that produced
    TASK-294's original 6-7 `Error 5` per storm run is largely gone. That is a reason to re-measure a
    filed premise before working it — not a reason to close it quietly, and not a reason to widen a catch:
    answering `0` for a *busy* database would be a fabrication where `0` for a *missing* table is the
    truth.
- **A remedy is priced on the STEADY STATE, not on the reproduction that found the defect — and a knob may
  only offer what the mechanism can actually deliver.** TASK-296, closing the thread TASK-290 named. Every
  Birko SQLite database ran on SQLite's rollback journal, where a statement on a **pooled** `sqlite3`
  handle can be answered from a schema image older than a `CREATE TABLE` another connection has already
  committed — so a freshly created table reads as missing and, since TASK-285 answers that with `0`, does
  so **silently**. The fix is `SqLiteSettings.JournalMode`, defaulting to `WAL`. Seven parts generalise:
  - **The storm's verdict was the opposite of the truth, on the axis that decides.** Both candidate
    remedies looked free there: `Pooling=False` ran 2.4× *faster* than the default. Measured on the
    ordinary case instead — warm store, sequential, 200 × write+count+read — it is **1.52× slower**
    (2,731 ms against 1,801 ms), because a storm is dominated by lock contention while the ordinary case
    pays for opening a real handle per statement. WAL is **5× faster** (351 ms) and removes the defect
    too. **A benchmark taken under the pathology measures the pathology.**
  - **Only WAL is a persistent journal mode, and that fact shaped the API rather than a footnote.**
    Measured: set `TRUNCATE`, `PERSIST`, `MEMORY` or `OFF` on one connection and a *new* connection
    reports `delete`; only `WAL` comes back as itself. Since the seam applies the PRAGMA **once, on a
    connection of its own**, accepting those four would take the value and silently do nothing — so they
    are **refused**, with the reason in the message. The whitelist is `WAL` and `DELETE`, and `DELETE`
    earns its place by being the persistent way back *out* of WAL. § SH-H037 applied to a setting rather
    than to a mapper: **do not accept what you cannot deliver.**
  - **Once per database is enough precisely because the mode is persistent** — and the once-ness is proved
    by *poisoning the setting afterwards* and showing nothing notices. The first version of that test
    asserted the mode was still right after twenty operations, which a per-statement implementation would
    also have satisfied: it pinned the outcome, not the property.
  - **⚠ A mode that does not persist cannot be a fixture's marker, and this cost two flaky rounds.** The
    opt-out test put the file into `TRUNCATE` and expected to find it there later. It passed in isolation
    only because pooling happened to hand the store the same handle that had set it in memory, and failed
    at random in a parallel run — the very mechanism the task is about, arriving inside its own test. The
    discriminator that works is `JournalModeInEffect` staying **null**: a state the file alone cannot
    express.
  - **⚠ And the neighbouring test was vacuous.** *"An explicit DELETE is honoured"* asserted `delete` on a
    **fresh** database, where delete is the default — it passed however the code behaved. It now starts
    the file in WAL and shows it reverted. **When a test asserts a value that is also the default, it is
    asserting nothing.**
  - **A concurrency property that cannot be applied is RECORDED, not thrown.** `JournalModeInEffect` /
    `JournalModeFailure`, on the same terms as `IndexCreationFailures` (TASK-204) and `SubscriberFailures`
    (TASK-289). WAL needs shared memory and does not engage on most network filesystems, and SQLite
    *reports* the mode in force rather than failing — so `JournalModeInEffect` has to be **read** rather
    than assumed, and a database that cannot take WAL must still be usable.
  - **The cross-provider "should not arise" was measured, and it was only askable because of TASK-295.**
    PostgreSQL: 0 escapes over 60 cold tables × 3 concurrent callers, 2 of 2 runs — its catalogue is
    server-side and transactionally visible, so the mechanism has no analogue. Before TASK-295 that run
    would have reported a clean 0 **for the wrong reason**, because `TablesCreated` was empty there. When
    an instrument has just been repaired, note which of your negatives predate the repair.
- **A load defect is reproduced by matching the CONCURRENCY SHAPE, not by turning the load up — and the
  shape that mattered here was several callers per table, not more tables at once.** TASK-290, closed after
  nineteen hypotheses. The escape consumer Symbio had been chasing for a fortnight is now named: **a
  statement on a POOLED `sqlite3` handle is answered from a schema image older than a `CREATE TABLE` that
  another connection has already committed.** Six parts generalise:
  - **More contention is not closer to the condition.** Round 1's storm fired 200 cold tables at once and
    produced 6-7 `SQLite Error 5` per run and **0** escapes — it saturated the 30 s command timeout, i.e.
    it was *further* from the target, whose own evidence recorded `Error 5 = 0`. Waves of 24 tables × **3
    concurrent callers per table** fire on 7 of 7 runs. The reason is structural and worth carrying: a
    caller that waits on another's `_initLock` proceeds to its statement the **instant** that init returns,
    so it reads a table whose create is milliseconds old. **When a reproduction will not fire, match the
    caller topology before raising the volume.**
  - **A single-variable control is what turns a reproduction into a named mechanism.** `Pooling=False` on
    the connection string, nothing else changed: **0 of 4** runs against 7 of 7. `SqLiteSettings.GetConnectionString()`
    is `virtual`, so that control needed **no framework change** — check for an existing seam before
    building plumbing.
  - **Ask whether the thing is ABSENT or merely INVISIBLE, and there is usually a synchronous way to ask.**
    `OnSchemaEscapeDetected` is raised inside `EnsureSchemaAndReport`, so a handler runs while the failing
    flow is still on the stack; one that opens its own connection reported `presentNow=True` on **every**
    escape. That single fact killed the entire "something removed it" family — where hypotheses 1-13 and
    TASK-292 all lived — and it cost fifteen lines. Round 1's plan had called for three new public probe
    fields; the one that mattered was reachable from an event that already existed.
  - **Design the fixture so a known false-positive channel cannot explain the result.** The probe tables
    are fixed-width `Probe000`…`Probe199`, so no name is a substring of another and § TASK-293's matcher
    cannot account for any escape. That was designed in *before* TASK-293's fix existed, which is the only
    reason the numbers were usable when it landed.
  - **⚠ Say what was not measured, and do not narrate a cause you did not observe.** The internal reason
    inside SQLite or Microsoft.Data.Sqlite is **not** established. A raw-driver probe with the framework's
    shape — connection per DDL, connection per read, pooling on, readers targeting the newest committed
    table — did **not** reproduce it in 200 creates, so something about the framework's pattern beyond
    "pooled connection-per-statement" is required. That negative result is kept in the tree so the next
    attempt does not repeat it.
  - **Naming a mechanism and spending its blast radius are different tasks.** `Pooling=False` is one line,
    removes the defect in this measurement, and is **2.4× faster** in this workload — which is the opposite
    of the usual assumption about pooling and precisely why one workload on one machine is not enough to
    change the shipped default for every SQLite consumer. Filed as [[TASK-296]] with the numbers and with
    WAL named as the alternative that might keep pooling. **A measurement that makes a change look free is
    the moment to be more careful, not less.**
- **Bookkeeping a rule depends on goes in a NON-VIRTUAL wrapper around a `*Core` seam — putting it in the
  virtual method means it runs on exactly the providers that did not override.** TASK-295, and the fifth
  instance of § TASK-243's *"a funnel with four overrides is not a funnel"*. `RecordTableCreated` was called
  from the **virtual** `AbstractConnector.CreateTable(string, IEnumerable<string>)`, which PostgreSQL,
  MySQL, MSSql and TimescaleDB all override (TimescaleDB's `base.` call landing on PostgreSQL's), so
  `TablesCreated` was permanently **empty on four of five connectors** — measured live. With it went
  TASK-286's annotation (always the benign *"NO recorded CREATE TABLE"* branch), TASK-287's `SchemaEscapes`
  channel and TASK-288's healing: **a table that vanished beneath an initialised store never healed and
  every write threw until the process restarted**, i.e. the consumer-reported outage TASK-288 closed, still
  open everywhere but SQLite. Seven parts generalise:
  - **TASK-286's own comment stated the defect as a reassurance.** *"Every CreateTable overload funnels
    here, which is why this is the one place it needs to go."* The **overloads** funnelled; the
    **providers** did not. A funnel claim has to name what it is a funnel over — overloads and overrides
    are different sets, and only one of them was checked.
  - **Reach for the wrapper, not for a call in each override.** Adding the line to four overrides is a
    fourth, fifth and sixth copy of the rule and re-arms the defect for the next provider. The wrapper
    makes it unbypassable by construction: an override changes the statement and never sees the wrapper.
    This is the framework's own documented `*Core` convention — stated for stores in § Architecture —
    applied to a connector emitter, so it needed inventing nowhere.
  - **The choice between placements is settled by the ODD CALLER, and the obvious placement loses it.**
    Recording in the `IDictionary` dispatcher also covers every override — and silently drops
    `SqlSchemaBuilder`, the one external caller that reaches the single-table overload directly. Measured:
    that mutation reds the migration test and **leaves every provider suite green at full count**, so the
    wrong choice would have looked correct exactly where anyone would have looked. **Enumerate a funnel's
    direct callers, not just its overriders.**
  - **A signature change is affordable or not, and that is a measurement.** 0 overrides of the method and 0
    subclasses of any Birko connector across all 16 consumer repos, so making it non-virtual breaks
    nothing. And note the direction: an override that no longer compiles (`CS0506`) is the loud half of
    § TASK-278's hazard — the dangerous direction is *adding* a parameter, which orphans an override
    silently.
  - **Pin the structure, because the regression is invisible offline.** A reflection test asserts the
    wrapper is non-virtual and the `*Core` seam is virtual. Making the wrapper virtual again reds **that
    test and nothing else** — which is the whole reason it exists: without it, the next reader restores the
    old shape and only a live per-provider run three suites away notices.
  - **Demonstrate unbypassability with a connector that actually overrides.** A test connector overriding
    the emitter — the exact shape all four shipped providers use, i.e. the shape that skipped the recording
    — must still record. "I put it in a wrapper" is construction; a fake that tries to bypass it is
    evidence.
  - **A DEGRADED create is still a create, and that follows from TASK-254's own licence.** TimescaleDB
    records a failed hypertable conversion rather than throwing, precisely because the plain table is
    committed and usable. So the create is a fact and belongs on record — otherwise a table that later
    vanished would read as a benign first touch on exactly the entities that already have a schema problem.
    Asserted live on the Guid-keyed shape that cannot be converted.
- **Ask the question of the ERROR, not of the statement — and when a justification says "a false positive
  is harmless here", check whether that is still true.** TASK-293. `AbstractConnector` decides whether a
  schema escape is *the anomaly* (a table this connector created, reported missing) and asked it of the
  **statement**: does any recorded table name occur as a **substring** of the SQL? Two false positives
  follow, both measured, and the second needs no unlucky naming at all — a recorded `Movement` makes a
  first touch of `StockMovements` read as the anomaly, and a statement naming two tables (one created, one
  not) reads as the anomaly on the strength of the created one, which is the ordinary shape of a view or a
  multi-type count. The provider's own error names the table that is **actually** missing and names only
  that one, so the discriminator can be exact: `AbstractConnectorBase.MissingTableName` /
  `MissingTableNameChain`, deliberately in the `IsMissingTableException` family. Seven parts generalise:
  - **The comment that licensed the looseness had expired, and nothing said so.** It read *"a false
    positive costs one extra line in an exception nobody sees unless something already went wrong"* — true
    when TASK-286 wrote it, false from TASK-288 on, because the same answer now drives `SchemaGeneration`
    and therefore every store's `CanTrustRememberedInitialization`. So a fabricated anomaly invalidates the
    remembered init of **every** store on the connector, each re-running `CREATE TABLE IF NOT EXISTS` under
    the DDL lock while its own reads wait: a positive feedback loop keyed on load. **When a later change
    makes a value load-bearing, the cheapness argument attached to it has to be re-read** — the same file
    already records that TASK-286's own "diagnostic only: nothing branches on it" had to be corrected.
  - **Extract around the QUOTES, never around the English.** PostgreSQL and MySQL localise the prose in
    these messages and never the identifier, so a phrase-anchored parse silently stops matching on a server
    whose `lc_messages` is not English — and a silent non-match here *disables* TASK-288's healing rather
    than announcing anything. Same reasoning `IsMissingTableException` already records for keying
    PostgreSQL on the SQLSTATE instead of on text.
  - **Gate the extractor on the classification it belongs to.** PostgreSQL's `42P01` is also
    `missing FROM-clause entry for table "x"`, where the relation exists perfectly well; TASK-211 excluded
    that from `IsMissingTableException`, and an ungated extractor reintroduces it by parsing the quotes of
    a message it was never entitled to read. Measured live: ungating reds exactly that test.
  - **Strip the qualifier, because the map is keyed bare.** `TablesCreated` is keyed by the `Table.Name`
    the framework created, so MySQL's `birkoview.` prefix would fail to find the very entry it is looking
    for. Measured: removing the strip reds MySQL and leaves MSSql green, whose message carries no
    qualifier — so the two providers are not interchangeable evidence.
  - **Keep the loose path as a FALLBACK and pin its trigger.** Answering "not the anomaly" when the wording
    cannot be parsed is tidier code and worse behaviour: a store whose table really vanished stays broken
    for the life of the process, silently. Erring toward re-running is the asymmetry
    `AbstractStore.CanRememberInitialization` records. A test pins the trigger (a message carrying the
    wording but no extractable name) so its reachability is measured rather than assumed.
  - **Measure the wording per provider on a live server, not from the documentation.** Four different
    shapes, and the fix depends on all four: `no such table: X`, `42P01: relation "X" does not exist`,
    `Table 'db.X' doesn't exist`, `Invalid object name 'X'.` — the typed exception path is the half no
    offline test can produce, which is why each provider suite carries it.
  - **⚠ And it surfaced that the whole apparatus is SQLite-only.** Writing the per-provider tests showed
    `RecordTableCreated` is called from exactly one place — the **base** `CreateTable(string, fields)` —
    which PostgreSQL, MySQL and SQL Server all **override** without recording. So `TablesCreated` is
    permanently empty there, and with it TASK-286's annotation, TASK-287's channel and TASK-288's healing.
    Fifth instance of § TASK-243's *"a funnel with four overrides is not a funnel"*. [[TASK-295]] owns it,
    with a pin in each provider suite that says not to fix it by adding a fourth copy of the call — a rule
    with one statement and four implementations is the shape this file keeps recording.
- **A durability question must be asked while the thing that makes it durable is still in scope — and a
  rule enforced in a base class about state a derived class publishes and withdraws is a rule enforced at
  the wrong moment.** TASK-292, found while working [[TASK-290]]. TASK-244's rule is *schema-ensure
  participates in the caller's boundary, and a participating schema-ensure is not remembered*, and its own
  acceptance demanded one answer for both transaction doors. The first half landed on both; the second
  landed on one, because of an evaluation **order** rather than a missing branch:
  `AbstractAsyncStore.EnsureInitializedAsync` evaluates `_initialized = CanRememberInitialization` **after**
  `InitCoreAsync` returns, and `InitCoreAsync` publishes the per-store context with
  `using var _tx = EnterTransactionScope()` — so that scope is already disposed when the base asks, and
  `DdlSurvivesRollback`'s `AmbientTransaction == null` term answered `true` about a create sitting in a
  caller's still-open transaction. The fix captures the answer at the end of `InitCore*`, inside the scope,
  and `CanRememberInitialization` reads the captured value. Seven parts generalise:
  - **The two doors looked identical and differed only in WHO holds the scope.** `SqlUnitOfWork` is entered
    by the caller and spans the whole operation, so the base's late question still saw the ambient;
    `SetTransactionContext` is published *by the method being asked about* and withdrawn on the way out.
    Fourth instance of § TASK-274's *two doors onto one feature must give one answer*, and the first where
    the disagreement is about **when** a shared expression is evaluated rather than about what it says.
  - **The sync store is the worse half and had no correct path to compare against.**
    `SqlUnitOfWork.FromStore` takes an `AsyncDataBaseStore`, so `SetTransactionContext` is the **only**
    transaction door a sync store has — the broken one. When a defect splits by door, check whether some
    caller has only the broken door; that caller has no green neighbour to make the asymmetry visible.
  - **It manufactures § TASK-290's signature on a legitimate path, which is what made it P1.** No `DROP`,
    no concurrency: recorded `CREATE TABLE`, init gate passed, table absent. Measured on SQLite — the next
    count answered `0` with **one anomalous escape recorded** and `SchemaGeneration` 0 → 1, and the next
    write threw carrying TASK-286's annotation. A framework that can produce its own diagnostic's alarm
    condition will have that alarm misread.
  - **The provider split is measured, and MySQL's opposite answer is asserted.** The condition is
    `AmbientTransaction != null && SupportsTransactionalDdl`, so reverting the fix reds the new test on
    SQLite, PostgreSQL 16 and SQL Server 2022 and leaves MySQL 8.4 **green** — its DDL commits itself, so
    `DoDdlCommand` suppresses the ambient, the table survives the rollback and remembering is *correct*
    (TASK-243). Without that green-side test the fix is indistinguishable from a blanket "never remember".
  - **Change WHEN it is asked, never WHAT is asked.** The captured value still comes from
    `Connector.DdlSurvivesRollback` — the same expression `DoDdlCommand` consults — so the provider switch
    keeps one producer. Re-deriving the condition at the store (`TransactionContext != null ? … : …`) was
    the obvious alternative and is a second implementation of a rule this file has already watched drift.
  - **The blanket fix is the one to guard against, and the steady-state control is what catches it.**
    Forcing the captured flag permanently false reds 4 tests including `VanishedTableHealingTests`'
    *"an unaffected store does not re-initialise on every operation"* — the hook is read on every CRUD
    call, so a flag that drifted would turn every operation into a schema-ensure.
  - **⚠ Its sibling question does NOT have the same trap, and saying so stops the next reader "fixing" it.**
    TASK-288's `CanTrustRememberedInitialization` is read at *use* time, outside any of `InitCore`'s
    scopes, and compares a counter rather than asking about an ambient — so it is unaffected. The trap is
    specific to a question whose answer depends on scope that the method under test owns.
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
- **A diagnostic that rides on a THROWN exception is blind on every path that answers instead of throwing —
  and the path that answers is where the wrong answer lives.** TASK-287, the hole between two individually
  correct fixes. TASK-285 made a `COUNT` of a missing table return `0`; TASK-286 made
  `EnsureSchemaAndReport` annotate its exception when the table reported missing is one this connector
  already created. The annotation travels **on the exception**, and the count catch had just consumed it.
  Measured against a live Symbio API 2026-08-31, both halves in the same forced condition minutes apart:
  **COUNT → `200`, `totalCount: 0`, zero log lines; write → `500`, annotation logged.** Both occurrences
  consumer Symbio's TASK-602 ever recorded were **counts**, so the instrument was blind over the only shape
  ever seen in the wild — and nineteen bring-ups reporting `0` escapes was a blind instrument's `0`, not a
  measurement. Five parts generalise:
  - **A silence is only evidence where the instrument can see.** "Zero escapes in nineteen bring-ups" was
    read as good news on both paths; it was real on one and vacuous on the other. Before citing an absence,
    establish that the observed path can produce the signal — the same discipline § TASK-248 states as *a
    revert that fails nothing is a missing test*, applied to production telemetry.
  - **Discriminate on the ANOMALY, never on the condition that contains it.** The benign lazy first-touch
    failure is also a missing table and is **~245× more common per bring-up**, so a channel keyed on "the
    table was missing" is no signal at all. The discriminator is TASK-286's annotation text, and the mutation
    that swaps one for the other reds exactly the benign-path tests — which is why those exist.
  - **Where a marker is written by one method and matched by another, interpolate it.** `AnomalousEscapeMarker`
    is a constant `DescribeSchemaEscape` composes into the message and the matcher reads back, so the
    producer and the consumer cannot drift into two spellings. Same one-producer rule as the identifier
    family, applied to a diagnostic string.
  - **Walk the chain, and say whether the walk is witnessed.** `EnsureSchemaAndReport` rethrows as
    `new Exception(annotatedText, ex)` and callers may wrap again, so a check on `ex.Message` compiles, runs
    and never matches — the exact inert guard that already shipped once here. **Measured, though: on both
    live SQLite count paths the annotation sits at depth 0**, so collapsing the loop reds only a synthetic
    test. Kept as defensive-not-witnessed and labelled that way (§ TASK-261), because the depth is a property
    of the call stack, not a promise.
  - **⚠ Recording is not rethrowing, and the tempting upgrade was refused.** The table is not genuinely
    missing in the anomalous case, so `0` is a wrong answer and a throw looks more correct — but it silently
    reopens what TASK-285 closed, at roughly one bring-up in five. Adding an instrument must not smuggle in a
    behaviour change; wanting the throw is a decision with a consequence, taken deliberately.
- **A recovery branch is only as good as the state it can actually reach — and a promise made in one class
  about a flag owned by another is a promise nobody keeps.** TASK-288, the sibling of the rule above and the
  half that turns a lost operation into a permanently broken entity. `EnsureSchemaAndReport` documented
  itself, and TASK-277 justified it, as *"rethrow so this attempt is reported, but call `DoInit()` so the
  next attempt can succeed"*. Measured on SQLite with the table dropped beneath an initialised store:
  **five consecutive writes threw and `sqlite_master` held 0 rows throughout**; only a new store instance
  recovered it. Six parts generalise:
  - **TASK-277 wrote the disproof of its own next paragraph.** It records that `DoInit()` raises `OnInit`,
    which nothing in the framework subscribes to, and uses that to condemn the *swallow* — then leaves the
    identical mechanism standing one paragraph later as the *promise*. **Read what a recovery call does, then
    read what the sentence beside it claims**; a branch that neither repairs nor retries cannot become one by
    being described differently.
  - **The state that was wrong lived in a different class from the code that detected the failure.** The
    connector saw the missing table; the flag that had to change was the store's `_initialized`, and the store
    short-circuits *before* `InitCore` is reached. So the fix could not go where the defect was detected.
    `AbstractStore.CanTrustRememberedInitialization` is `CanRememberInitialization`'s other half — that one
    asks *may I remember this?* at init time, this asks *does what I remembered still hold?* at use time — and
    it is consulted in **both** the outer fast path and the inner double-check, since guarding only the outer
    one lets a thread that was waiting on the lock return without re-initialising.
  - **A PULL across that gap, never a subscription.** Connectors are cached process-wide per (type, settings
    id) while a web app resolves a store per request, so a subscriber list on the connector accumulates dead
    stores on a process-lifetime object — TASK-204's defect arriving through a different door. A counter
    (`AbstractConnector.SchemaGeneration`) that the store compares against a value it pinned at the end of
    `InitCore` costs one read per operation and cannot leak. **Pin it AFTER the DDL**: an escape seen while
    our own schema-ensure was running has already been addressed by it, and treating that as staleness
    re-runs forever.
  - **The framework's own recorded asymmetry decided it, and it is worth quoting rather than re-deriving.**
    `CanRememberInitialization` already says: answering "no" costs one idempotent `CREATE TABLE IF NOT EXISTS`,
    answering it wrongly leaves a store broken for the life of the process, *so this errs toward re-running*.
    The measurement was that exact bad outcome, so the same rule answers the same way.
  - **⚠ Healing withdrew a discriminator somebody was reasoning from, so it had to hand back a better one.**
    Symbio TASK-602 argued *"a real absence never heals, and both observed occurrences healed, therefore the
    anomaly is not an absent table"* — true only while this defect existed. The change is acceptable because
    every invalidation is recorded on TASK-287's `SchemaEscapes` channel, so an absent table is now
    **recorded** rather than inferred from a symptom. The recording is the *licence* for the healing, not a
    nicety — and a mutation shows the two halves are independent (healing without recording passes every
    observability test), so a later change that quietly drops the recording leaves a heal that costs the
    investigation its discriminator and returns nothing. **When a fix removes a signal somebody is using, the
    replacement signal is part of the fix.**
  - **Record-and-invalidate are ONE event, which is why TASK-287's call sites moved a day later.** The same
    detection has to write the record and bump the generation, so it belongs at the point the anomaly is
    identified (`EnsureSchemaAndReport`), not at each path that answers it. TASK-287's two count-path calls
    then became unreachable — the annotation they matched only exists because that handler ran — and were
    removed rather than left as a second implementation (§ TASK-247). One assertion in TASK-287's own suite
    was **inverted rather than deleted**, with the comment recording where the line moved.
  - **⚠ Spawned [[TASK-289]] at this task's close, by running the gate rather than by reading the code.**
    A subscriber to the new `OnSchemaEscapeDetected` that *throws* — which is an ordinary thing to write, and
    which the Symbio-side task about to be opened invites — replaces the annotated exception, so the write
    loses TASK-286's annotation and, worse, the count path's
    `catch … when (IsMissingTableExceptionChain(ex))` **filter stops matching** and `SelectCount` throws
    instead of returning `0`. That is TASK-285 reopened from outside the framework. TASK-288's heal survives
    only because the record and the generation bump both happen *before* the `Invoke` — luck of ordering,
    now written down as something to make explicit. Rated **P1** where its sibling [[TASK-283]] is P2, for a
    reason that is about timing rather than severity: this channel has **zero** consumers today, so it can be
    hardened for free exactly as TASK-254 hardened the hypertable channel, and that window shuts the moment
    Symbio subscribes.
- **A diagnostic channel must survive its own subscriber — and the damage a throwing handler does is not
  where you look for it.** TASK-289, found by running [[TASK-288]]'s close gate rather than by reading the
  code. `OnSchemaEscapeDetected` was raised from inside `EnsureSchemaAndReport`, between building
  TASK-286's annotated exception and throwing it, so a handler that threw **replaced** that exception.
  Measured: the write threw the handler's exception with the **annotation gone**, and — the consequence
  worth remembering — `SelectCount`'s `catch … when (IsMissingTableExceptionChain(ex))` **stopped
  matching**, so the catch never ran and the count **threw instead of returning 0**. A host reopened
  TASK-285 without touching the framework, by doing nothing worse than escalating. Six parts generalise:
  - **An exception filter is a silent coupling between a swallow and everything that can replace the
    exception.** The count path's contract was defended by a `when` clause, which cannot fail loudly: an
    exception of the wrong shape simply is not caught. Anything that can substitute the exception —
    a handler, a wrapper, a retry policy — can therefore switch off a `catch` from a distance, with no
    diagnostic. **Grep the filters before adding anything that can replace an exception in flight.**
  - **Per subscriber, never one `try` around the multicast.** A plain `handler?.Invoke(x)` stops at the
    first delegate that throws, so a host with a logger and a metric loses the metric to a bug in the
    logger. `Delegate.GetInvocationList` isolates them. This was not in the filed criteria; it fell out of
    writing the helper, which is the usual place a second silent drop is found.
  - **Swallowed must mean RECORDED — and the sink cannot itself be an event.** `SubscriberFailures`, keyed
    by (channel, exception type), same current-state contract as `IndexCreationFailures`. A broken handler
    and an event that never fired look identical from outside, and this is what tells them apart;
    announcing a subscriber failure *through a subscriber* is the same hole one level up, so the absence
    of that event is **asserted by a test** rather than left as construction.
  - **Harden a channel while it has no consumers, and check whether that window is about to shut.**
    `OnSchemaEscapeDetected` had shipped hours earlier with zero subscribers — TASK-254's free-to-harden
    position — while its sibling `OnIndexCreationFailed` has the identical hole and is consumed by Symbio's
    production code, host, tests and specs, so [[TASK-283]] must measure before changing it. Rated **P1**
    against that sibling's P2 on *timing*, not severity: the consumer task that would have created the
    first subscriber was about to be written. **Do not read the cheap hardenings as having set the
    convention** — they were cheap because nobody consumed them; but do write the fix as the general helper
    (`AbstractConnector.RaiseDiagnostic`) so the expensive one adopts it instead of adding a third variant.
  - **⚠ A fix can make its own acceptance criterion untestable, and the criterion will not say so.** The
    filed criterion asked to pin the ordering that saved TASK-288's heal "with a test that fails if an edit
    reverses it". That claim was true of the *unfixed* code and the fix made it vacuous — once the
    exception cannot escape, the generation bump runs wherever it sits, so moving it failed **nothing**.
    Caught by running the mutation, not by reading the test. Re-aimed at the ordering that remains
    observable (what a handler sees of the connector when it is called) and the mutation reds it. **Judge a
    criterion against the diff, not against its own wording**, and record the swap where the discarded
    claim would otherwise be rewritten — it reads perfectly reasonable.
  - **Closing a gate is not the same as reading the code you just wrote.** Both TASK-287 and TASK-288 were
    committed, verified and reported before this was found, by asking the single question "did this spawn
    anything?". The defect was in the eleven lines the previous task had added.
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
- **A decorator never writes to an object the inner store returned, and it never persists the caller's
  object with a value that belongs somewhere else — the two halves are one rule, and CAPTURE-AND-RESTORE
  satisfies neither.** TASK-313 / SH-H015-H018, the one-producer family arriving at an *entity* rather
  than at an identifier, a scope or a tenant. `Birko.Data.Localization`'s four decorators treat a
  localizable field's base column and its translation as the same slot: a read applied the translation
  with `prop.SetValue` to the instance the inner store handed back, and a write handed the caller's
  entity — holding *translated* text under a non-default culture — straight to the inner store. Measured
  on a store that returns live instances: one read under `sk` left the store itself holding `Stolicka`,
  and a later default-culture read returned it. Five parts generalise:
  - **Nothing in `IStore<T>` promises a detached read, so the decorator cannot assume one.**
    `AbstractInMemoryStore.ReadCore` returns `_items.Values.FirstOrDefault(...)` — the stored instance,
    no copy — and a caching decorator returns the cached reference, which is why the finding named both.
    Fixing the *store* instead is a framework-wide contract change that still would not cover the
    decorator, so the fix belongs where the write happens: the layer that did not create the object.
  - **⚠ Capture-and-restore is the obvious fix and it is measurably wrong — it writes THROUGH.** The
    first version preserved the base column by swapping the values on the caller's entity and restoring
    them in a `finally`. A store may keep the reference it is given, so the restore put the translated
    text straight back into the store, reinstating the defect being fixed. It failed 4 of its own tests.
    **Hand the inner store a detached copy.** The one place restore-in-place *is* correct is the
    filter-based update, where the entity is the inner store's own item and writing the base value into
    it is the intent — so the rule is about ownership, not about the technique.
  - **The copy is `Object.MemberwiseClone` (by reflection, since it is `protected`), never a reflection
    copy of public writable properties.** The latter silently drops anything with no public setter and
    hands the caller a partially-populated entity — a quieter defect than the one it replaces
    (§ SH-H037). The framework offers no alternative: `AbstractModel.CopyTo(null)` returns `this`, and
    an un-overridden `CopyTo(new T())` copies only `Guid`. A memberwise clone copies every field, so it
    cannot lose data; it is shallow, but that is exactly what the caller already had.
  - **Copy unconditionally on the path that is entitled to mutate, not only when there is something to
    apply.** Copying only when a translation row exists would vary per entity inside one result set —
    the kind of difference a test passes by luck. The statable rule is *a non-default-culture read
    returns a detached entity*.
  - **A filter is resolved on every path that takes one, or a destructive statement disagrees with its
    own read.** Every read path called `RewriteFilter` and no write path did, so under `sk`
    `Delete(x => x.Name == "Stolicka")` matched the untranslated base column and removed a different set
    of rows than the identical `Read(filter)` returned. Same shape as § TASK-215's *guard the whole verb
    family or none of it*, and the test that catches it needs **two deliberately crossed rows** — one
    whose translation carries the value, one whose base column does — so the predicate has a right
    answer and a wrong one.
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
- **Birko's OWN bookkeeping tables are dialect-rendered like anything else — and the bare-column rule two
  entries up does NOT extend to them.** Twelfth instance of the identifier family (TASK-332), and the first
  where the broken statement is the framework's, not a consumer's. `SqlMigrationStore` built one hardcoded
  `CREATE TABLE` for the migrations state table: ANSI-quoted identifiers, `VARCHAR(255)`, `TEXT`, and
  `TIMESTAMP` twice. Two defects followed, both firing on Birko's own table **before a single model table is
  reached**, so the SQL migration runner worked on SQLite and PostgreSQL **only** — measured on MySQL 8.4.11
  (`ERROR 1064 … near '"__Migrations_X" ("Version" BIGINT PRIMAR'`, because `"` is an identifier delimiter
  there only under `ANSI_QUOTES`) and SQL Server 2022 CU26 (`Msg 2738, A table can only have one timestamp
  column`, because T-SQL's `TIMESTAMP` is a deprecated synonym for `ROWVERSION`). Seven parts generalise:
  - **A default that nothing can pass is not a default, it is the only behaviour.** The store took
    `quoteOpen`/`quoteClose` parameters defaulting to `"`, and **0 call sites anywhere** passed them —
    framework, tests, all 16 consumer repos — while `SqlMigrationRunner` constructed it **holding the
    connector**, one line away. § TASK-247's *a fallback nobody can reach is a second implementation that
    drifts*, arriving as a parameter rather than a branch. The remedy is to delete the guessing surface:
    the connector is now **required**, so there is nothing left to guess with.
  - **Quote the bookkeeping table's columns; the entity rule is about a different statement.**
    `AbstractConnector.CreateTable` emits entity columns **bare** deliberately (§ TASK-245), and the obvious
    unification — make the migrations table bare too — is silently catastrophic: PostgreSQL folds an
    unquoted identifier, so a bare `SELECT Version` asks for `version` while **every already-deployed
    database stores `Version`**, and every read of an existing database raises `42703`. Measured by
    mutation: emitting bare reds the upgrade test and **leaves the fresh-database test green**, which is
    exactly why this class of defect needs an *upgrade* test and not a round-trip on a clean server.
    What changed is only *which* delimiters are used, so PG and SQLite DML is byte-identical and no
    existing deployment is touched.
  - **Types come from `ConvertType`, because that is the one producer for the declared side of a column**
    (§ TASK-269). Restating a type table in the migrations store is how it drifts from the entity path, and
    routing through it means every past *and future* per-provider typing fix (TASK-257's `NVARCHAR(MAX)`,
    TASK-264's precision) reaches this table without being restated. Build the field through
    `SchemaField.For`, never a bare `AbstractField`: a connector reads a width off the **runtime type**, so
    the hand-rolled version silently loses `Name`'s 255 (§ TASK-264).
  - **Identity on the settings, rendering on the connector.** `SqlMigrationSettings.FullTableName` quoted
    each part with an ANSI delimiter hardcoded in a `protected virtual` on that class — a *second* quoting
    producer, and a settings object holds no connector so it cannot answer a provider capability. It is now
    `QualifiedTableName`, the bare `Schema.Table` identity, rendered by
    `AbstractConnectorBase.QualifiedIdentifier`. **Renamed rather than redefined**: an external reader of
    the old name would silently have received unquoted SQL where it used to get quoted, and a compile error
    is the loud direction (§ TASK-260). 0 external readers measured before renaming.
  - **⚠ The two defects do not overlap, and that is why the consumer saw two different errors.**
    `Microsoft.Data.SqlClient` connects with `QUOTED_IDENTIFIER ON`, under which SQL Server **does** accept
    `"` as an identifier delimiter — measured directly: the same statement is `Msg 102` under `sqlcmd`'s
    default `OFF` and reaches `Msg 2738` with `-I`. So defect 1 is MySQL-only in practice and defect 2 is
    MSSql-only, and **fixing either alone leaves one server broken**. Do not infer one provider's behaviour
    from another's in this family; measure per provider, through the driver the framework actually uses.
  - **The statement existed twice — sync and async — and the duplication was load-bearing.** A mutation
    reverting **only** the async copy reds exactly the two async tests on exactly the two broken dialects
    while every sync test stays green: half a fix that a sync-only suite would have called done. Both paths
    now render from one producer, and both are covered per dialect.
  - **`DEFAULT CURRENT_TIMESTAMP` needs no capability, and that is a measurement rather than an assumption.**
    Accepted by SQLite, PostgreSQL 16.15, MySQL 8.4.11 (on `DATETIME`) and SQL Server 2022 (on `DATETIME2`).
    Stated because the obvious reading — "a default expression is surely not portable" — would have bought a
    provider switch nothing needs. The one behaviour change on a working dialect *is* recorded and pinned:
    SQLite's `Version` is now `INTEGER PRIMARY KEY`, i.e. a rowid alias, so a full-width version is asserted
    to round-trip exactly.
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
- **A parameter documented as "SQL" has no containment story, so the fix is the API's SHAPE, never a
  validator — and where the fix needs an open set, a validated IDENTIFIER contains it without closing it.**
  TASK-260, the last member of the identifier family and the one that ends it for this class.
  `BuildContinuousAggregateSql` took `selectClause` and `groupByClause` as raw SQL in *statement* position:
  not inside a literal, not an identifier, so neither escaping nor quoting could reach them. Six parts
  generalise:
  - **Recognise the shape: uncontainable is a property of the PARAMETER, not a gap in the escaping.** Every
    other caller-derived value in that class sits inside a literal or an identifier position and is
    therefore containable. These two were expression *lists* — arbitrary SQL by definition — so a guard was
    never available and TASK-253 correctly pinned the boundary rather than pretending to close it. **When a
    parameter's documentation has to say "this is SQL, do not build it from untrusted input", that sentence
    is the defect**, and the remedy is structured values the builder composes.
  - **⚠ A closed set is the obvious containment and it can be the WRONG guard — measure what the server
    actually accepts.** This task's own acceptance criterion demanded an enum, reasoning *"a passthrough is
    the same hole with more ceremony"*. Measured on TimescaleDB 2.29.2, a continuous aggregate accepts
    **essentially any aggregate** — `array_agg`, `string_agg`, `bool_and`, even the ordered-set
    `percentile_cont`, plus user-defined ones. An enum would therefore have imposed a **brand-new**
    restriction rather than mirroring the server's, refusing aggregates that work today. **The criterion was
    amended openly, on the measurement, rather than quietly satisfied** — its *purpose* (containment) was
    kept and its *letter* dropped, which is the honest way round when a requirement predates the evidence.
  - **A validated identifier is not a passthrough, and the difference is measurable rather than rhetorical.**
    A passthrough accepts arbitrary text; this accepts a single bare identifier through
    `DataBase.ValidateColumnIdentifier` — TASK-255's producer, so the class has **one** rule for "a name
    interpolated bare into a statement". A payload fails the guard; a name that merely does not exist can
    only *fail* the statement, at DDL time, with `42883` naming the function and its argument types.
    Nothing swallows it (`42883` is not `42P01`, `ExecuteScript` has no `catch`, and TASK-254's degrade is
    on the store path). That is exactly the property the validator already claims: **a bare identifier that
    names nothing is at worst a database error, which is a wrong answer that reports itself.**
  - **Do not assume the structured replacement loses the capability — measure that too.** The plan was
    written expecting to give up an expression group-by (`date_trunc('day', Ts)`), and prepared to record
    the loss. Measured: it is **legal** in a continuous aggregate, so grouping got the *same* structured
    shape as the projection (function + escaped literal + validated column) and the capability survives.
    "Unusual, so probably fine to drop" was plausible and unmeasured; the measurement is what kept a real
    capability.
  - **Removing a parameter is safe in a way that inserting one is not: change the TYPE.** All 14 call sites
    failed loudly with `CS1503` — contrast TASK-255, where inserting a same-typed `string` let two
    6-argument calls **silently rebind**. Where a signature must change, prefer the change the compiler
    cannot miss. (And count the call sites with the *right* grep: the first measurement here said "three"
    because it covered only the `protected virtual` wrapper and missed the `internal static` the tests
    actually call — caught at the close gate by judging the criterion against the diff instead of its tick.)
  - **Replacing beats an obsolete overload when the parameter IS the defect.** Criterion 7 permitted keeping
    a string overload; keeping one would have left the uncontainable surface callable and the defect
    reachable, i.e. failed the task's own definition of done. Note the justification: **not** TASK-247's
    dead-fallback rule, which is about drift and was the weaker argument an earlier draft reached for.
    Affordable because 0 of 16 consumer repos call it.
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
- **A translator that cannot express a node REFUSES it, and "nothing was produced" is never a scope
  answer — because the same empty state also means "constrains nothing", which is a different thing.**
  Sixth member of the SH-H037 family, and the one that shows the read path needs its own guard: SH-H002 /
  TASK-137 / TASK-215 all refuse a *destructive* statement that constrains nothing, and a **read** has no
  such decision point, so the identical predicate silently returned every row for the life of the
  framework (TASK-308, `SH-H021` + `SH-H026`). `DataBase.ParseConditionExpression` dispatches on lambda,
  unary, binary, method-call and member nodes — in three independent `if` groups — and ends in
  `return Array.Empty<Condition>()`, so a `TypeBinaryExpression` (`x.Payload is string`), an
  `InvocationExpression` (`x => pred(x)`) or a `NewExpression` produced **no conditions and no
  complaint**. Measured on SQLite, 3 rows: top level → READ **3 of 3**; as an `||` operand → the **whole**
  predicate rendered no `WHERE` and READ 3 of 3; as an `&&` operand → the term silently dropped.
  `RequireTranslatableNode` refuses at the top of the recursion. Six parts generalise:
  - **The empty state is OVERLOADED, so the fix goes where the ambiguity is created, not where it is
    read.** `IsConstantBoolCondition`'s `Values == null → true` branch looks like the defect and editing it
    is the smaller diff — and it is wrong: measured, `(x.A == 1 || true) && x.B == 2` leaves the identical
    "nothing was parsed" state for its left operand and `true` is the **correct** answer there. Merging the
    two cases trades a silent match-all for a silent narrowing. Guard the unclaimed *node*; leave the
    reader alone.
  - **One guard at the top of a recursion covers every position, which is why two findings had one fix.**
    They were filed as separate defects — an operand of `||` and a node at the root — and are the same
    root cause seen twice. Before writing a guard per site, ask whether the recursion's entry point sees
    all of them.
  - **Refuse with the type the OTHER backends already throw.** `NotSupportedException`, matching
    `ElasticSearch.ParseFilterQuery` for the same predicate (TASK-268), so one `catch` selects "this
    filter cannot be translated" on either backend — the same one-catch-everywhere reasoning
    `WholeTableWriteException` records, applied to untranslatability. There was already a precedent inside
    the same file (`RenderValueFragment` refuses an untranslatable *value* operand with that type), and
    finding it is what settled the choice; **grep the file you are editing for an existing refusal before
    inventing one.**
  - **A whitelist of accepted node kinds is PERMISSIVE on purpose, and that asymmetry is written on it.**
    `ConstantExpression` and `ParameterExpression` are accepted although neither is known to reach the
    guard, because a false refusal breaks working code and is worse than the hole (`PredicateScope`'s own
    rule). Adding a kind is safe; removing one is not, and the doc comment says so — otherwise the next
    reader "tightens" it into a regression.
  - **⚠ The refusal message carries the node's TYPE, never `expr.ToString()`.** A rendered expression tree
    interpolates the values a closure captured, and such a message travels into logs and error responses.
    Caught at the security pass on my own change, and the same discipline the identifier family records:
    a diagnostic needs the shape, not the data.
  - **Five existing tests asserted the wide behaviour and were INVERTED rather than restored.** All five
    used the `InvocationExpression` shape as their example of "nothing rendered" and asserted
    `WholeTableWriteException`; the rows they assert are unchanged, only the exception type moved upstream.
    § TASK-211's rule — *a narrowing will break tests that assert the wide behaviour, and those are the
    interesting ones* — plus the measurement that made it affordable: **0** consumer references to
    `WholeTableWriteException` anywhere. And SH-H002's decision *not* to distinguish its causes is
    unaffected and now has its own pin: a null filter and a reduces-to-everything filter still render
    nothing and are still answered identically, because only the *untranslatable* cause moved.
- **A collapse that unwraps a group must COMBINE the enclosing flags, not assign over them — and the
  destructive guard cannot help, because the clause it produces is non-empty and simply wrong.**
  TASK-308 / `SH-H022`, the most serious of its area's seven and the only one SH-H002 could not cover.
  `ReturnSingleSubCondition` collapses `A && trueConst` to `A` and assigned `parent.IsNot =
  surviving.IsNot`, while the `Not` branch has already toggled `IsNot` on the very object it hands down as
  the parent. Measured: `x => !(x.Amount == 10 && trueFlag)` rendered `WHERE Amount = @p` against the
  control `x => !(x.Amount == 10)`'s `WHERE NOT (Amount = @p)`, so the read returned **the exact
  complement** and `DeleteAsync` **threw nothing and destroyed that complement**. Three parts generalise:
  - **XOR, not OR — two negations cancel.** `!(x.A != 1 && true)` is `x.A == 1`. A fix using `||` or `|=`
    negates twice and returns the complement in the other direction, which is why that case gets its own
    test rather than being assumed away.
  - **The file already knew the shape for the SIBLING flag.** The `.Date` range branch nests rather than
    merging, with a comment explaining that this very method would overwrite a nested `IsOr`. The `IsNot`
    half went unnoticed beside it for a year. **When you find a comment describing a hazard in one field
    of an object, check the object's other fields for the same hazard.**
  - **⚠ `!=` is `ConditionType.Equal` with `IsNot` on the comparison LEAF — there is no `NotEqual` type —
    and the comparison branch NESTS that leaf under the parent.** So the survivor handed to a collapse is
    a wrapper whose own `IsNot` is false. Two of the fix's own tests were written on the opposite
    assumption and failed; the correction is recorded in the test, because reading `IsNot` as "the leaf is
    negated" is exactly what makes the XOR look like the wrong operator.
- **A sub-translation that fails must not leave a query that MEANS something else — and a guard on the
  top-level result cannot see it, because the result is non-null.** TASK-308 / `SH-H025` + `SH-H027`, the
  ElasticSearch face of the rule above, and the pair that shows TASK-268's boundary guard was necessary
  but not sufficient. Measured by rendering the NEST query offline: a range whose bound
  `Convert.ToDouble` could not produce came out as `NumericRange(gt=NULL, gte=NULL, lt=NULL, lte=NULL)` —
  an **unconstrained** range over every document with the field — and `CombineBool` returned a one-clause
  `BoolQuery` when a single operand was untranslatable. **Both survived `ParseFilterQuery` *and*
  `ParseRequiredFilterQuery` with no throw**, so `DeleteByQuery` targeted the whole index on
  `x => x.Date < cutoff`, the most ordinary shape a time-series filter has. Four parts generalise:
  - **A dropped operand is wrong in OPPOSITE directions, which is why neither is tolerable.** A dropped
    **conjunct widens** the match, so a by-query delete destroys documents the caller excluded; a dropped
    **disjunct narrows** it, so a read silently misses rows. "Keep whatever translated" looks
    conservative and is not conservative in either direction.
  - **Refusing is only safe because `null` means exactly one thing, and that has to be asserted.** A
    predicate that legitimately matches nothing is a `MatchNoneQuery` (TASK-266) — non-null, composing
    normally — so `null` means *untranslatable* and refusing on it cannot break an empty-collection
    filter. Without a test for that the refusal is indistinguishable from one that is too broad.
  - **The minimal reading of the finding was the wrong fix, and § TASK-281 is the rule that caught it.**
    "Never emit a null bound" would have made every `DateTime` comparison untranslatable, and a date range
    is the commonest filter this store serves. NEST offers `DateRangeQuery`, so the defect is removed
    **and** the capability gained. Route by the value's type; refuse only what has no mechanism.
  - **⚠ An arm nobody can reach is not a capability, and I wrote one before measuring.** A
    `TermRangeQuery` branch for a `string` value is unreachable: `string` declares no ordering operators,
    so such a comparison cannot be written in C# at all. Removed, and the **premise** is pinned rather
    than the absence — § TASK-263's rule that a gap which is a decision reads as an oversight unless you
    say so.
- **A caller's value that reaches a query LANGUAGE is contained by choosing a query type with no grammar,
  not by escaping the grammar.** TASK-308 / `SH-H028`, and the identifier family's first instance outside
  SQL. `ParseContains` put the caller's string verbatim into a NEST `QueryStringQuery`, i.e. into Lucene's
  query grammar: measured, `secretField:*`, `* OR Count:5` and `unbalanced(` all rendered verbatim, so a
  search term could address fields the predicate never mentioned and unbalanced syntax became a parse
  failure rather than a no-match. Three parts generalise:
  - **Prefer removing the grammar to escaping it.** A `WildcardQuery` value means nothing beyond `*`, `?`
    and the escape character, so escaping those three is **total by construction** — where escaping
    `query_string` is a blacklist against a set that its own documentation lists at fifteen characters and
    can grow. Same instinct as parameterising a value instead of quoting it.
  - **The right containment also fixed what the API claimed to do.** `Contains` is documented as a
    substring match mirroring SQL `LIKE '%x%'`, which `query_string` over a *keyword* field never was. A
    containment that restores the documented meaning is the one to pick.
  - **Guard the whole verb family: `EndsWith` built a wildcard pattern from caller text too**, unescaped,
    so `EndsWith("a*b")` matched more than it named — narrower than the filed finding (wildcards only, no
    field access) and the same one-line fix, through one shared escaper. `StartsWith` needs nothing
    (`PrefixQuery` has no pattern syntax) and is **asserted unchanged**, because escaping there would
    corrupt a legitimate prefix.
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
    **Fourth instance (TASK-264), and the one that names the mechanism: a connector reads a column's WIDTH
    off the field's RUNTIME TYPE, not off a property.** `SchemaField` adapts a migration's
    `FieldDescriptor` to the SQL field model and forwarded **5 of its 15** properties, deriving straight
    from `AbstractField` — while `ConvertType` tests `field is CharField` before it will emit
    `NVARCHAR(n)`, and `field is DecimalField && Precision != null && Scale != null` before
    `DECIMAL(p,s)`. It satisfied neither test, so a declared `maxLength` produced the unbounded type and a
    declared `precision`/`scale` produced a bare `DECIMAL` — default scale **0** on SQL Server and MySQL,
    i.e. **money truncated to whole units**, and on **SQLite, this framework's default provider**, an
    unqualified decimal falls back to **`REAL`**, so a column declared `DECIMAL(18,2)` held binary
    floating point. Both silent. So: **a descriptor is adapted through ONE factory that dispatches to the
    field subclass carrying the metadata** (`SchemaField.For`), mirroring `CreateAbstractField`'s dispatch
    **including its `MaxLength`-then-`Precision` fallback for strings**, so the two producers cannot
    disagree about what a length is — and every construction site goes through it, which the mutation
    bypassing only the `ALTER TABLE ADD` site proves was necessary (it reds exactly one test). Three
    corollaries. **The filed half need not be the worse half**: the task named `MaxLength` and the
    unfiled decimal half was nastier, and fixing one without the other is § TASK-207's *re-keying half a
    dictionary is not a fix, it is a narrower bug*. **Both sides of every such switch are pinned** — an
    undeclared length must *still* be unbounded and an undeclared precision must still be the provider's
    default, or the fix is indistinguishable from imposing a ceiling on values that write fine today
    (§ TASK-248) — and SQLite's `TEXT` is asserted as *correct* rather than as a gap, since it has no
    length-enforcing string type and pinning it is what stops a later reader "fixing" it into a
    divergence. And **`IsIndexed` cannot be set on this path at all**: `SqlCollectionBuilder` and
    `SqlIndexBuilder` are separate builders with separate `Build()` calls and no shared state, often in
    separate migrations, so at `CREATE TABLE` time nothing knows an index is coming — `LoadIndexes`' trick
    of seeing a whole entity's attributes at once has no analogue. Honouring `MaxLength` resolves the case
    that matters and leaves the undeclared one **loud** (Msg 1919 / ERROR 1170 on the explicit
    `CreateIndexes` call, which per TASK-204 still throws).
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
  - **Fifth member (TASK-266): the same veto for `byte[]` — and the remedy the task wanted was not
    expressible, which is what widened the fix.** `ConvertType` mapped `DbType.Binary` to
    `VARBINARY(MAX)` / `LONGBLOB` unconditionally, and neither provider can use an unbounded blob as an
    index key, so a `[UniqueField] byte[]` entity had **no table at all** — measured on SQL Server 2022
    (16.0.4265.3) the inline `UNIQUE` is **Msg 1919 + Msg 1750**, which `TRY/CATCH` cannot intercept, so
    the batch aborts and the whole `CREATE TABLE` fails; **ERROR 1170** on MySQL 8.4.11. Bounded at those
    two providers via `IsInIndexKey`, **never refused at load**, because the identical declaration is
    legal on PostgreSQL and SQLite and a framework-wide refusal would break a working entity on two
    providers to fix two others — TASK-248's veto, third time it has decided one of these. Four parts:
    - **"Declare a width" is only a remedy if a width can be declared.** `BinaryField` had no length at
      all and `CreateAbstractField` never passed `maxLength` for a `byte[]`, so `[MaxLengthField(32)]` on
      one was **silently dropped**; shipping only the provider bound would have been § TASK-263's *escape
      hatch that did not open*. Opening it is half the change, and it is the shape a real binary key (a
      hash, a UUID) actually wants. Spelled `MaxLength`, deliberately not `CharField.Lenght` — a
      misspelling that is shipped public API is not a convention a new member inherits.
    - **Gate on the field's runtime type, because `DbType.Object` shares that `case` on all four
      connectors.** A serialized object has no byte width and a length applied to one would truncate it;
      test both ways, including that a null field neither NREs nor gets bounded.
    - **255 is a cross-provider agreement, not either server's ceiling** — measured, `VARBINARY(901)`
      indexes on SQL Server (the real limit being 1700 bytes nonclustered) and `VARBINARY(3072)` on MySQL.
      The same model runs on both, so a width that indexes on one must index on the other. Same reasoning
      TASK-257 recorded for the string knob, and the same shape of escape hatch —
      `protected virtual int IndexedBinaryColumnLength`, overridden in a test on each provider, because
      *"the real ceiling is the key limit, not this number"* is otherwise a comment nothing enforces.
    - **⚠ The wide composite is PINNED, not guarded, and the two providers answer oppositely.**
      4 × `NVARCHAR(255)` is 2040 bytes against SQL Server's 1700: it creates the index **with a warning**,
      rejects a max-width INSERT (Msg 1946) — **and a short row still inserts fine**. So it is
      data-dependent rather than broken, and refusing at DDL would break working code (`PredicateScope`'s
      *a false refusal breaks working code*). MySQL **refuses** the same 4-column index outright
      (ERROR 1071, 4080 of 3072 bytes), so a framework guard would duplicate one server while regressing
      the other — and note how tight that margin is: three columns is 3060 bytes, inside the limit by
      **twelve**. It is also not computable where the type is chosen: `ConvertType` sees one field, never
      an index.
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
- **`IsNullOrEmpty` on a parameter whose `null` MEANS something converts a missing configuration value
  into a different behaviour — and the tell is that its neighbours fail loudly on the same input.**
  TASK-284. `add_continuous_aggregate_policy`'s `start_offset` takes `NULL` to mean *"refresh from the
  beginning of time"*, and the emitter used `string.IsNullOrEmpty`, so a value that came back `""` rather
  than null produced a far **wider** policy than the author intended — every chunk, on every run of the
  job, with no error anywhere. Config binding, `LoadFrom` and JSON/env deserialisation all produce `""`
  where nothing was written. Four parts generalise:
  - **Look along the row before deciding a null-check is harmless.** Every other interval in that class —
    `endOffset`, `scheduleInterval`, `compressAfterInterval`, `dropAfterInterval`, the chunk interval —
    already failed loudly on `""`, because an empty string reaches `INTERVAL ''` and PostgreSQL answers
    `22007`. One parameter behaving differently from its five neighbours is the signal; the fix is to make
    it behave like them, not to make them tolerant.
  - **The dangerous half is that the wrong value fails by WORKING.** An over-wide refresh policy is a
    valid policy — it runs, it succeeds, it is simply much heavier and covers history the author did not
    ask for. Nothing surfaces it, which is why both behaviours are now written on the method: the caller
    has no other signal that `null` and `""` differ.
  - **⚠ Measure an "untested escape hatch" before assuming it is broken — a clean answer is a result.**
    The same task suspected that the door its refusal names (*"pass a null startOffset"*) might not open,
    since `add_continuous_aggregate_policy` declares its parameters `"any"` and an untyped bare `NULL` is
    exactly what a server can reject. Measured on 2.29.2: **accepted**. So the fix was not to change the
    message but to give the door a *live* test, because it had only ever been asserted as a rendered
    string. § SH-H037 requires the opt-out to be checked, not assumed broken.
  - **⚠ A suite can ENCODE the defect, in which case testing was never going to find it.** The empty-string
    behaviour was an `[InlineData("")]` row asserting it meant all-of-history — a documented, asserted
    contract rather than an untested corner. When a defect survives a well-tested area, check whether a
    test is asserting it.
- **A test teardown that reaches process-wide state damages a PARALLEL sibling, and the victim is never
  the file that caused it.** TASK-276. `SqliteConnection.ClearAllPools()` drops the pooled connections for
  every connection string in the process; xUnit runs test classes in parallel by default here, so a class
  calling it in `Dispose` disposes the `sqlite3` handle a sibling is mid-statement on. That sibling fails
  with `ObjectDisposedException: 'SQLitePCL.sqlite3'` — at random, always mid-statement, always passing in
  isolation. Five parts generalise beyond SQLite:
  - **Nothing about the symptom points back at the cause, which is why it needs a static guard and not a
    fix.** The failing file is innocent, so a future author debugging it has no path to the teardown that
    did it. `SqlitePoolIsolationTests` scans the project for the call; the helper (`SqlitePool.ClearFor`)
    is what they use instead. A cleanup without a guard in the same change comes back — and this call is
    especially prone to it, being the obvious one and the one every example online uses.
  - **A guard that must NAME the thing it forbids has to assemble the name**, or it reports itself — and
    the usual fix for that, excluding its own file, makes that file the one place the rule is unenforced.
    Measured: it caught itself on the first run because the literal was still in its `<remarks>`.
  - **⚠ Sample size decided the fix, and a small sample pointed the wrong way.** The obvious remedy is to
    delete the calls (*"every test owns its own database file, so the clear buys nothing"*). Of **400**
    sampled leaked temp directories **164 still held files**, so the pool does hold handles and the clear
    does release them — deleting outright would have made things worse. The first **6**-directory sample
    was all-empty and supported the wrong conclusion.
  - **Where a clear is per-key, the key is the whole connection string, not the resource.**
    `SqLiteSettings.GetConnectionString()` emits `Data Source={Path};Default Timeout={n}`, so two settings
    over one file are two pools. Asking the settings object for its own string cannot guess wrong; a
    path-only sweep can, and its limits belong on the method rather than in a reviewer's head. Both the
    premise and the sweep's fallibility are pinned by tests.
  - **⚠ A fix whose before/after shows nothing is justified by MECHANISM, and must say so.** Measured here:
    12 idle runs and 6 under load before, 12 after, **0 failures in all of them** — the flake did not
    reproduce on that machine that day, so the numbers distinguish nothing. What justified the change was
    the mechanism confirmed three independent ways (a dose-response in this suite, a consumer's 14-run
    measurement of the same exception in two different classes, and their analysis). *A revert that fails
    nothing is a missing test; a **fix** whose before/after fails nothing is a missing **reproduction**,
    and the two must not be reported the same way.*
- **A process-wide cached object keeps attracting per-caller state, and the only thing that stops the
  next instance is a test.** TASK-270. `DataBase.GetConnector` caches a connector per (type, settings id)
  for the life of the process, so it is reachable, shared and long-lived — and **four** independent
  features have now put one caller's state on it: the unit-of-work's `DbTransaction` (TASK-240), the
  append-only index-failure list, the migrations builder's connection *and* transaction (TASK-259), and
  `IsInitializing`. Four developers reached for "just put it on the connector" independently. Five parts
  generalise:
  - **§ Conventions saying so is not a mechanism — instances two, three and four all shipped *after* the
    reasoning was written down.** `ConnectorSharedStateTests` asserts the shape by reflection: no settable
    public/protected instance property, and no `Set*` method taking a `DbConnection`/`DbTransaction`
    (instance three's exact signature, as a regression guard now that it is deleted). *"I didn't add
    mutable state"* is construction; a test that fails when someone does is evidence.
  - **The exemption is a LEDGER with a reason per entry, and the ledger has its own currency test.**
    A flat "no settable state" rule was not achievable without a breaking change, and a rule that cannot
    pass gets deleted. `RetryPolicy` is the one entry, with its justification attached — and a test fails
    if an entry stops being needed, because a ledger that keeps a stale entry stops being a record and
    becomes a blanket (§ TASK-222's rule, applied to state instead of to query shapes).
  - **⚠ The obvious relocation was measured and rejected: moving it onto `Settings` HIDES the sharing
    rather than removing it.** `Settings.GetId()` is `Location:Name(:UserName:Port)` and carries neither
    `RetryPolicy` nor `CommandTimeout`, so two settings objects differing only in one of those **already**
    share a connector and the first caller's value silently wins for everyone. One policy per database is
    the correct scope anyway, since the connector *is* per database. **Check what the cache key actually
    contains before "fixing" shared state by moving it next to the key.**
  - **A re-entrancy guard is per CALL FLOW, and a flag on a shared object is not one.** `IsInitializing`
    was a plain mutable bool guarding `DoInit`, so a second flow arriving while the first was inside its
    `OnInit` handlers had its initialisation **silently discarded** — not deferred, not retried; it
    returned believing init had run. The unsynchronised check-then-set was the smaller half. Now an
    `AsyncLocal<bool>` per instance, entered through a scope — flow-scoped *and* instance-scoped, the same
    mechanism TASK-240 used to move the transaction off this same object. And the scope **restores on
    exception**, which the old assignment pair did not: a throwing `OnInit` handler left the flag stuck
    `true`, permanently suppressing `DoInit` for every caller of that database (§ TASK-289's family,
    arriving through a guard instead of a diagnostic).
  - **⚠ Correctly-shared state still has cross-caller reach, and that is a different finding from the
    four above.** `SchemaGeneration` *should* live on the connector — it is about the database, not about
    a caller — and TASK-288's healing depends on every store seeing it. The consequence, measured while
    closing this task: in a suite where all classes share one settings id, **one class's deliberate schema
    escape re-initialises another class's store and re-creates a table that test had just dropped**, at
    about 1 run in 5 (identity captured on [[TASK-276]]; 8/8 clean when the class runs alone). Not a
    defect, and **not a ledger entry** — but a test whose premise is a store's initialisation history must
    not share a connector with tests that invalidate it.
- **A check that compares DECLARED against STORED asks the schema for the stored side, never the driver
  — and the declared side is the method that emits the DDL.** TASK-269, and the first member of the
  reporting family (TASK-204, TASK-254, TASK-287, TASK-289) that has to *read* the database rather than
  record what the framework already knows. The framework never reconciles an existing table —
  `CREATE TABLE` is guarded by `IF NOT EXISTS` and schema-ensure only creates — so a model change, or an
  upgrade past any of the column-typing fixes (TASK-257, TASK-264, TASK-265, TASK-266, TASK-275), leaves
  the old column in place with no signal but an exception at the call site. Six parts generalise:
  - **⚠ The provider-independent surface CANNOT see a type's parameters, so the obvious implementation
    misses the worst case.** Consumer Symbio's `SchemaDriftCheck` reads `SELECT * FROM T WHERE 1 = 0` and
    takes the reader's column *names*, deliberately avoiding a catalogue query on the stated grounds that
    *"a diagnostic that only runs on the dialect the developer happens to use is precisely how this defect
    survived in the first place"*. Extending that with `GetDataTypeName()` is the natural move and is
    measurably wrong: it returns `VARCHAR` for `VARCHAR(255)` and `DECIMAL` for `DECIMAL(18,2)`, and
    `GetColumnSchema()` answers `ColumnSize = -1` with **null** precision and scale. So the check would
    report TASK-264's silent money truncation — `DECIMAL(18,0)` against `DECIMAL(18,2)`, *the same
    keyword* — as a clean bill of health. **The reasoning was right for names and does not transfer to
    types; check whether a borrowed justification covers the thing you are borrowing it for.**
  - **The dialect branch is therefore the only mechanism, and it goes on the connector — where every
    other provider capability already is.** `StoredColumnsSql` / `RenderStoredType`, in the family of
    `SupportsTransactionalDdl`, `FoldsUnquotedIdentifiers`, `SupportsPartialIndexes`,
    `RequiresOrderByForPaging`: stated once per provider, both sides asserted per provider. Symbio's
    objection was sound *for Symbio*, which has no connector to hang a branch on. The framework does, and
    the answer to "don't let a dialect branch rot" is to state it once and test it, not to avoid it.
  - **⚠ `DbDataReader.GetFieldType()` reports the stored VALUE's affinity, not the declaration.**
    Measured on SQLite: a `REAL` column holding the text `'not-a-number'` reads back as `String`, and one
    column answered `String` on a zero-row reader and `Double` on a populated one. A check built on it
    reports drift, or not, according to which rows happen to be in the table. `GetDataTypeName()` is
    stable across all three cases — it is *insufficient*, not unstable, and the two failure modes are
    worth telling apart.
  - **The declared side has ONE producer and it is `ConvertType`, the method `CREATE TABLE` uses.** So
    the check cannot drift away from the DDL, and every past *and future* column-typing rule is covered
    without being restated. A check that re-derived the expected type would be the second implementation
    of the rule — the shape this file keeps recording. It also means a test must never spell out an
    expected type by hand for the healthy case, or the test becomes the third.
  - **"Could not answer" is never "nothing is wrong".** `SchemaDriftReport.Supported` /
    `TableExists` / `IsClean` keep them apart: a provider with no readable catalogue, an unmapped type, or
    a table a lazy store has not created yet must not read as healthy. That conflation *is* the defect
    this family exists to close — § TASK-287's *a silence is only evidence where the instrument can see*,
    applied at the moment of reporting rather than of measuring. And a diagnostic must not throw on the
    likeliest mistake a host makes: `DataBase.LoadTable` answers **null** for a type with no `[Table]` and
    no `ModelMap`, which surfaced as a `NullReferenceException` until it was guarded.
  - **⚠ But "not clean" and "not healthy" are DIFFERENT questions, and the split is self-healing versus
    permanent.** The first version of this rule said an unchecked type "must not read as healthy", and the
    shipped check deliberately contradicts it — because stores create their table on first use, so at boot
    **every** table is absent, and a Degraded there makes every fresh deployment Degraded until each entity
    happens to be touched. That is a report an operator learns to ignore, i.e. the same defect one level
    up. So an **absent table stays Healthy** (expected, self-healing) while an **unsupported provider is
    Degraded** (permanent, never resolves), and the honesty requirement moves to the *wording*: the
    description may never claim a match for a type it did not check. `IsClean` on the report is still false
    for both — the report answers *"was this verified"*, the status answers *"should anyone act"*.
    **Found by a human review harness, not by the tests**, which asserted `IsClean` on the report and never
    read the one line an operator actually sees: the check said *"Schema matches the models (1 type(s)
    checked)"* about a table it had never read. **When a rule about reporting is written, check it against
    the rendered output, not against the model behind it.**
  - **The subscriber ships in the same change, or this is TASK-204 again.** Re-measured 2026-09-07:
    `OnIndexCreationFailed +=` has **0** subscribers across all 16 consumer repos, so every index failure
    since TASK-204 has been silent while TASK-245, TASK-248 and TASK-257 each found real ones hiding
    behind it. So the health check reports drift **and** `IndexCreationFailures` — one door for *"is my
    schema what my models think it is?"* — and it is `Degraded`, never `Unhealthy`, because pulling an
    instance out of a load balancer for a condition only a human can fix is how a diagnostic becomes an
    outage. It lives in a **per-dependency sibling** (`Birko.Health.Data.SQL`, like `.Redis` and
    `.Azure`), because `Birko.Health.Data` is dependency-free by construction; TASK-234 refused exactly
    this edge for Redis, and the distinguishing measurement is that a Redis check is useful *without*
    Birko.Redis while a schema-drift check is meaningless without Birko.Data.SQL.

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
verify-birko-conventions, roll-birko-changelog) are reachable only inside this repo — **and reachability
comes from a distinct name, not from shadowing.** Measured at TASK-267: a skill name present at both
`~/.claude/skills/` and this repo's `.claude/skills/` resolves **user-level first**, so the two that used
to share a generic name (`verify-conventions`, `roll-changelog`) never ran at all, and every close gate
silently linted with the generic skill while the repo believed otherwise. Project-local skills *are*
discovered — one with no user-level twin resolves here — so the defect was precedence, never discovery.
The gate is now wired the other way round: the generic `verify-conventions` **globs for
`.claude/skills/verify-*conventions*/SKILL.md`**, runs its own pass, hands off, and names the extension
on its report header — reporting a **blocker** if it finds one it did not run. So the Birko checks are
reachable through either door, and a run that skipped them says so instead of reporting a clean pass; the
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

The rolling per-change log now lives entirely in [CHANGELOG.md](CHANGELOG.md) (newest-first). Add new architectural / behavioral change notes here as `### Title (YYYY-MM-DD)` entries; when this section grows past ~5–8 entries, roll the oldest into CHANGELOG.md (the project-local `/roll-birko-changelog` skill does this). Granular code-review-remediation progress is tracked in `tasks/EPIC-014-code-review-remediation`, not here.


### A ViewModel update blanked every column the ViewModel could not express (2026-09-17)

TASK-316 / `SH-H034` + `SH-H035`, ranked to the top of [[STORY-051]] as silent corruption of a stored row
on an **ordinary** write path. **Both confirmed WIDER than filed.** 37/37 in
`Birko.Data.ViewModel.Tests` (17 pre-existing + 20 new), 263 across seven suites, **eleven disjoint
mutations, every one red**. Ten things worth carrying:

- **A ViewModel is a PARTIAL projection, so an update built from a fresh model is structurally unable to
  be correct.** `Update` called `LoadModelInstance` — `CreateModelInstance()` + `MapToModel` — and never
  read the row; every backend then persisted it whole. So `CreatedAt`/`UpdatedAt` and the `TenantGuid` a
  wrapper injects — columns a ViewModel *cannot* map — were reset to their defaults on every update. The
  fix maps onto a **detached copy of the stored row**.
- **⚠ Both findings were WIDER, in different directions, and both widenings changed the work.** `SH-H034`
  was filed against the two single-item repositories; the two **bulk** ones use the same helper, so it was
  **4** update paths. `SH-H035` named 5 backends; measured, it is **96** `storeDelegate?.Invoke(...)` sites
  with **0** consuming the result.
- **⚠ And all nine live consumer repositories derive from a BULK base** — seven via
  `ElasticSearchRepository`, two via `AsyncDataBaseRepository` — i.e. the half the finding did **not**
  name. Fixing only the filed pair would have left **100% of the live consumers broken** while closing the
  ticket. § TASK-215 is usually argued from consistency; here it was the difference between fixing the
  defect and fixing nothing. **Widen on the root cause, then measure which half the consumers use.**
- **⚠ The inherited reach number was wrong, and re-measuring at the close gate inverted it.** The task
  recorded *"0 consumer `.cs` references to `AbstractViewModelRepository`"* — true, and irrelevant, because
  consumers never name the base. Re-measured: **9 `override void MapToModel`** across **2** repos, so
  **live, not latent**. § TASK-283's *grep for the subscription, not the identifier*, as *grep for the
  override, not the base name*. One of them, `ProductRepository.cs:25`, even documents the false
  assumption: *"Guid, CreatedAt, UpdatedAt are handled by base"*. Nothing handled them.
- **⚠ THE REVIEW GATE REWROTE THIS FIX, and that is the session's main lesson.** The version that was
  36/36 green and read cleanly had three real defects that `/code-review` and a security pass found
  between them. None was visible from the tests.
- **⚠ The worst of the three was a rule already in this file, six days old.** The merge mutated the object
  the store handed back — SH-H016's mechanism, which [[TASK-313]] wrote up as *hand the inner store a
  detached copy*. So the update reached store state **before** and **independently of** the write: a failed
  write left it applied, and on JSON/XML the next unrelated write would flush it to disk. **And my own
  probe store detached, which is exactly what hid it** — the test double was kinder than every real
  backend, so the suite could not see it. Proving the fix needed a *live-reference* store.
- **⚠ Enabling a dormant optimisation is a behaviour change, and it was backed out.** Making the inert hash
  skip real looked like the point of `SH-H035`. `AuditStoreWrapper`, `TimestampStoreWrapper` and
  `EventSourcingStoreWrapper` all sit **inside** `Store.Update` in `StoreWrapperBuilder`'s **recommended**
  chain, so a suppressed write silently drops the audit stamp, the `UpdatedAt` bump and the domain event —
  and `VersionedStoreWrapper`'s optimistic check stops running. § TASK-287: a fix must not smuggle in a
  behaviour change. The write stays unconditional; the decision is [[TASK-453]].
- **The skip's two silent-loss paths survive as GUARD TESTS rather than as behaviour.** While it existed it
  had a stale hash oracle (a caller restoring a value someone else changed matched the old hash and their
  write was dropped) and a hash refreshed before the write (so a retry after a failure was a no-op). Both
  are now tests that red on the naive re-enable, written onto TASK-453. **Building a thing and taking it
  out can still leave the measurement behind.**
- **Hoisting a delegate changes WHEN it sees things.** `CreateCore` assigns `data.Guid ??= …` *before*
  invoking the store delegate, so a transform that ran there could stamp child rows with the new key.
  Moving it out would have taken that away silently — so the key is pre-assigned first, which every store
  honours because every one uses `??=` (checked, not assumed).
- **Cost recorded rather than hidden, and the cheap alternative refused for a measured reason.** The merge
  is **one extra read per updated entity, including on the bulk path**. A single bulk read keyed on a Guid
  `Contains` would be one round trip and is the translation landmine § TASK-218/137 record across these
  eight backends, so `Read(Guid)` — no translator involved — was chosen deliberately. **Contract change
  stated where a consumer meets it:** `MapToModel` may now receive a **populated** target, so it must
  assign rather than accumulate; checked against all nine consumer implementations, 0 of 9 accumulate.
  ⚠ Spawned [[TASK-451]], [[TASK-452]], [[TASK-453]], [[TASK-454]].

### Localized writes destroyed the default-culture text, and localized deletes hit the wrong rows (2026-09-17)

TASK-313, the first of [[STORY-051]]'s seven remaining high triage tasks and the top of its blast-radius
ranking: `SH-H015`/`SH-H017` claim silent corruption of stored text and `SH-H018` a destructive statement
selecting a different set of rows than its own read. **3 CONFIRMED, 1 CONFIRMED-NARROWER, 0 refuted.**
**112/112 green** (79 pre-existing + 33 new), **four disjoint mutations** — A 7, B 9, C 5, D 6 of 112. The
standing rule is in § Conventions. Eight things worth carrying:

- **All four are one root cause seen from four sides:** the decorators treat an entity's own column and
  its translation as the same slot, and they resolve a filter for reads but not for writes. Fixing the
  file a finding happened to name would have left three copies live, which is why the rule now lives once
  in `Decorators/LocalizedEntityFields.cs` and all four wrappers call it.
- **⚠ The obvious fix wrote through, and only a test caught it.** Preserving the base column by swapping
  the caller's values and restoring them in a `finally` fails on exactly the stores `SH-H016` is about —
  they keep the reference, so the restore lands in the store. 4 tests red. The fix hands over a detached
  copy instead.
- **⚠ All 79 pre-existing tests passed against the unfixed code and still pass now.** Nothing in a
  harvest-grade suite could see any of the four defects — the reason they survived. A green suite said
  nothing.
- **`SH-H015` narrowed on measurement:** the corruption holds for `Update`, where a stored default-culture
  value is destroyed; `Create` has no stored row, so both the column and the translation take the caller's
  text, which is a fallback and not a defect. Deliberately unchanged, and said so rather than fixed from
  symmetry.
- **Mutation B reds an `SH-H015` test, and that is the finding not the leak.** `SH-H016` *defeats*
  `SH-H015`'s fix on a live-instance store: preserving the base column works by reading the stored value
  back, and a corrupting read leaves nothing correct to read. The coupling is recorded on the finding.
- **The crossed-row fixture is what makes `SH-H018` provable** — one row whose Slovak *translation* is
  `Stolicka`, one with `Stolicka` in its *base* column — so a predicate naming it under `sk` has a right
  answer and a wrong one, and the delete is asserted to agree with the equivalent read.
- **Both twins are covered, because they are different code.** The async bulk wrapper implements
  `UpdateAsync(filter, action)` and `DeleteAsync(filter)` by reading and looping where the sync one hands
  a callback to the inner store (§ TASK-245: the twin you patched may not be the one anything calls).
- **⚠ Consumer reach re-measured: 0 `.cs` files across all 16 consumer repos**, so nothing observable
  changes for a consumer today — a reason not to overstate urgency, and per § TASK-219/256 not a reason to
  discount the fix.

### `Destroy()` read as disposal on the framework's central store interface (2026-09-17)

TASK-321 / `SH-H046`, picked because it was the **only non-latent** item left in the high tier — every
other one measures 0 consumer references, while `IStore` is the interface every consumer reads.
`Destroy()` / `DestroyAsync()` were documented as *"destroys the store and releases all resources"*,
while all 17 implementations permanently delete data and `RavenDBStore` drops the **entire database**
(`hardDelete: true`). **133/133 green** across four suites, 7 new, **four mutations**. Documentation
only — no implementation changed. Seven things worth carrying:

- **⚠ The finding's supporting claim was FALSE, and correcting it made the defect worse, not smaller.**
  It said *"no store implements IDisposable, so a consumer looking for cleanup finds only Destroy()"*.
  Measured: `RavenDBStore` and `InfluxDBStore` declare it and the SQL stores reference it, releasing
  exactly the resources the doc claimed. So the doc did not merely mislead — it **described what an
  existing, correct member already does**, giving a reader no reason to look for `Dispose()`. Disposal
  is genuinely absent from the *contract*, which is why the fix belongs there and on no implementation.
  **Re-verify a finding's supporting claims, not only its headline**; this one changed the framing.
- **⚠ Widened by the SPEC REGEN, not by reading code.** Grepping the spec tree for the old phrase found
  a second area documenting the same disagreement, which traced to `IBaseRepository.Destroy()` carrying
  the **identical sentence** — and every repository family forwards to the store, so a SQL repository's
  `Destroy()` drops the entity's table. Fixed together per § TASK-215: a warning on one contract beside
  a reassurance on the other, for one operation, is the half-fix that rule names. **Step 7 is a place
  findings are discovered, not just a place specs are updated.**
- **A doc fix with no test is a doc fix somebody reverts while tidying.** The tests pin the contract
  shape (no disposal member, so the redirect stays honest) and the doc text itself. Restoring the old
  summary reds 2; deleting the paragraph that names the alternatives reds 1 — § SH-H037's *a guard that
  only says no gets reached around*, as an assertion.
- **⚠ The doors were verified before being named** (§ TASK-263). `Delete(filter)` is on
  `IBulkDeleteStore<T>`, but `DeleteAll()` is a **base-class** member and on no interface — so the doc
  says that rather than implying otherwise, and a test asserts both against the types.
- **⚠ A mutation showed two of my own tests were duplicates.** Making InMemory's `Destroy` a no-op red
  2 tests and **neither was mine** — the pre-existing behaviour tests carry that weight. My versions
  were deleted and the class remarks point at them. *A mutation tells you who owns a guarantee, not
  only whether one exists.*
- **⚠ Third scan this session to match its own explanation, and the fix had to be right twice.** The
  new remarks quote the old wording to record it, so a flat `NotContain` fails; a line-based filter
  then passed on the store interfaces **by luck** (each quotation on one line) and failed on the
  repository contract, whose async remark wraps across two. Both files now share one
  `WithoutQuotations` helper — two tests checking one thing two different ways is the shape this file
  keeps recording, and it does not stop being that because they are tests.
- **Rejected, with reasons recorded rather than left implicit:** renaming to `DestroyAll` (§ Conventions'
  naming rule is about a short name one keystroke from a safe one; `Destroy` is already alarming and it
  was the sentence underneath that disarmed it — and a rename breaks 17 implementations, 7
  `JobQueueSchema.DropAsync` helpers and every wrapper), and adding `IDisposable` to `IStore` (a design
  decision about a central interface, not something to slip into a documentation fix).

### The Cosmos migrator escaped with the wrong dialect, and three parts of the fix had no test (2026-09-16)

TASK-450, spawned by [[TASK-447]]'s own criterion and closing that thread. `FormatSqlValue` escaped a
string by **doubling the quote** — the SQL-standard rule, which Cosmos NoSQL does not use — so the
framework held two mutually incompatible escapers for one dialect. **23/23 green** (14 → 23), **four
mutations**. Six things worth carrying:

- **Two escapers for one dialect means at most one is right, and here neither was.** Measured offline:
  `O'Brien` → `'O''Brien'` is two adjacent literals (a syntax error), and `a\' OR 1=1 --` →
  `'a\'' OR 1=1 --'` closes the literal early because **a backslash in the value was never escaped at
  all**. So the doubling is a *correctness* defect and the unescaped backslash is the *injection* —
  the same class as TASK-447, reached from the opposite direction.
- **⚠ Three parts of the fix first measured ZERO on their mutation, and that is the session's real
  finding.** I had fixed `Bind`, its three call sites, and the schema builder — and nothing tested any
  of them, because all of them sit in methods that touch a live Cosmos container before building a
  statement. Without the mutations the change would have shipped a statement full of `@pN`
  placeholders with nothing bound to them. **A fix you cannot mutate into failure is a fix you have
  not tested**, however obviously correct it reads.
- **Reach for what the shape actually permits.** `Bind` is `internal static`, so it got a direct test;
  the call sites and the schema builder got **source scans** — weaker than a behavioural test, and the
  honest alternative to none. Each scan records in its remarks what its mutation measured, so nobody
  later mistakes it for belt-and-braces.
- **Values and identifiers are treated oppositely, deliberately.** A value is parameterised (a bound
  value has no grammar to break out of); a field **name** cannot be a parameter, so escaping is the
  only containment — and CR-M104's rule became one shared producer, because
  `CosmosDBSchemaBuilder.RenameField` had been interpolating a caller's field name with **no escaping
  whatsoever** while its sibling in the same project escaped. One rule, two behaviours, two files that
  ship together.
- **⚠ A test was the defect's alibi.** `ParseFilterToSql_EscapesSingleQuotes` asserted `'O''Brien'`,
  which made a wrong escaping scheme look like a considered decision. § TASK-284's rule, third sighting
  this week — and the replacement asserts the payload is **absent** from the clause rather than
  correctly escaped, a claim that cannot rot as the grammar changes.
- **⚠ The P1 rating was understated and is left as filed.** It was chosen because the parser's
  behaviour was unmeasured, and the task's first criterion existed to settle it — which it did, as
  injection, i.e. TASK-447's P0 class. The frontmatter is not rewritten after the fact: a priority is a
  record of what was known when it was filed, and the correction belongs in the outcome.

### Seeded noise was not reproducible across .NET versions, and 14 tests could not see it (2026-09-16)

TASK-449, filed by hand rather than harvested and picked by explicit instruction — it sat in
`tasks/_loose/` with an empty `findings:` and no `review-intake` parent, so nothing ranked it.
`PerlinNoise` and `SimplexNoise` both take a seed and both shuffled their permutation table with
`new System.Random(seed)`, whose algorithm changed in .NET 6 and carries no cross-version stability
guarantee. **130/130 green**, 9 new, **three mutations**. Six things worth carrying:

- **Seedable and reproducible are different properties, and the call site cannot tell them apart.** A
  constructor taking a seed *looks* like it promises a stable sequence. That is the whole reason this
  was invisible, and it is why the fix ships a **per-type reproducibility table** in
  `Birko.Random/CLAUDE.md` rather than only a code change — a consumer picking an RNG for replay or
  procedural generation should not have to read the source to find out.
- **⚠ The area had 14 tests and every one of them passes against the defect.** They assert ranges,
  continuity, zero-at-integer-coordinates, and that two instances agree **within one process** — where
  `System.Random` is perfectly deterministic. The thing never asserted was a **value**, which is the
  only assertion that can fail when the runtime changes underneath. Measured: reverting the fix reds
  8 of 130 and leaves all 14 green. § TASK-284's rule, in its purest form yet.
- **Extract to one producer when the defect IS the duplication.** The task proposed "roughly two lines
  per file" and that is correct and smaller. `NoisePermutation.Build` was extracted instead because
  two files were independently deciding where their randomness came from — which is how they came to
  make the same wrong choice. A shared producer makes the next such edit impossible to get half-right.
- **A golden vector must pin the CONSTRUCTION, not just determinism.** Mutation B keeps SplitMix and
  changes only the seed widening (`(ulong)seed` → `(ulong)(uint)seed`); it reds 2 of 130. Without a
  mutation at that granularity, "the vectors pass" would only prove *some* deterministic generator is
  in use.
- **⚠ A source scan over files that explain the defect must strip comments, or it matches its own
  explanation.** All three noise sources discuss `System.Random` in their doc comments — deliberately,
  so the reason stays with the code. Mutation C removes the stripping and the guard fails on itself.
  § TASK-276's self-matching trap, arriving from the other side: there the fix was to *assemble* the
  forbidden literal, here it is to scan code rather than prose.
- **⚠ The tracking convention contradicts itself, and this task is both halves of the evidence.**
  § *Task tracking* prescribes filing single-sub-project work in the sub-repo; **1 of 178**
  sub-projects has a `tasks/` folder, and `/tasks pick`, the dashboard and [[fix-next]] all run from
  the aggregator. So the prescribed location is *filed and scheduled by nothing* — and the
  aggregator's own `_loose/` is only marginally better, since a task there with no `findings:` and no
  `review-intake` parent is outside the pool too. Recorded rather than given an id: the decision
  belongs to whoever owns the convention, not to a defect fix in `Birko.Random`.

### A Cosmos view filter's string value could break out of its own quotes (2026-09-16)

TASK-447, spawned by [[TASK-322]]'s close gate the same day and worked next because it is the same end
state reached with the caller holding the predicate. `CosmosFilterTranslator` rendered a string
operand as a quoted literal escaping only `'`; Cosmos NoSQL escapes with **backslash**, so a backslash
in the input consumed the escape the code had just added. **35/35 green**, 11 new + 6 inverted,
**three mutations**. Seven things worth carrying:

- **The containment is the API shape, not a better escaper.** Values are now **bound as parameters**.
  Both execution sites already built a `QueryDefinition` and simply never called `WithParameter`, so
  the mechanism was present and unused — and § TASK-308/SH-H028's rule is *prefer removing the grammar
  to escaping it*: escaping is a blacklist against a documented set that can grow, a bound value has
  no grammar at all. **Check for the unused mechanism before designing an escaper.**
- **A rendering layer that needed fixing twice is a rendering layer to delete.** CR-M086 corrected the
  hand-written literal formatter for enums and for `DateTime`; the `_ => value.ToString()` fallback
  still emitted **unquoted** text for anything unlisted. All of it is gone. The SDK serializes a
  parameter with the same serializer that wrote the document, so the stored form and the compared form
  match by construction instead of by a guess about System.Text.Json's defaults.
- **⚠ Say what the change gives up.** No offline test can see the wire format of a bound `DateTime` or
  `Guid` any more, and the inverted test file says so in place rather than quietly dropping the
  coverage. That is the trade, not an oversight.
- **⚠ A mutation that failed NOTHING was the most useful result of the session.** Deleting the
  enum-to-numeric conversion left all 35 green. Measured why: C# builds `v.State == Status.Published`
  as `Convert(v.State, Int32) == Convert(2, Int32)`, so the operand evaluates to a boxed **`Int32`**
  and never reaches the `is Enum` branch — which is reachable only through an operand whose static
  type is `object`. The test was aimed at the wrong shape. Retargeted, it reds; the ordinary shape is
  kept as a second test recording that the **compiler**, not this code, satisfies CR-M086 there.
- **Assert the absence of caller text, not the correctness of an escape.** The payload theory asserts
  that the input appears **nowhere** in the query text and **verbatim** in the parameter. That claim
  cannot rot as the grammar changes, where a list of correctly-escaped payloads can.
- **⚠ Measured versus documented, kept apart.** The *rendering* is measured. That the old rendering
  **parses** as an injection is read off Cosmos NoSQL's literal grammar and is **not** executed —
  that needs a live account. Both the task and the test file say which is which.
- **⚠ Spawned [[TASK-450]] (P1), and it is wrong a DIFFERENT way.** The task's own criterion sent me to
  check the Cosmos migration emitters:
  `Birko.Data.Migrations.CosmosDB`'s `FormatSqlValue` escapes with **SQL-standard `''` doubling**,
  which Cosmos does not use at all — so the framework held two mutually incompatible escapers for one
  dialect and at most one could be right (§ TASK-274). Filed P1 rather than P0 **because its effect on
  the parser is not yet measured**, and calling it a leak would be a claim ahead of its evidence.
  § TASK-253 had already flagged that file as the one site left out of the `EscapeLiteral` convergence.

### A Cosmos view filter that could not be translated returned every tenant's rows (2026-09-16)

TASK-322 / `SH-H055`, the last backend still holding the fail-open CR-H047 closed on ElasticSearch and
TASK-268 generalised. `CosmosFilterTranslator.Translate` wrapped its whole recursion in
`catch { return string.Empty; }`, and both consumers append the WHERE only when the clause is
non-empty -- so an untranslatable predicate produced **no WHERE at all** and the aggregate ran over
every document. **15/15 green**, 9 new, **four disjoint mutations**, plus a measurement taken before
the fix existed. Seven things worth carrying:

- **The empty string was overloaded THREE ways, and only one of them was the defect.** "No filter
  supplied", "translation failed", and "always true" all rendered as `""`. That is why *stop
  swallowing* is the wrong fix on its own, and it is § TASK-308's *the empty state is OVERLOADED*
  arriving in a second translator -- there the ambiguity was between "constrains nothing" and "nothing
  was parsed", here it has a third member.
- **⚠ `x => true` WORKED, by accident, and the test that proves it passed against the unfixed code.**
  A `ConstantExpression` hit the unsupported-node throw, was swallowed, and the empty clause happened
  to mean the right thing. A naive throw turns a working call into an exception -- `PredicateScope`'s
  *a false refusal breaks working code*. **Write the test before the fix and the accident is visible;
  write it after and it looks like a requirement you invented.**
- **⚠ And its mirror was a silent wrong answer the finding never mentions.** `x => false` took the
  identical path and therefore matched **every** document instead of none. Same root cause, opposite
  direction, found only by asking what else reaches the swallowed branch.
- **The always-true door is at the ENTRY POINT; a nested constant renders as a literal.** § TASK-137:
  an empty fragment returned for a nested term is silently joined between its neighbours' separators
  into `(c.Amount > 0 AND )`. So the recursion always emits `true`/`false`, and only the top-level
  body short-circuits -- accepting a single `ConstantExpression` node, never a shape that merely
  reduces to true.
- **Refuse with the type the sibling already throws.** `TranslateValue` let
  `InvalidOperationException` escape from `Compile()` for a column-vs-column operand; it now reports
  `NotSupportedException`, so one catch selects "this filter cannot be translated" on Cosmos and
  ElasticSearch alike. Cancellation is excluded from that rewrap (§ TASK-291), and the message carries
  the node's **type** and never the rendered expression (§ TASK-308).
- **⚠ "This needs a live Cosmos" was false, and a harness for it already existed.** `BuildAggregateSql`
  is reached by reflection off a store built from an unreachable connection string -- the shape
  `CosmosViewAggregateSqlTests` had been using since CR-H044. Only *executing* a query needs a server.
  Third instance of § TASK-309's rule; before accepting that a sink is untestable offline, try
  rendering it.
- **⚠ Spawned [[TASK-447]] (P0):** `TranslateValue` escapes `'` and **not** the backslash, so a filter
  value breaks out of its own literal -- measured, `a\' OR 1=1 --` renders as `'a\\' OR 1=1 --'` and
  the remainder is parsed as SQL. Different root cause, so it is its own task rather than folded in.
  The existing `CosmosViewTranslateValueTests` asserts `O'Brien` -- the single input where escaping
  only the quote is correct, which is how the sink stayed invisible in a tested area.

### One workflow could relabel and overwrite another's instance, on all seven backends (2026-09-16)

TASK-315 / `SH-H056` + `SH-H057`, the first of [[STORY-051]]'s ten remaining P1 per-area tasks. **Both
confirmed, one wider than filed.** Every `IWorkflowInstanceStore` backend keeps all workflows and all
`TData` types in one table/collection (SQL: `__WorkflowInstances`), and `SaveAsync` upserted by
`InstanceId` alone before assigning the caller's `workflowName` over whatever was persisted. **116/116
green** across eight suites, 12 new, **six disjoint mutations**. The standing rules are in § Conventions.
Eight things worth carrying:

- **The two findings are one chain and the silent half is the read.** `FindByState`/`FindByStatus`
  filtered on state alone, so a store typed for `OrderData` got `InvoiceApproval` rows — and
  `ToInstance<TData>()` deserializes a foreign payload into a **fully-defaulted** `TData` rather than
  throwing, because System.Text.Json ignores unknown members. A consumer enumerating that result and
  saving then relabelled the foreign row and overwrote its payload, state and history, with nothing
  raised anywhere.
- **⚠ The filed location named SQL; the defect was seven backends, and CosmosDB was wrong the OTHER
  way.** Cosmos scoped those two queries to a constructor `_workflowName` while its own
  `FindByWorkflowNameAsync` filtered on the *parameter* — so it disagreed with its six siblings **and
  with itself** (§ TASK-274). Converging the six onto Cosmos without noticing that would have adopted a
  shape its own third query contradicts.
- **The contract could not be settled by the caller, because there is none.** `IWorkflowInstanceStore`
  has **0** callers in the framework (`WorkflowEngine` never touches it), 0 construction sites in any
  test, and 0 `.cs` files across all 16 consumer repos; the seven backend test projects cover only their
  *model*. So § TASK-309's rule had to be applied to the *interface's own shape* instead: `SaveAsync` and
  `FindByWorkflowNameAsync` both take the name per call, and `TData` is fixed on the store — which makes
  the unscoped `WorkflowInstance<TData>` return type unsound however a backend implements it.
- **That made SH-H056 an API decision, not an implementation one, so it was asked rather than guessed.**
  `FindByStateAsync`/`FindByStatusAsync` now take `string workflowName` first, matching their two
  siblings; Cosmos's field and both constructor parameters are deleted. Affordable because the break is
  **loud** (`CS7036`) and was priced first at 0 call sites — § TASK-260's *removing or retyping a
  parameter is safe in a way that inserting one is not*.
- **SH-H057 is refused at one producer, not fixed seven times.**
  `Birko.Workflow.Core.WorkflowInstanceOwnership.RequireSameWorkflow` throws
  `WorkflowInstanceOwnershipException : WorkflowException`, so one `catch` still selects the family, and
  the message **names both doors this caller has** — delete and re-save, or relabel through the `Store`
  property every backend exposes.
- **This SUPERSEDES CR-L404 rather than reverting it.** That finding made the update branch refresh
  `WorkflowName` so a re-save under a different name "isn't silently kept stale"; with a mismatch refused,
  a stale name is unreachable, so the assignment is provably dead and is deleted — and the scan below
  fails if anyone puts it back.
- **⚠ Six of seven backends cannot be exercised offline, so a source scan is the only cover they have —
  and the mutations prove it.** Un-wiring MongoDB's guard reds **1 of 68**, the scan, while **MongoDB's
  own suite stays green at 8/8**; same for unscoping RavenDB's `FindByStatus`. Reflection cannot see "does
  this method call that helper", so the scan reads the seven sources and also pins the count at 7, so a
  new backend cannot arrive unguarded.
- **⚠ The interface change broke exactly one thing — this task's own pin test — and it was INVERTED, not
  deleted.** Written hours earlier to assert the unfixed read so a later change could not land silently,
  it now asserts the scope (§ TASK-211). Worth recording that a test written to pin a deliberate gap is
  the right artefact even when the gap closes the same session.

### Bidirectional sync — the DEFAULT direction dropped every new item, then deleted it (2026-09-16)

TASK-309, the last open P0 in [[STORY-051]] and the first of its 15 per-area triage tasks to be drained since
TASK-308. All seven high `data-sync` findings: **6 confirmed, 1 confirmed-narrower, 0 refuted outright.**
**220/220 green across 10 suites** (Sync 64, Sync.Tenant 37, Sync.Sql 7, Sync.Json 7, Sync.Xml 7,
Sync.MongoDb 5, Sync.ElasticSearch 11, Sync.RavenDB 9, Sync.CosmosDB 14, Aggregates 59), 38 new, **seven
disjoint mutations**. The standing rules are in § Conventions. Nine things worth carrying:

- **Four findings were one chain, and the chain fired on the shipped default.** `SyncOptions.Direction`
  defaults to `Bidirectional`; `ProcessBatch`'s `Create` arm tested `Direction == Download` then
  `== Upload` **with no else**. So a brand-new entity was counted `Processed`, given a knowledge row, and
  written nowhere — and since the knowledge row was built from the **pre-action** items, it recorded the
  destination as *deleted*. Run two read that as a deletion and, under `RemoteWins`, **deleted the user's
  original row**. The conflict escape was itself a no-op, because every arm required both copies to exist
  and a conflict is only ever raised when one of them does not.
- **The fix dispatches on which side is MISSING, not on `Direction`** — the direction is already applied
  upstream by `DetermineSyncAction`, so a third arm would be a second place where direction is decided.
  Behaviour-preserving for Download and Upload: all 43 pre-existing tests stayed green.
- **The knowledge row now describes the state AFTER the action**, tracked as an `effectiveLocal` /
  `effectiveRemote` pair. Both sides are asserted — a create must not mark the destination deleted, and a
  delete must still mark the deleted side — or "never set the flag" passes as a fix and breaks delete
  propagation.
- **The one-sided conflict arms copy `DetermineSyncAction`'s own `LocalWins`/`RemoteWins` shortcuts**
  rather than inventing semantics, so a policy and its conflict path cannot disagree (§ TASK-274).
- **⚠ `SH-H009` cannot be reproduced from `SH-H011` alone, and the mutation proves it.** With the Create
  arm fixed but the hashes stale, run 1 really uploads, so run 2 sees both sides and takes the Update path.
  A consequence-finding needs its cause broken to fire — which is why the flags are asserted directly too.
- **⚠ The drop moved no counter at all** — not `Created`, not `Skipped`, only `Processed`. The finding did
  not note that, and it is why nothing ever noticed. Now pinned as
  `Created + Updated + Deleted + Skipped == TotalProcessed`.
- **⚠ Half of `SH-H013` was refuted, and fixing it as filed would have caused a regression.** The finding
  says Raven and Cosmos are both wrong "in opposite directions"; measured against the sole caller, Raven's
  conditional predicate is *correct* and its scope-wide delete is the sanctioned all-tenants path. Only
  Cosmos was wrong, and its `root["TenantId"] = null` matched nothing. See § Conventions.
- **⚠ Two areas had zero coverage of the thing that was broken.** The 50-test aggregates suite stayed green
  through the whole `SH-H014` fix — nothing asserted the old many-to-many expansion, which emitted
  `Delete` of the shared **Category** instead of the junction row, so a caller applying it destroyed a row
  every other product shares. And the Cosmos suite had no filter coverage because it believed the filter
  needed a live server.
- **⚠ Spawned [[TASK-445]] (P3):** `ConflictResolution.Merge` has no `case`, no write, no counter and no
  error, so a resolver asking for a merge is indistinguishable from a dropped item — the same silent-drop
  family, but implementing a merge is a feature, not a bug fix, so it is documented and specced as inert
  meanwhile rather than left to be believed fixed.

### The migration runner's own bookkeeping table could not be created on MySQL or SQL Server (2026-09-12)

TASK-332, reported by consumer Symbio after building its 109-table schema on live PostgreSQL 16, MySQL 8.4.11
and SQL Server 2022 CU26 for the first time — every previous Symbio schema had only ever been built on SQLite.
PostgreSQL passed completely. **MySQL and MSSql both failed inside Birko's own migration bookkeeping, before a
single model table was reached**, so the SQL migration runner worked on SQLite and PostgreSQL only. Verified
with `BIRKO_REQUIRE_LIVE` set against live PostgreSQL 16.15, MySQL 8.4.11, SQL Server 2022 CU26 (16.0.4275.2),
TimescaleDB 2.30.0 and on-disk SQLite: **210 tests, 0 failed, 0 skipped** across three suites, 24 new, four
disjoint mutations. The standing rules are in § Conventions. Eight things worth carrying:

- **Both defects were reproduced against live servers before a line was written**, verbatim: MySQL's
  `ERROR 1064 … near '"__Mig_Probe" ("Version" BIGINT PRIMARY KEY'` and SQL Server's `Msg 2738, A table can
  only have one timestamp column`. The proposed replacement statement was then run on all three servers
  **before** being coded, which is what made `DEFAULT CURRENT_TIMESTAMP` a measurement rather than a bet.
- **⚠ The two defects do not overlap, and measuring that changed the story.** SQL Server accepts `"` as an
  identifier delimiter under `QUOTED_IDENTIFIER ON` — which is `Microsoft.Data.SqlClient`'s default — so
  defect 1 never fires there and defect 2 never fires on MySQL. The same statement is `Msg 102` under
  `sqlcmd`'s default `OFF` and `Msg 2738` with `-I`; I nearly recorded the wrong conclusion from the first
  run. Fixing either defect alone leaves one server broken.
- **The fix deletes the guessing surface rather than defaulting it better.** `quoteOpen`/`quoteClose` had
  **0 call sites anywhere** while the runner constructed the store holding the connector; the connector is
  now required. Same for types: `ConvertType` is the one producer (§ TASK-269), so this table is typed
  exactly as an entity table and inherits every past and future per-provider fix.
- **⚠ The tempting unification is the catastrophic one, and a mutation proved it.** Making the migrations
  table's columns bare — matching the entity DDL — reds the PostgreSQL upgrade test (`42703` against a table
  an earlier release created) while **leaving the fresh-database test green**. That asymmetry is the whole
  reason the suite carries an upgrade test and not only a clean-server round-trip.
- **The duplication was load-bearing.** Reverting **only** the async `CREATE TABLE` reds exactly the two
  async tests on exactly the two broken dialects, sync green — a half-fix that a sync-only suite calls done.
- **The read-back is the assertion, not "the DDL did not throw".** `GetAppliedVersions` answers **empty** for
  an absent table rather than failing, so an empty set cannot distinguish "created" from "never created";
  only a recorded version coming back proves the table exists, accepts a write and can be queried.
- **⚠ Two process mistakes of mine, both worth recording.** I ran `git checkout` on a file carrying
  uncommitted work to undo a mutation and **destroyed the fix**, rebuilding it from a script — mutations are
  now reverted from an explicit backup copy. And I set `BIRKO_REQUIRE_LIVE` globally across a suite whose
  server was not running and read the resulting **35 TimescaleDB failures as signal**; all 35 were
  `SKIPPED: no live TimescaleDB` promoted to failures, exactly the trap § TASK-259 and § TASK-266 record.
  Standing a TimescaleDB container up instead gave 88/88 and a fifth dialect verified live.
- **⚠ Out of scope and deliberately not conflated:** three Symbio columns (`SpaceAttributes.Key`,
  `ProductAttributes.Key`, `InAppNotifications.Read`) are reserved words in MySQL and T-SQL, so their
  `CREATE TABLE` fails there. That is a model-naming decision in the consumer (their TASK-690), not a
  framework defect — the bare-column emission it collides with is deliberate — and "fixing" it by quoting
  entity columns everywhere would break PostgreSQL, the one server dialect that already worked.

### A SQL cache key did not identify its query, in two independent ways — and the obvious fix was measurably insufficient (2026-09-09)

TASK-310 / `SH-H004` + `SH-H005` + `SH-H007`, resumed after a mid-session SDK removal. **1,057 tests
green**, three disjoint mutations. Eight things worth carrying:

- **A key built from `filter.ToString()` is not a key for the query.** A closure-captured local renders
  as `value(<>c__DisplayClass…).field` — **measured, byte-identical for every value** — so
  `x => x.TenantGuid == tenant` produced **one cache key for every tenant** and the first tenant's rows
  were served to all the others. Inline literals *do* render distinctly, which is why every pre-existing
  test passed: all of them used literals. **A test suite built on constants cannot see a closure defect.**
- **⚠ The obvious fix was measured and found insufficient, which changed the design.** The framework
  already ships `ExpressionNormalizer`, whose funcletization folds a captured local to its value — so
  reusing it was the one-producer answer and looked complete. Measured first: it fixes **scalars**
  (`Guid`, `int`, `string` render distinctly) and **not collections** — `List<int>{1,2,3}` and `{9,9,9}`
  both render `value(System.Collections.Generic.List`1[System.Int32])`. So `ids.Contains(x.Id)`, the
  ordinary set-membership filter, would still have collided, and a fix stopping at normalisation would
  have closed the finding while leaving the same class of leak live. Hence **normalise, then check the
  rendering, then refuse** — and the refusal is the design, not a fallback.
- **Refusing to cache is the safe direction, and the trade is stated rather than hidden.** A miss costs
  a database round-trip and is always correct; a shared key costs one caller another caller's rows. So
  set-membership and object-valued filters are now **not cached at all**. Affordable only because reach
  was measured at 0 consumer `.cs` files — the same veto that has decided several of these.
- **The refuse-marker is what the runtime actually emits, not a type whitelist.** A whitelist of "safe"
  constant types must be complete to be safe and is silently wrong when it is not; `value(` is what
  `ConstantExpression` renders when a value does not describe itself, so the check is total by
  construction.
- **⚠ A key and its invalidation prefix must be scoped in the SAME change, and the asymmetry is the
  trap.** `SH-H005`'s missing database identity caused a leak on *reads* and **over**-invalidation on
  *writes* (one store's write removed the other database's entries). Over-invalidation is
  correctness-preserving — a spurious miss — so scoping only the prefix would have turned a harmless
  over-reach into entries **nothing ever removes**, which is worse than the leak. Both halves are
  asserted.
- **⚠ A hierarchy assumption in an earlier fix left the primary provider uncovered — spawned as a P0.**
  Reading `AsyncDataBaseBulkStore` for `SH-H007` showed it does **not** derive from
  `AbstractAsyncBulkStore`: it implements `IAsyncBulkStore<T>` directly and carries its own filter-based
  overloads (hence its own private `RequireFilter`). So [[TASK-215]]'s `RequireBoundedFilter` — wired
  into the abstract bulk stores — is **never applied in `Birko.Data.SQL` at all**. Measured with a
  throwaway probe rather than filed as a reading: `Update(x => !empty.Contains(x.Name), …)` gave
  `thrown=NONE | overwritten=3 of 3` on SQLite, a silent whole-table rewrite. [[TASK-329]].
  **When a fix is wired "into the base", check which bases the concrete types actually derive from.**
- **⚠ The planned fix for `SH-H007` was impossible, and the reason improved it.** The plan was to
  override the filter-update loop and read uncached — but that class's `RequireFilter` is **private**, so
  the guard could not be called, and copying the loop would have duplicated a rule with one producer.
  Instead the override delegates to the base and diverts only the *read*, through an `AsyncLocal`
  instance scope with save-and-restore (TASK-270's mechanism, chosen because a singleton store serving
  concurrent requests would race on a plain flag, and an exception must not strand it).
- **⚠ And the correctness pass found a defect in my own fix.** Normalization compiles subtrees, so I had
  put a possible throw on every cached read path where `filter?.ToString()` could not fail. Now
  fail-safe: any failure means "cannot be described", so the read proceeds uncached. Labelled
  **defensive, not witnessed** — `TryFold` already swallows the common case, so the catch is about a read
  never failing for a *caching* reason rather than about an observed throw.

### Two tenant-isolation findings, two different KINDS of fix — and the first attempt at the code one was wrong (2026-09-09)

TASK-311 / `SH-H049` + `SH-H053`, the second `/fix-next` pick. Both confirmed. Both end in cross-tenant
access, because the tenant wrappers **deliberately fail open** on `HasTenant == false` (CR-L229, pinned
by `TenantFailOpenTests`) — so any defect that loses the ambient tenant becomes an isolation breach.
**195 tests green** across four suites, three mutations. Eight things worth carrying:

- **`SH-H049` needed code; `SH-H053` could not have it, and the difference is worth understanding.**
  The middleware took `ITenantContext` from `builder.ApplicationServices` (the **root** provider), so a
  documented `AddTenantContextScoped()` registration was never observed — fixed by taking the context
  **per request**. The event bridge's mis-wiring, by contrast, is **undetectable where it does harm**: a
  bridge reading the wrong instance and a *genuine system event* both arrive with `TenantGuid == null`,
  and narrowing that branch would break cross-tenant system events, which are its documented purpose. So
  its fix is corrected documentation plus pinned mechanism, and the residual design question was
  **escalated** ([[TASK-328]]) rather than guessed at.
- **⚠ The first version of the code fix was WRONG, and only a test written at the correctness pass caught
  it.** It passed `null` to `UseMiddleware<TenantMiddleware>(null, options)`. That helper binds args to
  constructor parameters through `ActivatorUtilities`, which **cannot match a null** — so the optional
  `ITenantContext` would have been left unmatched and filled from the **root provider**, silently
  reinstating the exact capture being removed. **Every existing test passed with it**, because
  `UseTenantMiddleware` has 0 production callers and nothing covered it. Rewired to resolve from
  `ctx.RequestServices`, which is unambiguous. **A fix in a code path nothing tests is a guess** — and
  the reason to write the missing test is not coverage for its own sake, it is that the fix was wrong.
- **⚠ Instance-level `AsyncLocal` is the load-bearing detail neither finding stated.**
  `TenantContext` holds its state in `private readonly AsyncLocal<…>` **instance** fields, not static
  ones — so two instances are two ambient scopes and share nothing. Had those been static, both findings
  would have been false positives. **When a defect turns on "a different instance", check whether the
  state is actually per-instance before believing it.**
- **A contested downgrade was resolved by reading what each side was about, not by overriding one.**
  Three closed tasks recorded `SH-H049` as *"downgraded, not tasked"*; [[TASK-118]] described a live
  fail-open through it. Both are right: TASK-118's concern was the **guard**, and TASK-118 itself routed
  the guard around the registration (it reads `HttpContext.Items`). What remained was the
  middleware/store mismatch. **A contradiction between two records is often two different subjects.**
- **The failure mode split by environment, and the dangerous half is the invisible one.** Development
  throws (`ValidateScopes` refuses a scoped resolve from root); **Production is silent**. A defect that
  announces itself in dev and hides in prod is worse than one that always throws.
- **⚠ A registration-time guard was designed, then measured inert.** Inspecting the `IServiceCollection`
  for an `ITenantContext` descriptor looked like the clean way to refuse a broken event-bridge wiring —
  but the sole consumer calls `AddEventTenantScope()` at `Program.cs:101` and `AddBirkoSecurity` at
  `:104`, so at guard time no descriptor exists. **Check the call ORDER of the real consumer before
  building a registration-time check**; a guard that cannot fire on the only real pattern is worse than
  none.
- **The literal revert of a signature change is a compiler error, not a red test — say which.** Removing
  the `InvokeAsync` parameter breaks the build at 5 call sites, so no test can run to fail. That is a
  *stronger* signal (it cannot be ignored) and it is *not* a split, so two compiler-tolerated mutations
  were run to produce one: 2 of 76 and 1 of 76, each naming its target.
- **⚠ And the first draft of the regression test committed the very sin under repair** — a leftover
  `provider.GetRequiredService<ITenantContext>()` resolving a scoped service from the root provider,
  failing with *"Cannot resolve scoped service from root provider"*. Recorded because it is a fair
  illustration of how easy the mistake is: the defect being fixed is a one-line habit.

### An authentication service allowed everything when it was switched ON but misconfigured (2026-09-08)

TASK-312 / `SH-H040`, the first `/fix-next` pick from the newly-drainable high pool. A Birko service with
`Enabled: true` and no usable token accepted **every** caller, including one presenting no token at all.
`ValidateToken` gated on `IsAuthenticationEnabled()`, which answers *"enabled **and** configured"* — so a
config section with the flag set and the token list empty collapsed into the same allow-all branch as a
deliberate `Enabled = false`. Four transports share that engine. **128 tests green** across five suites,
two disjoint mutations. Eight things worth carrying:

- **One question standing in for two is the whole defect, and the fix is to separate them at one
  producer.** `IsAuthenticationDisabled` (`!_config.Enabled`) is now the only state a gate may treat as
  allow-all; `IsMisconfigured` reports the other; both gates read the producer, so a third caller cannot
  reintroduce the split. `IsAuthenticationEnabled()` is deliberately **unchanged** — five transport
  wrappers expose it and a test pins all three of its answers, so moving the *gate* was smaller and safer
  than redefining the method.
- **The correct behaviour was already written and unreachable.** `ValidateToken` ended with
  `LogWarning("Authentication enabled but no tokens or bindings configured"); return false;` — dead,
  because the gate in front returned `true` for exactly that state. § TASK-247's *a fallback nobody can
  reach* shape, guarding an auth decision. **Most of this fix was making existing code reachable**, which
  is also the strongest evidence of what the author intended.
- **⚠ The filed trigger was the SAFE case, and reproducing it as written would have closed the finding as
  a false positive.** It blamed *"a renamed `${VAR}`"*. Measured: an absent variable makes
  `GetEnvironmentVariable` return `null`, so `ExpandEnvironmentVariable`'s `?? value` **keeps the literal**
  `"${VAR}"` — non-blank, so retained, authentication stays on, every real token refused. That fails
  **closed**. The real triggers are nothing-configured-at-all and a variable that **exists and is blank**
  (`""`, where the `??` never fires). The safe case is now pinned as a *contrast* test so nobody "fixes"
  the fallback into a fail-open. **Re-verify a finding's trigger, not just its mechanism.**
- **⚠ There were TWO independent gates and only one was filed.** `SseAuthenticationService`
  (`:94`) gated on `IsAuthenticationEnabled()` directly and returned `Success` *before extracting a
  token*. Proven independent by mutation: with the engine already fixed, reverting the SSE gate alone
  still reds 2 of 24. § TASK-215's *guard the whole verb family or none of it* — shipping half a fix for
  an auth bypass is not a partial improvement.
- **⚠ The security pass found a defect in my own fix, which is why it is conditional-but-not-optional.**
  The misconfiguration arm first returned `Fail("Authentication is misconfigured on the server")` to an
  **anonymous** caller — a distinguishable signal that the server is in a known-broken state. It now
  returns the same message as an invalid token, with the operator detail in the `LogError` only, and a
  test pins the two refusals as indistinguishable. **A gate axis that only ever passes is not being run.**
- **Rejected: throwing from the constructor.** Loudest, and § SH-H037 would permit it since the opt-out
  exists and is checked first. Rejected on blast radius per § TASK-256's inversion of the same instinct:
  a consumer in this state is running an *open* endpoint, and a throw converts their running service into
  a start-up failure. Refusing at validate time closes the hole; `IsMisconfigured` + `LogError` give the
  operator the signal. **A service that silently refuses everything is as hard to diagnose as one that
  silently allows everything**, which is why the report is part of the fix rather than a nicety.
- **The defect lived in the GAP between two passing tests.** `IsAuthenticationEnabled_False_WhenEnabledButNoTokens`
  and `ValidateToken_AllowsAny_WhenAuthenticationDisabled` both existed and both were honest; nobody had
  ever asserted `ValidateToken` with `Enabled = true` **and** no tokens. So a green suite said nothing —
  a different failure from § TASK-284/279's *a test was asserting the defect*, and worth telling apart:
  **there was nothing to correct, only something absent.**
- **⚠ And it measured that NO spec area covers any of the four transports.** `docs/specs/.map.yml` has
  **0** globs reaching `Birko.Communication.SSE`, `.WebSocket`, `.REST.Server` or `.SOAP`, and **0** specs
  mention `SseAuthenticationService` — so the SSE half of an auth-bypass fix produced **no spec diff**,
  and the evidence this epic normally relies on did not exist for it. The spec layer describes the shared
  engine and none of the four network boundaries that call it. Appended as a **third instance** to
  [[TASK-142]] rather than spawned as a duplicate, and it upgrades that task from tidiness to a
  security-coverage gap.

### 16 findings recovered from a lost harvest pass: 11 folded, 5 duplicates, and the one proposed high downgraded (2026-09-08)

TASK-195, closing [[STORY-055]]. The 2026-07-30 harvest aggregated by `severity`, a field its **first**
pass's output schema did not have, so three areas — `core-model-contracts` (4),
`store-lazy-initialization` (6), `unit-of-work-and-transactions` (6) — matched no section of the findings
doc and survived only as a count in a note that read *"complete"*. Recovered verbatim from the workflow
journal on 2026-07-31; rated, ID'd, folded and routed today. Documentation and routing only — **no
production code changed.** Totals **865 → 876** (`57 · 428 · 391`). Seven things worth carrying:

- **Re-verify a recovered finding before rating it, not after.** All 16 were checked against current source
  first, because 39 days and ~40 tasks had passed and several neighbouring mechanisms had moved
  ([[TASK-270]] reworked re-entrancy in `DataBase`, [[TASK-288]] added `CanTrustRememberedInitialization`
  to the store bases). **All 16 still described the shipped code** — but that was a measurement, not an
  assumption, and it is the thing that makes the ratings worth anything.
- **⚠ Five were duplicates, not the two that were known — and the three extra were found by the check the
  task file insisted on.** `SLI-1` is `SH-M307` (same file, **same line 48**, same mechanism) and `SLI-2`
  *and* `SLI-3` are both `SH-M316`, whose title — *"InitCore re-entrancy fails two different ways in the
  sync and async bases"* — deliberately spans both halves, so two recovered ids collapse into one existing
  finding. The reason the duplication exists is structural: `store-crud-contract`'s globs include the same
  `AbstractStore.cs` / `AbstractAsyncStore.cs` files, so two agents reported the same defects.
  **When folding a recovered or re-run sweep in, search the existing doc for every FILE the new findings
  name** — that search returned 15 existing ids for the two store bases and **0** for
  `AbstractModel.cs` / `AbstractLogModel.cs` / `SqlUnitOfWork.cs` / `SqlTransactionContext.cs` /
  `ElasticSearchUnitOfWork.cs`, which is what makes "the other 10 are new" a claim rather than a hope.
- **⚠ The one proposed high does not clear the bar, and two measurements settled it.** `UOW-1` (an ES
  commit failure leaving the buffer queued) was filed as high on the strength of *"a retry double-applies
  succeeded items"*. Measured: every operation `BulkOperationContext` can buffer is **idempotent** —
  `Index` sends the full document, `Delete` is by id, `Update` uses `Doc(partialDocument)` rather than a
  script, and there is no `create` — so a re-send overwrites with the same value. And the class's own doc
  comment already says *"This is NOT a true ACID transaction. Individual operations within the bulk may
  succeed or fail independently"*, so partial application is **documented rather than silent**, with the
  caller holding an exception. Downgraded to medium as `SH-M426`. The residue is real (stale `IsActive`, an
  intact buffer, no per-item outcomes for a targeted retry) and is not silent data loss.
- **So the recovered set contributed NO high finding, and that is recorded on [[STORY-051]] as "no change,
  for this reason".** Its `finding-count: 57` stands and the 15-task decomposition filed hours earlier
  still covers the tier completely. An omission and a deliberate non-change look identical in a diff unless
  one of them says so.
- **A predicted total is a hypothesis.** [[STORY-055]] predicted **881** (`58 · 430 · 393`); the answer is
  **876**. It double-counted two duplicates it knew about (the error TASK-195 anticipated), missed three it
  did not, and assumed a high that measurement removed. **And the constraint that mattered more than the
  total:** every id was appended past the existing maxima, because [[TASK-151]]–[[TASK-194]] carry
  *explicit contiguous* `findings:` lists, so renumbering anywhere inside a range would silently invalidate
  44+ task files at once. Verified mechanically — the id set went 865 → 876 with **0 lost**.
- **Routing is the half that makes it real, and these three areas had no task at all.** The 44 per-area
  tasks cover the 22 areas that *had* rated findings; these three are precisely the ones that did not, so
  folding ids in alone would have left them *"filed, and scheduled by nothing"*. [[TASK-323]]–[[TASK-327]]
  now own all 11, verified by grep: every new id resolves to a `status: todo` task's `findings:` list.
- **⚠ They are named "Fix", not "Triage", and the distinction is load-bearing.** Their 44 siblings open
  with confirm-or-refute; these arrive **pre-verified**, so a triage step would be busywork and would
  invite the next reader to redo this pass. Each task says so explicitly. Priorities are measured too:
  `SqlUnitOfWork` is named in **3** consumer `.cs` files (so `SH-M427`'s pooled-connection leak per failed
  `BeginAsync` is live), `SH-M425` sits on the base every async store inherits, and the
  `core-model-contracts` trio has **0** `.CopyTo(` call sites anywhere in the framework's non-test code —
  real API defects, latent today.
- **A cross-link that changed a finding's reachability the same day.** `SH-M428` (a failed commit skips
  `CleanupAsync`) names a deadlock-victim commit as its trigger, which was hypothetical when filed;
  [[TASK-306]] reproduced exactly that on live SQL Server 2022 hours earlier — error **1205**, *"Rerun the
  transaction"*, 1 run in 12 under CPU load. A measured trigger is the difference between a theoretical
  cleanup gap and one this tree has seen.

### 39 of the 57 high spec-harvest findings had no task, a month after the same defect was fixed for medium (2026-09-08)

The intake nobody had run. [[STORY-051]] held **31 task files covering 18 of its 57 findings**; the other
**39 had no task at all**, so they were invisible to `/tasks pick`, to the `Next up` snapshot and to
[[fix-next]] — *a checklist line is filed, not scheduled*. [[STORY-053]] diagnosed exactly that for the
**medium** tier on 2026-08-09 and fixed it by decomposing into 22 per-area triage tasks; the fix was never
applied to the tier that outranks it, where **high** means silent data loss, cross-tenant leakage, auth
bypass, or a destructive operation on the wrong rows. Filed as **15 per-area tasks** (TASK-308–322), all 39
findings covered, `fix-next`'s pool **66 → 81**. Seven things worth carrying:

- **The rule the medium tier fixed did not propagate to the tier above it, and nothing noticed for a
  month.** Both stories cite the same rule; only one was decomposed. **When a scheduling defect is fixed in
  one container, check its siblings in the same change** — the severity stories are three instances of one
  shape, and the highest-severity one was the one left out.
- **⚠ Checked item by item before filing, per [[TASK-252]]'s lesson, and it changed the count.** The naive
  arithmetic (57 − 17 closed) says 40; the answer is **39**, because `SH-H037` has a `done` task *and* a
  `todo` follow-up. And **13** of the 39 are *mentioned* in closed task bodies — every one of those mentions
  turns out to be an explicit *"separate task"* / *"unverified"* / *"not fixed here"* boundary, so none was
  quietly already fixed. That check is cheap at filing time and expensive at draining time.
- **⚠ A finding can be contested rather than merely open, and that must ride on the task.** Three closed
  tasks record `SH-H049` as *"downgraded in STORY-051, not tasked"*; [[TASK-118]] then described a live
  fail-open path **through** it and closed saying *"SH-H049 is not fixed here. This task routes around it
  rather than through it."* Both are on the record and they disagree. [[TASK-311]] carries the instruction
  to re-measure the downgrade's premise before relying on it — § TASK-283's rule, which exists because a
  stale measurement kept a P2 open for nine days; here a stale *downgrade* is how a P0 stays invisible.
- **⚠ The mechanical priority rule degenerates here, so it was replaced with a stated one.** `/tasks
  intake` says *"theme 1–2 blockers → P0"* — but every finding in this story is high severity, so applying
  it literally makes all 15 tasks P0 and ranks nothing. The rule used instead was calibrated against the
  story's **existing** 8 P0s (auth bypass, SQL injection, whole-table write, cross-tenant read/delete,
  silent non-persistence): **P0** = the claim, if true, is auth bypass, cross-tenant leakage, or silent
  loss/destruction of data; **P1** = confined to one opted-into feature, decorator, migration or provider.
  Five P0, ten P1, and **each task states its own reason** so the call can be argued with rather than
  inherited.
- **⚠ Consumer reach was measured per area, and it is mostly latent — which is recorded without being used
  as a discount.** Only **two** areas have live consumer use of the exact type the finding names:
  `security-and-authorization` (`AuthenticationService`, **6** consumer `.cs` files) and
  `filter-expression-translation` (`Birko.Data.SQL` in **6** consumer aggregators, on every SQL read's
  path). For the rest the module is imported by an aggregator — often only `Birko.Sandbox`, which imports
  everything — and **0** consumer `.cs` files construct the type. Each task carries its own number, because
  a fix should be priced on what it protects; and each also carries the counter-warning, since this
  framework's recent history is largely defects that stayed latent until a consumer selected the backend
  (§ TASK-219/256: the window *"closes the moment one does"*).
- **The acceptance criteria encode this epic's own hard-won lessons rather than restating a template.**
  Every task requires that a claim of *silent* loss be asserted as **observed state** — rows counted, the
  value read back, the tenant that could see it — *never* that no exception was thrown, because § Conventions
  records several defects that a "did not throw" assertion hid, including one in this epic that hid a live
  MSSql failure for weeks. `confirmed-wider` is an allowed verdict too: of the 15 findings ever hand-checked,
  one was re-verified **wider** than filed, so a claim is not a ceiling.
- **⚠ Regenerating the dashboard surfaced two bugs in the generator itself.** Task titles were being read
  from the whole file, so the frontmatter's own `# status: todo | in-progress | …` comment became the
  displayed title of ~14 tasks (visible in the previous dashboard); and every link was built by stripping a
  forward-slash prefix from a path `glob` had returned with **backslashes**, so the strip silently did
  nothing and every link resolved to `tasks/tasks/…` from a file already inside `tasks/`. Both fixed. The
  count is now verified against `find | wc -l` (**292** task files) rather than against the previous
  dashboard, which had drifted to 269. **A generated artifact nobody diffs is a place where two bugs can
  live in plain sight.**

### The three `in-progress` tasks are cleared, and two of them were stale rather than unfinished (2026-09-08)

Asked for as step 1 of a backlog review: `TASK-038`, `TASK-258` and `TASK-276` had sat `in-progress` for
82, 21 and 17 days. **All three repos were clean** — no uncommitted work anywhere — so `in-progress` was
a status nobody had resolved, not work in flight. An `in-progress` task is one no scheduler offers and no
reader can act on, which is why this was worth doing before picking anything new. Verified: MSSql
**133/133 × 12 loaded runs**, `Birko.Data.SQL` **686/686**, playground `verify.mjs` **679 checks** +
`device-fix-check` **68/68**, exit 0 throughout. Eight things worth carrying:

- **TASK-258 was answered, not unfinished — and the honest close is a NARROWED scope plus a successor.**
  Its question was what `retryWhenOwned`'s *"preserve each provider's retry policy"* preserves; the answer
  is **a no-op**, because `RetryPolicy` defaults to `None` and `ExecuteWithRetry` short-circuits. So its
  criteria 1-2 are not open work, they are questions its own answer made vacuous. Both now carry
  `→ [[TASK-305]]` verbatim, and the `pr:` field that had been left `null` points at the commit that
  landed 21 days ago. Parking such a task `in-progress` indefinitely hides a finished finding behind an
  unfinished-looking status.
- **⚠ Re-measured the claim before the successor cited it, and it had grown.** TASK-258's sweep was 17
  days and ~30 tasks old. Re-run: **0** `RetryPolicy` assignments in framework production code, **0**
  across all 16 consumer repos (production *and* test), **0** in any `appsettings*.json` — and **3** test
  files rather than 2, the new one arriving with TASK-291/294, whose own fix (a rewrap that defeated
  `IsTransientException`) is the *second* defect traceable to nobody ever exercising this machinery.
- **⚠ And the successor got a measured motivation it would not otherwise have had.** Running the MSSql
  suite under load produced a **SQL Server deadlock** — `1205`, *"Rerun the transaction"* — which
  `MSSqlConnector.IsTransientException` **already enumerates** and whose summary advertises it. So the
  framework identifies the textbook retryable error and retries nothing. TASK-305 now opens on an observed
  failure instead of a structural argument.
- **A fully-diagnosed mechanism can be tested deterministically, so the race need not be reproduced.**
  TASK-276's MSSql instance was diagnosed 2026-09-07 (a sibling class's schema escape bumps the shared
  connector's `SchemaGeneration`, so this class's store re-initialises and re-creates the table it had just
  dropped) with a fix *proposed and never applied*. Applied: its own database, hence its own cached
  connector. The test provokes the escape **synchronously** on the shared connector and asserts this
  connector did not see it — no interleaving required. Both halves are asserted, so a change that stopped
  the healing altogether reds it too rather than passing quietly.
- **⚠ TASK-303's fix for the identical twin does not port, and the measurement is what says so.** A shared
  xUnit collection served the TimescaleDB instance two days earlier because the overlap was 5 classes. Here
  **13 of 19** classes issue `DROP TABLE`, so a collection covering them is
  `"parallelizeTestCollections": false` in all but name — the fix TASK-276 explicitly forbids. A separate
  settings id is also *immune by construction*: a class added later cannot reach the connector at all.
- **⚠ The load lever generalises, and the loaded arm found a DIFFERENT flake than the one under fix.**
  0/12 idle against 1/12 loaded, same binary — TASK-276 had established load-as-trigger for the SQLite
  *pool* flake only, and it now holds for a second suite and a second mechanism. But the failure was
  `NullableUniqueColumnLiveTests` deadlocking, not the target, so **the before/after distinguishes nothing
  for the target** and the fix rests on the mechanism plus the deterministic test. Said plainly rather than
  glossed; the deadlock is [[TASK-306]], and the 0-after does not cover it either (~1 in 12 is the expected
  outcome either way).
- **⚠ TASK-038 was three months stale, and the criteria "confirmed by eye" were where the defects were.**
  Its one remaining item was a browser re-check; the playground had meanwhile grown two headless harnesses
  that answer it. Running them: 68 components, 0 empty, 9 token groups, 0 warnings. But `verify.mjs`
  counted *groups* and never drove the editor or the export — and **download-as-file had never been built**
  (no `Blob`, no `download` anywhere), while the live-edit selector is deliberately `:root[data-pg-edits]`
  and not the `[data-theme="playground"]` the task specifies, for a reason the code recorded and the task
  file never learned (an element has one `data-theme`, so claiming it wiped the edits on a theme switch).
  **When a criterion has only ever been eyeballed, drive it before ticking it.**
- **⚠ I left a literal NUL byte in `verify.mjs`, and it ran green.** A lost backslash through a heredoc
  turned `'\u0000'` into the character, which makes `grep` treat the file as binary — so the next person's
  search would silently miss it. Removed, and the same trap then bit a task file. Recorded because the
  harness worked perfectly throughout: **a test harness can be quietly corrupt and green.**









### Seven ways a filter could mean something other than what it said (2026-09-09)

TASK-308, the first of [[STORY-051]]'s 15 per-area triage tasks and the biggest single defect cluster this
epic has drained: all seven high `filter-expression-translation` findings, **7 confirmed (2 narrower), 0
refuted**, five root causes, two repos. Eleven offline suites green: SQL 695, SqLite 379, ES 162, Core 102,
Caching 25, SQL.Views 59, ES.Views 19, Migrations.SQL 87, InMemory 69, JSON 23, XML 18 — **1,638 tests,
0 failed**, 51 new, eight mutations. The standing rules are in § Conventions. Nine things worth carrying:

- **The read path needed its own guard, and that is the thread this closes.** SH-H002, TASK-137 and
  TASK-215 all refuse a *destructive* statement that constrains nothing; a **read** has no such decision
  point. So the two findings claiming *"Delete deletes the whole table"* were **already fixed** — measured,
  both threw `WholeTableWriteException` and left 3 of 3 rows — while the identical predicate on a read
  returned **3 of 3 rows, silently**, which is what survived and what this fixed.
- **The empty condition set is OVERLOADED, so the smaller diff was the wrong one.** Editing
  `IsConstantBoolCondition`'s `Values == null → true` branch is the obvious fix and is measurably wrong:
  `(x.A == 1 || true) && x.B == 2` leaves the identical state and `true` is the *correct* answer there.
  Guarding the unclaimed **node** instead tells the two cases apart where the ambiguity is created — and
  one guard at the top of the recursion closed both findings, which had been filed separately as an
  operand case and a root case.
- **`SH-H022` was the one its area's guard could not cover, and it destroyed rows.**
  `ReturnSingleSubCondition` assigned the enclosing negation away, so
  `x => !(x.Amount == 10 && trueFlag)` rendered `WHERE Amount = @p` instead of `WHERE NOT (…)`: the read
  returned the exact complement and `DeleteAsync` **threw nothing and deleted the complement**. The clause
  is non-empty, so there was nothing for `AddRequiredWhere` to refuse. ⚠ And the file already carried a
  comment about this method overwriting the sibling `IsOr` flag — the `IsNot` half went unnoticed beside
  it.
- **On ElasticSearch, TASK-268's boundary guard was necessary and not sufficient.** It fires on a *null*
  query; a range with a null bound and a half-translated boolean are both non-null. Measured:
  `x.Date > cutoff` → `NumericRange(gt=NULL, gte=NULL, lt=NULL, lte=NULL)`, an unconstrained range, and it
  passed **both** `ParseFilterQuery` and `ParseRequiredFilterQuery` — so `DeleteByQuery` targeted the whole
  index on the commonest shape a time-series filter has.
- **The minimal reading of that finding would have crippled the feature.** "Never emit a null bound" makes
  every `DateTime` comparison untranslatable. NEST offers `DateRangeQuery`, so routing by the value's type
  removes the defect *and* gains the capability — § TASK-281's rule. ⚠ I also wrote a `TermRangeQuery` arm
  and then measured it unreachable (`string` declares no ordering operators); removed, with the **premise**
  pinned rather than the absence.
- **`SH-H028` is contained by removing the grammar, not escaping it.** `Contains` put the caller's value
  into Lucene's query grammar, so `secretField:*` addressed a field the predicate never mentioned. A
  `WildcardQuery` value has no grammar beyond `*` / `?` / `\`, so escaping three characters is total by
  construction where escaping `query_string` is a blacklist — and it restores what `Contains` was
  documented to mean, a substring match, which `query_string` over a keyword field never was.
- **⚠ Five existing tests asserted the wide behaviour and were inverted, not restored.** All five used an
  `InvocationExpression` as their untranslatable example and asserted `WholeTableWriteException`; the rows
  they assert are unchanged, only the type moved upstream. Affordable because **0** consumer references to
  that type exist anywhere. One test's *example* was retargeted rather than its assertion, because the old
  shape would have tested the new refusal and passed for the wrong reason.
- **⚠ Nothing pinned the ElasticSearch behaviour at all — the whole suite stayed green through all three
  of its fixes.** That absence is the finding behind the findings, and it is why the new ES suite asserts
  the numeric path, `StartsWith`, and the empty-collection operand as *unchanged*: without those, a
  refusal that was too broad would be indistinguishable from one that is right.
- **⚠ Three corrections to my own work, each caught by running rather than reading, plus one spawn.** The
  refusal message interpolated `expr.ToString()`, which renders a closure's captured **values** into a log
  — narrowed at the security pass. Two tests were written on a wrong assumption about where `!=` carries
  its negation (`Type=Equal` + `IsNot` on a nested leaf; there is no `NotEqual` type). And the `SH-H024`
  opt-out test failed on `ToUpper()`, which is how [[TASK-331]] (P1) was found: an expression-valued
  `UPDATE` that binds **no parameter** issues no statement at all, because `Update` opens with
  `if (values.Any())` — a silent no-op, now pinned as a defect so it cannot be believed fixed.

### A base nothing derives from is not a base: the SQL bulk stores had no bounded-filter guard (2026-09-09)

TASK-329 / `SH-H002`, spawned while reading `AsyncDataBaseBulkStore` for [[TASK-310]] and worked next
because a silent whole-table rewrite is data corruption. [[TASK-215]] wired `RequireBoundedFilter` into
`AbstractBulkStore` / `AbstractAsyncBulkStore`; the SQL bulk stores derive from **neither**, so on the
framework's primary provider `Update(x => !empty.Contains(x.Name), r => r.Name = "OVR")` rewrote
**3 of 3 rows with `thrown=NONE`**, sync and async. Eight offline suites green: Core 102, SQL 686,
SqLite 363, InMemory 69, JSON 23, XML 18, Caching 25, SQL.Providers 8 — **1,294 tests, 0 failed**, 33 new
— and every test project in the family builds with 0 warnings and 0 errors. The standing rules are in
§ Conventions and § Architecture. Nine things worth carrying:

- **One `grep` was the whole diagnosis.** `grep -rn RequireBoundedFilter Birko.Data.SQL/` returned
  nothing. When a guard is described as living "on the base", check which file **declares** it and which
  concrete types can see it — `DataBaseBulkStore<DB,T>` implements `IBulkStore<T>` directly and carries
  its own copies of all six filter-based overloads, which is also why it has its own private
  `RequireFilter`.
- **The scope was 2 of 6, not 6 of 6, and the task file guessed wrong.** It said the `PropertyUpdate` and
  `Delete` overloads were "very likely" affected too. Measured: those four reach the connector, where
  `AddRequiredWhere` already refuses the identical predicate. Only `Update(filter, Action<T>)` was
  defective — it emits a `SELECT` then per-row `UPDATE … WHERE Guid = @g`, each individually bounded, so
  **no conditionless statement is ever emitted** for a statement-level guard to see. Blanket-wiring all
  six would have been indistinguishable from wiring the two that mattered.
- **Two implementations plus one uncovered hierarchy is a fix at the wrong layer.** Copies three and four
  in the SQL layer were four lines and are the shape § Conventions keeps naming as the cause. The rule now
  lives once in `Birko.Data.Core/Expressions/BoundedFilterGuard.cs` and all three declaration sites
  forward to it passing **only the door name** — the sole thing that differs per caller (§ SH-H037: an
  async store has no `DeleteAll()`).
- **The refactor's behaviour preservation is measured, not inspected.** Making the new producer a no-op
  reds **18** pre-existing tests across three suites (6 SqLite, 10 InMemory, 2 JSON), so the delegation is
  live rather than merely compiling. The two former copies are also byte-equivalent through the change:
  their previous 4-argument `WholeTableWriteException` call defaulted `explicitDoor` to exactly the strings
  they now pass explicitly.
- **The one override of a guarded overload was correct by delegation, and that is now pinned.**
  `CachedAsyncDataBaseBulkStore.UpdateAsync(filter, Action<T>)` — the only one in the framework, swept
  across all seven SQL projects — wraps `base.UpdateAsync`, so it inherited the fix. An edit inlining the
  loop to save a call would reopen a whole-table rewrite behind a decorator whose own tests stayed green.
- **No live per-provider run, and the reason is stated rather than skipped.** Neither defective overload is
  overridden by any provider, and the guard runs on the **expression** before any provider code executes —
  so a PostgreSQL/MySQL/MSSql run would exercise the same statement on the same shared class. Recorded as a
  reasoned scope decision with its measurement, not as a claim of live verification.
- **⚠ The close gate found the new guard had no test in its DECLARING project — third instance.**
  § TASK-255 records the rule (*a guard declared in `Birko.Data.SQL` is tested in `Birko.Data.SQL.Tests`,
  not only from its consumer's suite*) and TASK-257's gate caught it for `IsInIndexKey`; here all 17 tests
  of a `Birko.Data.Core` type lived in store projects. Writing
  `Birko.Data.Core.Tests/BoundedFilterGuardTests.cs` immediately earned a fifth mutation: the producer's
  two checks are **order-dependent** — `ReducesToAllRows(x => true)` is *also* true, so the explicit-door
  check has to run first or the documented `DeleteAll()` synonym would be refused — and nothing pinned
  that ordering. Swapping them reds 3 Core tests and 2 SqLite ones.
- **⚠ And § Architecture had been stating the defect's premise all along.** Its store-hierarchy and
  SQL-store diagrams sit next to each other with nothing saying they are **disjoint**, so they read as one
  chain. That is the misreading, in the rulebook, for the life of the file. Now stated explicitly with the
  measured cost — the register-on-introduce check earning its place by finding drift rather than a gap.
- **⚠ Spawned [[TASK-330]] (P3):** the two SQL bulk stores declare no `UpdateAll(Action<T>)`, so an
  action-overload caller is pointed at a door taking a `PropertyUpdate<T>`; the portable bases *do* declare
  it and their message still names the sibling. Nothing does the wrong thing either way — a decision about
  public surface, so filed rather than widened into a P0 defect fix.

### A composite PRIMARY KEY could not be declared at all, and TimescaleDB requires one (2026-09-08)

TASK-303, split out of TASK-252 on pick. `FieldDefinition` rendered `PRIMARY KEY` inline per column, so
two primary fields emitted **two clauses** and every provider rejected the statement. Verified with
`BIRKO_REQUIRE_LIVE` set against live TimescaleDB 2.29.2, PostgreSQL 16, MySQL 8.4, SQL Server 2022 and
on-disk SQLite: **1,639 tests, 0 failed** across nine suites, 14 new. Six things worth carrying:

- **The server states the remedy itself.** `create_hypertable` over a Guid-keyed table answers
  *"cannot create a unique index without the column ts (used in partitioning)"* and hints *"ensure the
  partitioning column is part of the primary or composite key"* — so the framework could not express a
  shape one of its own providers demands. With `PRIMARY KEY (Guid, Ts)` it converts cleanly.
- **The veto measurement said go, where TASK-248's said stop.** 0 classes across the framework, its tests
  and all 16 consumer repos declare more than one primary, so the change is purely additive.
- **⚠ Two of this task's own premises were incomplete.** It cited only PostgreSQL's `42P16`; **SQLite
  fails too** (`Error 1: table has more than one primary key`). And SQLite's
  `INTEGER PRIMARY KEY AUTOINCREMENT` cannot join a table-level clause at all — refused up front with a
  message naming autoincrement, which the server's own error does not.
- **⚠ A mutation exposed a hole in my own tests.** With only the suppression asserted, deleting the
  table-level clause left `Birko.Data.SQL.Tests` **entirely green (683)** — only the live tests caught it.
  Suppression without emission is *worse than the original defect*: the table gets **no key at all**,
  silently, and bulk update and delete key on `GetPrimaryFields()` and would quietly do nothing.
  **A test that asserts a flag is not a test that asserts a statement.**
- **⚠ And writing the round-trip test found a limitation the fix does not cover.**
  `AbstractDatabaseModel` puts `[UniqueField]` *and* `[PrimaryField]` on `Guid`, so a subclass carries a
  standalone `UNIQUE (Guid)` forbidding two rows that share one — measured as
  `SQLite Error 19: 'UNIQUE constraint failed'`. The capability is real and **the framework's own base
  model still cannot use it**; [[TASK-304]] owns that, and the qualification is on the task rather than
  left implied by a passing suite.
- **⚠ [[TASK-276]]'s TimescaleDB flake was caught and diagnosed** once the sweep kept a `.trx` per project
  instead of grepping the summary line: a parallel class dropped a continuous aggregate's internal table
  mid-catalogue-read (`42P01 … _materialized_hypertable_1048`). Fixed by serialising the five live classes
  that share the database — not by disabling parallelism, which that task forbids. Two of them are ones I
  added this week, so it was plausibly my own regression.

### Half of a grouped latent-gaps task had already been closed by other work (2026-09-08)

TASK-252 collected six per-provider gaps that the index-DDL thread had left as prose in closed tasks'
out-of-scope sections — filed so they would be schedulable rather than urgent. Worked today: **three of
the six were already resolved**, one was declined on a measurement, one split out, and one turned out to
have a stale premise. Verified against live PostgreSQL 16, MySQL 8.4 and SQL Server 2022: Migrations.SQL
87, MySQL 119, SQL 678 — 0 failed. Four things worth carrying:

- **⚠ A grouped latent-gaps task must be re-checked item by item before it is worked.** Its whole premise
  is that the items sat still, and adjacent tasks landing is exactly what stops them sitting still. #3
  (`Sparse()` / `WithProperty()` no-ops) was fixed by TASK-274; #4 (MySQL's 3072-byte ceiling on bounded
  columns) was answered by TASK-266, pinned rather than guarded because SQL Server and MySQL behave
  oppositely; #5 (`byte[]` unindexable on MySQL) was fixed by TASK-266. Working from the list as written
  would have been three re-investigations of closed questions.
- **⚠ And a recorded coverage fact expires exactly like a blast radius does.** The task predicted a revert
  of the async index loop *"fails **0** tests because nothing reaches it"*. Measured by making
  `CreateIndexesAsync` throw: **5 failures across three suites**. The claim was true when written, and
  TASK-273's close gate then added the async-funnel coverage it said was missing. § TASK-283's rule
  (re-measure before concluding) applies to *coverage* claims, not only to consumer counts.
- **A decline is a verdict only if it carries the measurement.** `RenameField` needs MySQL 8.0+ while the
  provider's own guide declares 5.7 support — a promise the code does not keep. Declined because there are
  **0** callers anywhere, and because the fallback is not a dialect swap: measured on 8.4.11, a type-less
  `CHANGE` is `ERROR 1064`, so a 5.7 path must read the column's full definition and restate it, which
  risks silently altering a column a rename should leave alone. **Recorded beside the version claim it
  contradicts**, not only on the method — a reader checking whether their MySQL is supported looks at the
  provider guide.
- **Splitting on pick worked as designed.** The task's own criterion said to split #2 out once it had a
  measurement, a consumer-visible consequence and a dependent — it had all three, so composite primary
  keys are now [[TASK-303]] rather than one row of a grouped table.

### A hypertable probe answered "no" for a hypertable that exists, and "maybe" for one that does (2026-09-08)

TASK-280. `IsHypertable` and `GetChunkInterval` matched the caller's name against a catalogue column
holding the **bare** table name, with the schema in a separate column. Verified against live
TimescaleDB 2.29.2: **1,021 tests, 0 failed** across six suites. Five things worth carrying:

- **Both failure modes measured before the fix**, with `public."Evts"` and `reporting."Evts"` both
  hypertables: a **qualified** name matched **0** rows, so `IsHypertable` answered false for a hypertable
  that exists and `GetChunkInterval` returned null — which its own doc defines as *"not a hypertable"*.
  An **unqualified** name matched **2**, and `ExecuteScalar` took whichever the planner emitted first:
  `1 day` or `7 days`, arbitrarily.
- **The fix resolves both sides to the same OBJECT rather than comparing name text** —
  `(quote_ident(schema)||'.'||quote_ident(name))::regclass = to_regclass(@table)`. That is the server's
  own resolver, the same one `::regclass` gives the emitters, so a name means one thing on both doors
  (§ TASK-274). It also needs **no splitter at all**, which serves the one-producer rule better than the
  criterion's own suggestion of splitting and matching two columns.
- **It answers "what does an unqualified name mean?" by measurement, not taste:** the `search_path` —
  because that is what `create_hypertable` did when it created the object.
- **`to_regclass`, never `::regclass`.** The cast *throws* for a name that does not exist, and a probe
  must answer `false` for an absent table rather than fault.
- **⚠ A test was asserting the defect — third consecutive task.** It created the hypertable qualified and
  then asked for the bare name, calling it *"a documented limitation rather than a promise"*. After
  TASK-284's `[InlineData("")]` and TASK-279's `..._DefaultsOrderByTime_...`, that is three in three days,
  and it is now a standing rule: **when a defect survives a well-covered area, check whether a test is
  holding it in place.** The mutation reds only the two schema-aware tests while the **84** single-schema
  ones stay green — which is exactly why they never caught it.

### CR-H070's other half: a default that could not work on any Birko entity (2026-09-08)

TASK-279, the sibling TASK-255 deliberately refused to fix from symmetry.
`BuildCompressionPolicySql` declared `orderByColumn = "time"`, and no framework-created table can have such
a column — definitions are emitted bare and every Birko entity is PascalCase. Verified against live
TimescaleDB 2.29.2: **85 passed, 0 failed**. Four things worth carrying:

- **The default was a source-compatibility artefact, and only `git show 531d816` shows that.** The commit
  that *fixed* CR-H070 introduced the parameter and defaulted it so then-existing calls kept compiling —
  never a judgement that `"time"` is good. TASK-255 recorded the rule (*measure a precedent's motivation,
  not its shape*) and left this half so it would be measured rather than copied.
- **⚠ A test was pinning the default.** `CompressionPolicy_DefaultsOrderByTime_AndOmitsSegmentBy` asserted
  `compress_orderby = 'time'`, making it a named, asserted contract — which is why it survived CR-H070's
  own remediation. **Second instance in two days**, after TASK-284's `[InlineData("")]`. When a defect
  survives a well-covered area, check whether a test is holding it in place.
- **⚠ And the task's own blast radius was stale.** It said "no other caller anywhere" relies on the
  default; four test call sites did. The number that actually decided was 0 of 16 consumer repos, which
  was unchanged — but the claim was corrected rather than reused.
- **TASK-255's silent-rebind hazard does not apply, and saying so matters.** That warning is about an
  *inserted* parameter letting a call rebind quietly. Removing a default changes neither arity nor order,
  so all four sites failed `CS7036`, loudly. `segmentByColumn` keeps its `null` — the
  `compress_segmentby` line is *omitted* when unset, so that default works, and the reflection pin asserts
  both sides so a change stripping both cannot pass half the test unnoticed.

### An empty config value silently widened a refresh policy to all of history (2026-09-08)

TASK-284. `add_continuous_aggregate_policy`'s `start_offset` takes `NULL` to mean *"from the beginning of
time"*, and the emitter used `string.IsNullOrEmpty` — so a configuration value that came back `""` rather
than null produced a far heavier policy than the author intended, on every run of the job, with no error
anywhere. Verified with `BIRKO_REQUIRE_LIVE` set against live **TimescaleDB 2.29.2 / PostgreSQL 16** plus
four other providers: **1,363 tests, 0 failed** across seven suites. The standing rule is in § Conventions.
Five things worth carrying:

- **The asymmetry was the tell.** Every other interval in that class already failed loudly on `""` —
  `INTERVAL ''` is `22007`. One parameter behaving differently from its five neighbours is the signal.
- **⚠ The second finding was a false alarm, and measuring it first is what the task demanded.** It
  suspected the door its own refusal names — *"pass a null startOffset"* — might not open, since the
  server declares those parameters `"any"` and an untyped bare `NULL` is what a server can reject.
  Measured: **accepted**. So the fix was a *live* test for a path that had only ever been asserted as a
  rendered string, not a change to the message.
- **⚠ The suite was asserting the defect.** `[InlineData("")]` on the null-rendering theory made
  "empty means all of history" a documented contract, not an untested corner. When a defect survives a
  well-covered area, check whether a test is pinning it.
- **The wrong value failed by working** — an over-wide policy is a valid policy. Both behaviours are now
  on the method's remarks, because the caller has no other signal.
- **⚠ Two fixture faults of mine.** The live helper first asked for "the only policy in the database" and
  read my own probe's leftover as the answer; and the obvious catalogue join was wrong — measured,
  `jobs.hypertable_name` is the **view** name, not the materialisation hypertable, so the join matched
  nothing and every assertion read `<none>` while looking green.

### A test teardown was disposing parallel siblings' database handles, and the production question is answered (2026-09-08)

TASK-276, taking the question that file names as its priority — *can this happen in production, or does it
need `ClearAllPools`?* — rather than "make the suite green". Answered **no**: measured, 0 calls in the
framework's production code and 0 in any of the 16 consumer repos' production code, so consumers cannot
reach it and it is test hygiene. Verified with `BIRKO_REQUIRE_LIVE` set against live PostgreSQL 16,
MySQL 8.4, SQL Server 2022 and on-disk SQLite: **1,483 tests, 0 failed** across seven suites. The standing
rule is in § Conventions. Six things worth carrying:

- **⚠ The expensive experiment this task designed was unnecessary, because a consumer had already run it.**
  Symbio's TASK-657 records the same mechanism independently, with 14 consecutive full-suite runs → 2
  failures, both `ObjectDisposedException: 'SQLitePCL.sqlite3'`, in two *different* classes — and they had
  already built the per-database helper and a guard. Check whether a cost is real before paying it.
- **⚠ I could not reproduce the flake, and the before/after therefore proves nothing.** 12 idle + 6 loaded
  runs before, 12 after, 0 failures throughout. The change is justified by the mechanism, confirmed three
  ways; "0 after" is not evidence and is labelled as not evidence.
- **⚠ Sample size decided the fix.** Deleting the calls outright was the obvious remedy and my first
  6-directory sample supported it. At 400 directories, **164 still held files** — the pool does hold
  handles, so deleting would have made the leak worse.
- **The guard caught its own file on the first run**, because the forbidden literal was still in its
  `<remarks>`. That is the trap its own `Forbidden` field documents, and a fair demonstration it works.
- **The suite got faster**: 11 s → 6 s. Two dozen process-wide pool clears were not free.
- **⚠ Spawned [[TASK-302]]**: ~**90,000** leaked `%TEMP%irko-*` directories, because every teardown
  swallows its delete failure. Unrelated to the pools, found while measuring them, and invisible for as
  long as the suites have existed.

### Four features had put per-caller state on the process-wide connector, and only a test can stop the fifth (2026-09-07)

TASK-270. `DataBase.GetConnector` caches a connector per (type, settings id) for the life of the process,
and four independent features have put one caller's state on it — the unit-of-work's `DbTransaction`
(TASK-240), the append-only index-failure list, the migrations builder's connection and transaction
(TASK-259), and now `IsInitializing`. Verified with `BIRKO_REQUIRE_LIVE` set against live PostgreSQL 16,
MySQL 8.4, SQL Server 2022 and on-disk SQLite: **1,479 tests, 0 failed** across seven suites, 6 new. The
standing rule is in § Conventions. Six things worth carrying:

- **⚠ Instance four was already in the code and this task's own audit said "nothing is currently firing".**
  `IsInitializing` was a plain mutable flag guarding `DoInit`, so a second flow arriving while the first
  was inside its `OnInit` handlers had its initialisation **silently discarded** — not deferred, not
  retried. The unsynchronised check-then-set was the smaller half.
- **And a third defect fell out of fixing it:** the reset was a bare assignment, not a `finally`, so a
  throwing `OnInit` handler left the flag stuck `true` and permanently suppressed `DoInit` for every
  caller of that database. Now an `AsyncLocal<bool>` per instance behind a scope.
- **Prose is not a mechanism.** Instances two, three and four all shipped *after* § Conventions said not
  to do this. `ConnectorSharedStateTests` asserts the shape by reflection, plus two behavioural tests for
  instance four — and the mutation reverting `IsInitializing` reds all three, so they are regression
  provers rather than pins.
- **⚠ The obvious fix for `RetryPolicy` was measured and rejected.** Moving it onto `Settings` hides the
  sharing rather than removing it: `GetId()` is `Location:Name(:UserName:Port)` and carries neither it nor
  `CommandTimeout`, so two settings differing only there already share a connector and the first caller
  wins. It stays settable as a **ledger entry with its reasoning**, and the ledger has a currency test so
  a stale entry must be deleted rather than silently covering something else.
- **⚠ Three of this file's own counts were stale and one was wrong** — `RetryPolicy` assignments were
  claimed as 0 and are 2 (both tests, so the conclusion held and the claim did not), four events are now
  five, and the DI-seam blast radius was unmeasured: **29 `GetConnector` call sites across the consumer
  repos**, which is why Q2 is deferred rather than half-started.
- **⚠ The sweep identified [[TASK-276]]'s flake, which had been open since TASK-273 with no identity.**
  `SchemaEnsureRollbackResidueLiveTests.A_write_to_a_missing_table_fails_instead_of_reporting_success`,
  8/8 clean alone and ~1 in 5 in-suite. Every class in that suite shares one cached connector, so another
  class's deliberate schema escape bumps `SchemaGeneration`, this test's store re-initialises and
  re-creates the table it had just dropped. Not a product defect — TASK-288's healing is correct — and not
  a ledger entry, because `SchemaGeneration` *should* be shared. It is the other half of the thesis:
  correctly-shared state still has cross-caller reach.

### Nothing reported a stale column type, and the obvious way to detect it would have missed the worst case (2026-09-07)

TASK-269. Birko never reconciles an existing table — `CREATE TABLE` is guarded by `IF NOT EXISTS` and
schema-ensure only creates — so a model change, or an upgrade past any of the column-typing fixes
(TASK-257, TASK-264, TASK-265, TASK-266, TASK-275), leaves the old column in place with no signal but an
exception at the call site. `AbstractConnector.DetectDrift` now answers it and
`Birko.Health.Data.SQL.SchemaDriftHealthCheck` reads it. Verified against live **PostgreSQL 16**,
**MySQL 8.4**, **SQL Server 2022** and on-disk SQLite with `BIRKO_REQUIRE_LIVE` set: **1,562 tests,
0 failed, 0 skipped** across eight suites, 23 new. The standing rule is in § Conventions. Eight things
worth carrying:

- **⚠ The provider-independent mechanism cannot see the width, and measuring that inverted the design.**
  Consumer Symbio's `SchemaDriftCheck` reads `SELECT * FROM T WHERE 1 = 0` and takes the reader's column
  *names*, explicitly to avoid "a diagnostic that only runs on the dialect the developer happens to use".
  Extending it with `GetDataTypeName()` was the obvious move and is **wrong**: measured, it returns
  `VARCHAR` for `VARCHAR(255)` and `DECIMAL` for `DECIMAL(18,2)`, and `GetColumnSchema()` answers
  `ColumnSize = -1` with null precision and scale. So a reader-based check reports TASK-264's silent money
  truncation — `DECIMAL(18,0)` against `DECIMAL(18,2)`, the same keyword — as a **clean bill of health**.
- **The dialect branch is the only mechanism that answers, so it goes where every other provider
  capability already lives.** Symbio's reasoning was right for *names* and does not transfer; it also had
  no connector to hang a branch on, and this framework does. Same family as `SupportsTransactionalDdl`,
  `FoldsUnquotedIdentifiers`, `SupportsPartialIndexes`.
- **⚠ `GetFieldType()` is value-dependent and must never be the oracle.** Measured: a SQLite `REAL` column
  holding the text `'not-a-number'` reads back as `String`, and one column answered `String` empty and
  `Double` populated. Drift would be reported or not according to which rows happened to be in the table.
- **One producer for the declared side, and it is the method `CREATE TABLE` uses.** `ConvertType`, so the
  check and the DDL cannot disagree and every past and future column-typing rule is covered without being
  restated. A check that re-derived the expected type is the second implementation this epic keeps paying
  for.
- **The subscriber ships with it, because a channel with no reader is the defect being closed.**
  Re-measured 2026-09-07: `OnIndexCreationFailed +=` has **0** subscribers across all 16 consumer repos,
  so every index failure since TASK-204 has been silent. The health check reports both, giving that
  channel its first reader.
- **⚠ Every one of the five fixes has a zero deployed population, and my own recommendation overstated
  it.** No consumer selects a server provider (`"Default": "SQLite"` in every Symbio environment; every
  non-test `DataProvider.MsSql` is a switch case), and TASK-264's only non-test `ISchemaBuilder` hit is a
  doc comment saying why it is *not* used. So the justification is **model evolution against an existing
  database**, not the five fixes — a live population of every consumer.
- **A new sibling project, not a file in `Birko.Health.Data`**, which is dependency-free by construction.
  TASK-234 refused exactly this for Redis; the distinguishing measurement is that a Redis check is useful
  without Birko.Redis while a SQL-schema-drift check is meaningless without Birko.Data.SQL — and 2 of 2
  aggregators importing `Birko.Health.Data` already import `Birko.Data.SQL`.
- **⚠ Two defects the live run caught that no offline test could.** PostgreSQL's `RegclassLiteral`
  returns the literal's *contents*, not a quoted literal, so the catalogue query raised `42703` on every
  table until the quotes were added; and `RunReaderCommandOn` invokes its transform **once per row** with
  the reader already positioned, so a transform that loops internally silently loses the **first column of
  every table**. Both were found by tests, not by reading.

### A `byte[]` index key meant no table at all, and the wide composite was pinned rather than guarded (2026-09-07)

TASK-266, the binary and width half TASK-257 deliberately left when every one of its criteria said
*string*. `ConvertType` mapped `DbType.Binary` to `VARBINARY(MAX)` / `LONGBLOB` unconditionally, and
neither provider can use an unbounded blob as an index key. Verified with `BIRKO_REQUIRE_LIVE` set
against live **SQL Server 2022 (16.0.4265.3)** — the build this task's own numbers came from — **MySQL
8.4.11**, **PostgreSQL 16.15**, **TimescaleDB 2/PG16** and on-disk SQLite: **1,599 tests, 0 failed,
0 skipped** across nine suites, 43 new. The standing rule is in § Conventions. Eight things worth
carrying:

- **An inline `UNIQUE` over `VARBINARY(MAX)` is not merely a lost index — it is Msg 1919 + Msg 1750 and
  `TRY/CATCH` cannot intercept it**, so the batch aborts and the whole `CREATE TABLE` fails. A
  `[UniqueField] byte[]` entity had *no table*. On MySQL the same shape is ERROR 1170.
- **The remedy the task wanted was not expressible, which is what widened the fix.** `BinaryField` had
  no length at all and `CreateAbstractField` never passed `maxLength` for a `byte[]`, so
  `[MaxLengthField(32)]` was silently dropped — "declare a width" would have been § TASK-263's *escape
  hatch that did not open*. Opening it is half the change, and it is the shape a real binary key (a hash,
  a UUID) actually wants.
- **Bound at the provider, never refuse the declaration** — an unbounded binary unique key is legal on
  PostgreSQL and SQLite, so a framework-wide refusal would break a working entity on two providers to fix
  two others. § TASK-248's veto, third time it has decided one of these.
- **Gate on the field's runtime type, because `DbType.Object` shares that `case` on all four
  connectors.** A serialized object has no byte width, and a length applied to one would truncate it.
  Tested both ways, including that a null field neither NREs nor gets bounded.
- **⚠ The wide composite is PINNED, not fixed, and one measurement decided that.** 4 × `NVARCHAR(255)`
  is 2040 bytes against a 1700-byte limit; SQL Server creates the index anyway with a warning, rejects a
  max-width INSERT (Msg 1946) — **and a short row still inserts fine**. So it is data-dependent rather
  than broken, and refusing at DDL would break working code (`PredicateScope`'s rule). It is also not
  computable where the type is chosen: `ConvertType` sees one field, no index.
- **The two providers behave oppositely here, and both are pinned.** MySQL **refuses** the same
  4-column index outright (ERROR 1071, 4080 of 3072 bytes) where SQL Server only warns. So a framework
  guard would duplicate one server while regressing the other — and note how tight MySQL's margin is:
  three columns is 3060 bytes, inside the limit by **twelve**.
- **255 is a cross-provider agreement, not either server's ceiling** — measured, `VARBINARY(901)` indexes
  on SQL Server (the real limit being 1700 bytes) and `VARBINARY(3072)` on MySQL. The same model runs on
  both, so a width that indexes on one must index on the other. Same reasoning TASK-257 recorded for the
  string knob.
- **⚠ Two measurement faults of mine, both of which looked like code failures.** I set
  `BIRKO_REQUIRE_LIVE` globally across suites whose servers were not up and read the resulting 63 and 65
  failures as signal — the skip-as-failure trap § TASK-259 records falling into one task after
  documenting it. And my new MySQL live class defaulted `BIRKO_MYSQL_PASSWORD` to `Birko!Passw0rd`
  while **all nine** existing classes there default to `root`, producing 65 unrelated `Access denied`
  failures — the same fixture trap TASK-273 recorded *in that very suite*. Both diagnosed by reading the
  failure rather than the pass/fail bit.

### A migration's declared column metadata never reached the column, and money became a float (2026-09-07)

TASK-264. `SchemaField` adapts a `FieldDescriptor` to the SQL layer's field model and forwarded **5 of
its 15** properties. The connectors read a column's size off the field's **runtime type** — `field is
CharField` before a length, `field is DecimalField && Precision != null && Scale != null` before a
precision — and `SchemaField` derived straight from `AbstractField`, so it satisfied neither test.
Verified `Birko.Data.Migrations.SQL.Tests` **87 passed** (54 → 87), plus the two suites that import it
(`Migrations.TimescaleDB` 81, `SQL.View.Migrations` 14): **183 tests, 0 failed, 0 skipped**. The standing
rule is in § Conventions. Seven things worth carrying:

- **The unfiled half was the worse half.** The task named `MaxLength`; the same method dropped
  `Precision`/`Scale` with a nastier outcome. A bare `DECIMAL` has default scale **0** on SQL Server and
  MySQL, so declared money was **truncated to whole units** — and on **SQLite, this framework's default
  provider**, an unqualified decimal falls back to **`REAL`**, so a column declared `DECIMAL(18,2)` held
  binary floating point. Both silent. Fixing one and not the other is § TASK-207's *"re-keying half a
  dictionary is not a fix, it is a narrower bug"*.
- **⚠ Two of the task's own premises were wrong, and measuring inverted both.** It said the index failure
  is *"recorded on `IndexCreationFailures` and silent (TASK-204)"*: no — `CreateIndexes` catches only
  `IsIndexAlreadyExistsException`, which the base returns `false` for and **MSSql does not override**
  (only MySQL does, for 1061), so that filter cannot match *any* exception there and Msg 1919
  propagates. The recording lives in *schema-ensure's* per-index catch, which a migration never enters.
  Verified from the type system, which is stronger than a live run.
- **`IsIndexed` cannot be set here, and the fix works anyway.** `SqlCollectionBuilder` and
  `SqlIndexBuilder` are separate nested classes with separate `Build()` calls and no shared state, often
  in separate migrations — so at `CREATE TABLE` time nothing knows an index is coming, and
  `DataBase.LoadIndexes`' trick of seeing a whole entity's attributes has no analogue. Criterion 2 is
  therefore answered **built when a length is declared, loud when not**: no cross-builder state, no
  imposed ceiling, and it is what the author must do on MySQL regardless.
- **One producer, mirroring the attribute path's dispatch — including its quirk.** `SchemaField.For`
  copies `CreateAbstractField`'s `MaxLength`-then-`Precision` fallback for strings, so the two producers
  cannot disagree about what a length is. All three construction sites go through it; the mutation that
  bypasses only the `AddField` site reds **exactly one** test, which is what proves the `ALTER TABLE ADD`
  path needed wiring independently.
- **Both sides of every switch are pinned.** An undeclared length must *still* be unbounded and an
  undeclared precision must still be the provider's default — otherwise the fix is indistinguishable from
  bounding every migration string, which would impose a ceiling on values that write fine today
  (§ TASK-248).
- **SQLite's `TEXT` is asserted as correct, not as a gap.** It has no length-enforcing string type, so
  the criterion's "all four providers" is honestly three; pinning `TEXT` is what stops a later reader
  "fixing" SQLite into a divergence from its own convention. Fourth instance of § TASK-245's *"look for
  the field that gets lost on the way in"*, after TASK-245, TASK-246 and TASK-274.
- **⚠ Two knobs deliberately left, both spawned rather than absorbed.** `DefaultValue` is accepted by
  `WithField` and **no connector emits `DEFAULT` at all** ([[TASK-298]] — a knob the mechanism cannot
  deliver, § TASK-296); `IndexName`/`IndexOrder`/`IndexDescending` are read by **nothing in any backend**,
  so an inline index declaration yields a column and no index ([[TASK-299]], which is also the one shape
  where `IsIndexed` *would* be knowable at column time). Missing features, not dropped assignments.

### The close gate's project-local convention checks had never run, twice over (2026-09-07)

TASK-267, its own P1 and about the gate rather than the code. This repo ships
`.claude/skills/verify-conventions/` on the theory that a project-local skill shadows a user-level one of
the same name — the header said so, `install-skills.ps1` said so, § *Skills shipped by this repo* said so,
and the **generic** skill said so. Measured 2026-09-07 from the skill loader's own banner:
`Skill(verify-conventions)` resolved to `~/.claude/skills/verify-conventions` (the generic file, none of
checks 1–10), while `Skill(new-store-backend)` — no user-level twin — resolved to this repo. So
**project-local skills are discoverable and a colliding name resolves user-level first**: the failure was
precedence, never discovery, and shadowing was never a mechanism. Every `/tasks close` and `/fix-next` in
this repo has linted with the generic skill alone. The standing rule is in § *Skills shipped by this
repo*. Seven things worth carrying:

- **The premise was written down in four places, which is why two fixes bounced off it.** The first fix
  renamed `verify-birko-conventions` → `verify-conventions`; it could not have worked, and nothing
  verified that it had. § *verify the escape hatch opens* — a fix whose mechanism was never executed.
- **The detector belongs in the skill that WINS, not the one that loses.** Same discipline TASK-295
  records for `CreateTable`: bookkeeping a rule depends on goes in the non-bypassable wrapper. So the
  generic `verify-conventions` gained a **step 0** that globs
  `.claude/skills/verify-*conventions*/SKILL.md`, runs its own pass, hands off, and **names the extension
  on its report header** — reporting a 🛑 if it finds one it did not run. A distinct name is what *arms*
  the gate; the old header claimed renaming would disarm it.
- **Two doors, one answer** (§ TASK-274). `verify-birko-conventions` is now directly invokable *and*
  reachable by discovery, so its step 0 is conditional on which door was used — mandatory when invoked
  directly, skipped with a note when the generic pass already ran, or the two skills loop and double
  every finding.
- **The audit found a second instance and four non-instances.** `roll-changelog` collided identically and
  also never ran (renamed `roll-birko-changelog`). Four skills junction *into this repo*, so they resolve
  to the same bytes either way and were never at risk; two have no user-level twin and always worked.
- **The five `[[verify-birko-conventions]]` references were not stale — they were early.** They are
  correct again without being touched.
- **⚠ The gate caught a real violation on the change that made it able to fire.** Running it on this
  diff, check 9 reported the missing `Recent Updates` entry — this one. Before the fix that check could
  not have run at all.
- **⚠ What is NOT fixed: a skill instruction is not an enforcement mechanism.** Step 0 is as hard as a
  skill system allows — it is in the file that always loads, at the top, with a blocker for the negative
  case — but nothing *compels* an agent to execute it. A pre-commit hook is the only mechanism that
  cannot be skipped, and § *Where this runs* already names [[update-config]] for it. Recorded rather than
  claimed as closed.

### A throwing diagnostic subscriber could brick an entity, and the reason it was left alone had expired (2026-09-03)

TASK-283. `RecordIndexCreationFailure` raised `OnIndexCreationFailed` with a bare `Invoke` **inside** the
catch implementing TASK-204's degrade, and stores set `_initialized` only after schema-ensure returns — so
a subscriber that threw left the entity's whole surface, reads included, throwing until restart. Exactly
what TASK-204 removed, reintroduced through the channel that reports it. Verified with
`BIRKO_REQUIRE_LIVE` set against four live servers and on-disk SQLite: **1,619 tests, 0 failed, 0 skipped**
across eleven suites. The standing rule is in § Conventions. Five things worth carrying:

- **The premise that kept it open for nine days was stale.** TASK-254 left this channel bare because it had
  real consumers where the hypertable one had none. Re-measured: **0** `+=` subscriptions across all 16
  consumer repos — the cited "consumers" were doc comments, one of them explaining why it does *not* read
  the channel. It had been free to harden the whole time.
- **Grep for the subscription, not the identifier**, and keep the *collection* and the *event* apart: the
  collection has one real reader and is untouched, with a test saying so.
- **§ TASK-259 cuts both ways** — a stale blast radius can make you claim safety you do not have, or block
  on a decision that does not exist. Re-measure before concluding you are blocked.
- **The hypertable channel moved too, narrowing this task's own out-of-scope bullet on purpose.** It was
  not broken, but it was fixed *differently* — a single `try` that swallows without recording — and two
  policies side by side is the divergence criterion 6 forbids. All three channels now share TASK-289's
  `RaiseDiagnostic`, which also gives the hypertable channel per-subscriber isolation and turns its silent
  swallow into a recorded one.
- **⚠ `pg_isready` answers during initdb.** The TimescaleDB suite reported 15 of 17 failing purely from
  that, and 56/56 twice once the server was genuinely up. Wait on a real query. This sweep also ran with a
  trx logger, the correction [[TASK-276]] asked for after a previous run lost a failure's identity.

### A blanket rewrap was silently disabling the retry policy, cancellation handling and every host `catch` (2026-09-02)

TASK-291 + TASK-294, filed apart and closed as one change because they were one line.
`EnsureSchemaAndReport` rewrapped **every** exception as `new Exception(DescribeSchemaEscape(ex, …), ex)`
— right for a missing table, where TASK-286's annotation rides on the message deliberately, and pure loss
for everything else, where `DescribeSchemaEscape` returns the command text unchanged. Verified with
`BIRKO_REQUIRE_LIVE` set against live PostgreSQL 16, MySQL 8.4, SQL Server 2022, TimescaleDB 2 and on-disk
SQLite: **1,614 tests, 0 failed, 0 skipped** across eleven suites. The standing rule is in § Conventions.
Six things worth carrying:

- **Step 0 found a third consequence neither task file had**, and it was found by enumerating the catch
  filters rather than by reading the tickets: `ExecuteWithRetry` filters on `IsTransientException(ex)` —
  the **direct** predicate, not a chain walk — so a rewrapped `SQLITE_BUSY` stopped being transient and
  **a `RetryPolicy` a consumer configured silently never fired** on any failure raised inside the try.
- **Two tasks naming the same line are one change.** Fixing them apart would have meant reasoning about
  the same catch twice and building the second on the first's assumptions.
- **Keep what the rewrap was actually contributing.** The command text moves to
  `Exception.Data[AbstractConnector.CommandTextDataKey]`; dropping it would have traded one diagnostic for
  another and called that a fix.
- **`ExceptionDispatchInfo.Capture(ex).Throw()`, not `throw ex`, and it is witnessed** — the mutation reds
  exactly one test and the stack head degrades from
  `Microsoft.Data.Sqlite.SqliteException.ThrowExceptionForRC` to `EnsureSchemaAndReport`.
- **⚠ The first end-to-end test measured the one path that was already fine.**
  `RunCommandTransaction` calls `BeginTransaction()` outside its `try` and Microsoft.Data.Sqlite issues
  `BEGIN IMMEDIATE`, so a lock contended *before* the statement never reaches this funnel, already keeps
  its type and already retries — measured as 0 `OnExecute` for the INSERT and a raw `SqliteException`
  code 5. The contrast is pinned; the defect is only on failures raised inside the try.
- **⚠ TASK-294's premise is now much rarer and that is recorded, not used to close it quietly.** TASK-296
  put SQLite on WAL, where readers do not block writers, so its original 6-7 `Error 5` per storm run are
  largely gone. Reachability dropped; wrongness did not — and answering `0` for a *busy* database was
  never a candidate, because `0` is the truth for a missing table and a fabrication for a locked one.
  Also recorded on [[TASK-276]]: one unidentified `MSSql` failure (1 of 111) during the eleven-suite
  sweep, identity not captured, 5 subsequent isolated runs clean.

### SQLite databases now run on WAL, which closes the schema-escape thread (2026-09-02)

TASK-296, the remedy for the mechanism [[TASK-290]] named. Every Birko SQLite database ran on SQLite's
rollback journal, where a statement on a **pooled** `sqlite3` handle can be answered from a schema image
older than a `CREATE TABLE` another connection has already committed — a freshly created table reads as
missing, and since TASK-285 answers that with `0`, silently. `SqLiteSettings.JournalMode` now defaults to
`"WAL"`. Verified with `BIRKO_REQUIRE_LIVE` set against live PostgreSQL 16 and on-disk SQLite:
**1,269 tests, 0 failed, 0 skipped** across eight suites; the SQLite suite is 4 of 4 clean on repeats and
identical with `BIRKO_STORM` set. The standing rule is in § Conventions. Seven things worth carrying:

- **The storm's verdict was the opposite of the truth on the axis that decides.** Both candidate remedies
  looked free there — `Pooling=False` ran 2.4× *faster*. On the ordinary case (warm, sequential,
  200 × write+count+read) it is **1.52× slower**: 2,731 ms against 1,801 ms. WAL is **5× faster**
  (351 ms) *and* removes the defect *and* keeps pooling. A benchmark taken under the pathology measures
  the pathology.
- **Only WAL is a persistent journal mode, and that shaped the API.** Measured: `TRUNCATE`, `PERSIST`,
  `MEMORY` and `OFF` are per-connection — a new connection reports `delete`. The seam applies the PRAGMA
  once, on its own connection, so accepting those four would take the value and silently do nothing. They
  are refused with the reason; the whitelist is `WAL` and `DELETE`, the latter being the persistent way
  back out of WAL.
- **The value is whitelisted because it is a bare keyword in statement position** — `PRAGMA journal_mode=…`
  takes no parameter, so refusal is the only containment. § Conventions' identifier family at a fourth
  kind of sink.
- **A journal mode that cannot be applied is recorded, not thrown** (`JournalModeInEffect` /
  `JournalModeFailure`). WAL needs shared memory and does not engage on most network filesystems, and
  SQLite reports the mode in force rather than failing — so that property must be **read**, not assumed.
- **The control pair now includes a rollback-journal variant that still fires**, which is what stops the
  new default's 0 being a broken reproduction. Mutation: putting the default back to `DELETE` reds 3
  guards **and the storm produces 15 escapes**.
- **⚠ Two fixture faults of mine, both instructive.** The opt-out test used `TRUNCATE` as its distinctive
  marker and was flaky twice — a mode that does not persist cannot be a marker, and it only ever passed
  because pooling handed back the same handle: the task's own mechanism, inside its own test. And
  *"an explicit DELETE is honoured"* asserted `delete` on a **fresh** database, where delete is the
  default, so it passed however the code behaved.
- **PostgreSQL measured clean** (0 escapes, 60 cold tables × 3 callers, 2 of 2) — the mechanism has no
  analogue on a server-side transactional catalogue. ⚠ That was only askable because of [[TASK-295]]:
  before it, `TablesCreated` was empty there and the same run would have reported 0 for the wrong reason.
  MySQL and SQL Server deliberately not measured, and said so.

### The schema-ensure escape is named: a pooled connection answering from a stale schema image (2026-09-02)

TASK-290, the open half of consumer Symbio's TASK-602, closed after nineteen hypotheses. **A statement on a
pooled `sqlite3` handle is answered from a schema image older than a `CREATE TABLE` that another connection
has already committed.** Reproduced in this repo, observed synchronously, and isolated by a single-variable
control. Verified `Birko.Data.SQL.SqLite.Tests` **310 passed**, 0 failed, 0 skipped, both with and without
`BIRKO_STORM` set. The standing rule is in § Conventions. Six things worth carrying:

- **The ingredient was the caller topology, not the volume.** Round 1's 200-tables-at-once storm produced
  6-7 `SQLite Error 5` per run and **0** escapes — more contended than the condition, whose own evidence
  had `Error 5 = 0`. Waves of 24 tables × **3 callers per table** fire on **7 of 7** runs (2-9 escapes
  each). A caller released from another's `_initLock` reads a table whose create is milliseconds old.
- **The control is one variable and needed no framework change**, because
  `SqLiteSettings.GetConnectionString()` is virtual: `Pooling=False` gives **0 of 4** runs against 7 of 7.
  It also runs **2.4× faster** (16 s against 38-40 s), which is the opposite of the usual assumption.
- **`presentNow=True` on every escape.** `OnSchemaEscapeDetected` fires synchronously inside
  `EnsureSchemaAndReport`, so a handler opening its own connection can ask `sqlite_master` while the
  failing flow is still on the stack. The table is in the file — so this is a stale read, which killed the
  entire "something removed it" family in fifteen lines. Round 1 had planned three new public probe fields;
  the one that mattered was reachable from an event that already existed.
- **It matches the consumer's signature on every recorded axis:** all `SELECT count(*)`; created→missing
  windows of 19-30 ms against its 28-66 ms; **zero** thrown failures, so each escape served a silently
  wrong `0`; and no `SQLite Error 5`. Fixed-width probe names mean TASK-293's substring channel cannot
  account for any of it — designed in before that fix existed.
- **⚠ What is not measured is said so.** The internal reason inside SQLite/Microsoft.Data.Sqlite is not
  established, and a raw-driver probe with the framework's shape did **not** reproduce it in 200 creates —
  that negative result is kept in the tree so the next attempt does not repeat it. Two further hypotheses
  died here: an unmapped entity's schema-ensure does no-op silently but annotates as benign (and then
  throws `NullReferenceException`, so it is loud); and a boundary holding an uncommitted create gives a
  concurrent reader `SQLITE_BUSY`, never the uncommitted image — measured, where Round 1 had only reasoned.
- **The remedy is filed, not taken.** `Pooling=False` is one line and removes the defect here, but it
  changes the shipped connection behaviour of every SQLite consumer on the strength of one workload on one
  machine. [[TASK-296]] (P1) owns it with the numbers, with WAL named as the alternative that might keep
  pooling, and with the other three providers to be checked now that TASK-295 makes their escape channel
  work at all.

### A vanished table healed on SQLite and nowhere else, because the recording sat in the virtual method (2026-09-02)

TASK-295, found hours earlier while writing [[TASK-293]]'s per-provider tests and worked next because the
window is closing: `RecordTableCreated` was called from the **virtual**
`AbstractConnector.CreateTable(string, fields)`, which PostgreSQL, MySQL, MSSql and TimescaleDB all
override. So `TablesCreated` was permanently **empty on four of five connectors**, and with it TASK-286's
annotation, TASK-287's `SchemaEscapes` channel and TASK-288's healing — **a table that vanished beneath an
initialised store never healed and every write threw until the process restarted**, the consumer-reported
outage TASK-288 closed, still open everywhere but the one provider the consumer runs. Fixed with a
non-virtual wrapper around a new `protected virtual CreateTableCore`. Verified with `BIRKO_REQUIRE_LIVE`
set against live PostgreSQL 16, MySQL 8.4, SQL Server 2022, TimescaleDB 2/PG16 and on-disk SQLite:
**1,579 tests, 0 failed, 0 skipped** across eleven suites. The standing rule is in § Conventions. Six
things worth carrying:

- **TASK-286's own comment stated the defect as a reassurance:** *"every CreateTable overload funnels here,
  which is why this is the one place it needs to go."* The overloads did; the **providers** did not. A
  funnel claim has to name which set it is a funnel over.
- **Step 0 priced both placements before anything was written**, and the obvious one loses on the odd
  caller: recording in the `IDictionary` dispatcher covers every override and silently drops
  `SqlSchemaBuilder`, the single external caller that reaches the leaf directly. That mutation reds the
  migration test and **leaves every provider suite green at full count** — the wrong choice would have
  looked correct exactly where anyone would have looked.
- **The signature change was measured, not assumed:** 0 overrides and 0 Birko-connector subclasses across
  all 16 consumer repos. And the break direction is the loud one (`CS0506`) — § TASK-278's silent-orphan
  hazard is about *adding* a parameter, not removing `virtual`.
- **The structural pin earns its place because the regression is invisible offline.** Making the wrapper
  virtual again reds one reflection test and nothing else; without it, restoring the old shape is noticed
  only by a live per-provider run three suites away.
- **Unbypassability is demonstrated, not claimed** — a test connector that overrides the emitter, which is
  the exact shape that used to skip the recording, records anyway.
- **⚠ A degraded create is still a create.** TimescaleDB records a failed hypertable conversion rather than
  throwing precisely because the plain table is committed and usable (TASK-254's licence), so the create is
  a fact and is recorded. Otherwise a table that later vanished would read as benign on exactly the entities
  that already have a schema problem. Asserted live on the Guid-keyed shape that cannot be converted.
  **Symbio needs no change:** it already subscribes to `OnSchemaEscapeDetected`, so the channel simply
  starts working if it moves to PostgreSQL — which is why this was done before the move rather than after.

### The escape channel fabricated anomalies, and on three providers it never fired at all (2026-09-02)

TASK-293, the highest-value item [[TASK-290]]'s Round 1 left queued: the discriminator for *"a table this
connector created, reported missing"* was a **substring search over the statement**. Two false positives,
both measured before a line changed, and the second needs no unlucky naming — a recorded `Movement` makes
a first touch of `StockMovements` read as the anomaly, and a statement naming two tables (one created, one
not) reads as the anomaly on the strength of the created one, which is the ordinary shape of a view or a
multi-type count. Fixed by asking the provider's own error, which names the table that is actually missing
and names only that one. Verified with `BIRKO_REQUIRE_LIVE` set against live PostgreSQL 16, MySQL 8.4,
SQL Server 2022 and on-disk SQLite: **1,325 tests, 0 failed, 0 skipped** across six SQL suites, plus 244
in five adjacent suites needing no server. The standing rule is in § Conventions. Six things worth
carrying:

- **The comment that licensed the looseness had expired.** It said a false positive *"costs one extra line
  in an exception nobody sees"* — true when TASK-286 wrote it, false from TASK-288 on, because the same
  answer now drives `SchemaGeneration` and so invalidates the remembered init of **every** store on the
  connector. Under load that is a positive feedback loop, i.e. the profile TASK-290's trigger has.
- **It re-reads the consumer's evidence.** Three of the eight tables in Symbio's storm evidence sit on a
  substring relation (`Movements`⊂`StockMovements`, `Reservations`⊂`StockReservations`,
  `Events`⊂`AlarmEvents`), so up to 5 of its 12 escapes may have been fabricated. Ranking this ahead of
  another storm cycle was the point.
- **Extract around the quotes, never around the English** — PostgreSQL and MySQL localise the prose and
  never the identifier, and a silent non-match here disables TASK-288's healing rather than announcing
  anything. And gate the extractor on `IsMissingTableException`, or PostgreSQL's statement-shaped `42P01`
  (`missing FROM-clause entry`) hands back a relation that exists perfectly well — TASK-211's narrowing,
  inherited rather than re-derived.
- **The fallback is kept and its trigger is pinned.** Answering "not the anomaly" when a wording cannot be
  parsed is tidier and worse: a store whose table really vanished would stay broken for the life of the
  process, silently. Erring toward re-running is the asymmetry `CanRememberInitialization` records.
- **⚠ Writing the per-provider tests found something bigger: the whole apparatus is SQLite-only.**
  `RecordTableCreated` is called from exactly one place — the **base** `CreateTable(string, fields)` — and
  PostgreSQL, MySQL and SQL Server each override it without recording, TimescaleDB inheriting PostgreSQL's.
  Measured live on all three: `TablesCreated` empty, `SchemaEscapes` empty, `SchemaGeneration` 0 even for a
  table the connector created and that was then dropped. So TASK-286's annotation, TASK-287's channel and
  TASK-288's healing are all inert off SQLite — which is the only provider the consumer runs today, and
  TASK-256 records that a move to PostgreSQL is expected. Fifth instance of *"a funnel with four overrides
  is not a funnel"*. [[TASK-295]] (P1) owns it, and each provider suite pins the gap with an instruction
  not to fix it by adding a fourth copy of the call.
- **Mutations, disjoint:** revert to the substring scan → 2 of 5 red, exactly the false-positive pair with
  both true positives green; ungate the PostgreSQL extractor → 1 live test; remove the qualifier strip →
  MySQL red and **MSSql green**, since its message carries no qualifier, so the two are not
  interchangeable evidence. Also filed: [[TASK-294]] (P2) — a count that hits lock contention is a 500 on
  every provider, which the storm reproduced 6-7 times per run while TASK-285 exempts only a *missing*
  table.

### A rolled-back schema-ensure was still remembered on one of the two transaction doors (2026-09-02)

TASK-292, found while working [[TASK-290]] — the open half of consumer Symbio's TASK-602, which asks why a
statement reports a table missing that this connector created while the store's init gate had passed.
TASK-244's acceptance demanded one answer for both transaction doors; it landed on one. The per-store door
(`SetTransactionContext`) publishes its scope *inside* `InitCore*` and withdraws it on the way out, while
`AbstractAsyncStore` evaluates `CanRememberInitialization` **after** that method returns — so
`DdlSurvivesRollback`'s `AmbientTransaction == null` term answered `true` about a create still sitting in a
caller's open transaction. Verified with `BIRKO_REQUIRE_LIVE` set against live **PostgreSQL 16**,
**MySQL 8.4**, **SQL Server 2022** and on-disk SQLite: **1,415 tests, 0 failed, 0 skipped** across nine
suites — SqLite 298 (287 → 298), PostgreSQL 93, MySQL 98, MSSql 108, `Birko.Data.SQL` 655,
Migrations.SQL 53, InMemory 69, JSON 23, XML 18. The standing rule is in § Conventions. Six things worth
carrying:

- **It manufactures TASK-290's signature on a legitimate path, with no `DROP` and no concurrency** —
  recorded `CREATE TABLE`, init gate passed, table absent. The next count answered `0` with one
  **anomalous** escape recorded and `SchemaGeneration` 0 → 1; the next write threw with TASK-286's
  annotation. A framework able to produce its own alarm condition will have that alarm misread.
- **⚠ And it is NOT Symbio's mechanism, which is the honest result rather than the convenient one.** Symbio
  reaches transactions through `SqlTransactionBoundary` → `SqlUnitOfWork` (the ambient door, unaffected)
  and its own tests explicitly reject `SetTransactionContext` for a singleton store. TASK-290 stays open.
- **Not SQLite-specific, and that was measured on three servers.** Reverting reds the new test on SQLite,
  PostgreSQL and SQL Server and leaves **MySQL green**, because its DDL commits itself so remembering is
  correct there (TASK-243). The green side is asserted, or the fix is indistinguishable from a blanket
  "never remember" — which is the mutation that reds 4 tests including the steady-state control.
- **Four hypotheses about the consumer's escape were killed by measurement**, in probes now in the tree: a
  committed create is immediately visible to an already-open connection (so no stale pooled schema cache);
  a hot journal does undo a create but the commit **fails loudly with Error 10**, so nothing gets recorded;
  a reader inside an open read transaction **blocks** the writer's commit rather than reading past it, so a
  committed create cannot be invisible; and the failure-versus-classification race cannot span the
  consumer's 28-66 ms windows. The one baseline that *is* confirmed: an **uncommitted** create reads as
  `SQLITE_ERROR 1: no such table`, never `SQLITE_BUSY`.
- **Two of the consumer's readings were corrected.** "All twelve escapes are counts" is an artefact of the
  instrument — both reader paths swallow a missing table at the reader, so a `SELECT` can never reach the
  channel and only counts and writes are visible. And `Error 5 = 0` is not evidence of no contention:
  `Default Timeout=30` absorbs BUSY up to that ceiling, so the storm's Error 5 failures arrive only after
  ~30 s of waiting.
- **⚠ The 200-table storm reproduces contention, not the anomaly** — `created=200`, `escapes=0`, and 6-7
  `SQLite Error 5` failures per run. It is in the tree opt-in behind `BIRKO_STORM` with a non-gated
  positive control, because a diagnostic that saturates the disk reds its neighbours: adding three classes
  that each call the project's idiomatic process-wide `SqliteConnection.ClearAllPools()` in `Dispose()`
  took the suite from 6/6 clean to 1-2 failures per 6 runs, and removing those three calls restored it.
  That dose-response is recorded on [[TASK-276]], whose leading hypothesis was killed in **isolation** and
  reproduces at **suite scale**.

