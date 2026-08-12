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
