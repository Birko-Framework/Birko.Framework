# Birko Framework

Modular .NET framework with data access, communication, AI, and model infrastructure. General-purpose across enterprise back-office, e-commerce, presentation/CMS, desktop, IoT, and real-time domains.

See also:
- [CLAUDE-conventions.md](CLAUDE-conventions.md) — **the rulebook (63 rules with their measurements); § Conventions below indexes it and is not a substitute for it**
- [CLAUDE-projects.md](CLAUDE-projects.md) — Full project catalog
- [CLAUDE-maintenance.md](CLAUDE-maintenance.md) — Maintenance guidelines, new project checklist, solution registration
- [CHANGELOG.md](CHANGELOG.md) — Historical architectural changes
- [README.md](README.md) + [docs/](docs/) — User-facing documentation

Each project has its own `CLAUDE.md` at `Birko.{ProjectName}/CLAUDE.md` (tests: `tests/Birko.{ProjectName}.Tests/CLAUDE.md`) with project-specific details.

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
  + Birko.Data.Core (for Exceptions.WholeTableWriteException only — TASK-314's MigrationFilter refuses
    with the framework's one whole-table refusal type rather than inventing a per-backend one; measured
    first: all 4 consumer aggregators importing Migrations already import Birko.Data.Core)
  -> Birko.Data.Migrations (IMigrationContext, IDataMigrator, IContextualMigration, IMigration, IMigrationRunner, IMigrationStore, MigrationFilter)
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

Day-to-day API conventions are below. **The rulebook proper — 63 rules, each with the measurement that
earned it — lives in [CLAUDE-conventions.md](CLAUDE-conventions.md).** The index after these bullets carries
each rule's opening statement so you can tell whether one applies; open that file for the reasoning,
the counter-examples and the measurements. They are not optional: every one exists because the defect
it describes shipped, and several because it shipped twice.

- All stores implement: `IStore`, `IAsyncStore`, `IBulkStore`, `IAsyncBulkStore`
- All repositories implement: `IRepository`, `IAsyncRepository`, `IBulkRepository`, `IAsyncBulkRepository`
- Bulk stores support filter-based Update/Delete: `Update(filter, PropertyUpdate<T>)`, `Update(filter, Action<T>)`, `Delete(filter)`
- Use `PropertyUpdate<T>` for native platform operations (SQL SET, MongoDB $set, ES UpdateByQuery); use `Action<T>` for complex mutations
- New platform stores should override `Update(filter, PropertyUpdate<T>)` and `Delete(filter)` for native performance
- On a bulk store, `Read(filter)` returns the **collection** (`IEnumerable<T>`), not a single entity: the bulk `Read(filter, orderBy, limit, offset)` overload hides the inherited single-result `Read(filter)` from member lookup (C# only considers the most-derived type that declares the method name). Use `ReadFirst(filter)` / `ReadFirstAsync(filter)` (on `IBulkReadStore<T>` / `IAsyncBulkReadStore<T>`) for a single result, or cast to `IReadStore<T>` / `IAsyncReadStore<T>`
- Concrete stores override `protected *Core` methods (e.g., `CreateCoreAsync`, `ReadCore`), **NOT** the public CRUD methods. The base class handles lazy-init in the public wrapper
- Use protected setters for properties that derived classes need to modify
- `RemoteSettings` should be passed via `base.SetSettings()`, not constructed inline

### The rulebook — index to [CLAUDE-conventions.md](CLAUDE-conventions.md)

Search that file for the phrase to reach the full rule.

1. A destructive all-rows operation is named `*All`; only reading everything gets the short name
2. That rule is not about SQL — it is about any write whose scope can silently become "everything", in any backend
3. A scope guard tests what the statement MEANS, never whether text was produced — and an always-true term is reduced away, never rendered
4. Fifth instance of the scope-guard family, and the first where the filter is DATA rather than a predicate: a migration's JSON filter
5. A nullable filter argument means "do not filter" or it means a value — decide once, and check what the backend's query language does with null, because one dialect will silently turn it into MATCH NOTHING
6. A write that opens its own connection cannot be inside anybody's transaction — and a boundary is only as wide as its NARROWEST participant
7. Schema-ensure PARTICIPATES in the caller's boundary, and a participating schema-ensure is not remembered — because "initialised" must mean "the schema is durably there", not "I ran the DDL once"
8. A blast radius is a measurement with an expiry date, and a stale one can argue for the wrong decision in either direction
9. An exception's TYPE is a contract three mechanisms select on, so rewrap only where the rewrap earns something — and enumerate the filters before you replace an exception in flight
10. A remedy is priced on the STEADY STATE, not on the reproduction that found the defect — and a knob may only offer what the mechanism can actually deliver
11. A load defect is reproduced by matching the CONCURRENCY SHAPE, not by turning the load up — and the shape that mattered here was several callers per table, not more tables at once
12. Bookkeeping a rule depends on goes in a NON-VIRTUAL wrapper around a `*Core` seam — putting it in the virtual method means it runs on exactly the providers that did not override
13. Ask the question of the ERROR, not of the statement — and when a justification says "a false positive is harmless here", check whether that is still true
14. A durability question must be asked while the thing that makes it durable is still in scope — and a rule enforced in a base class about state a derived class publishes and withdraws is a rule enforced at the wrong moment
15. An identifier that reaches interpolated SQL is resolved against table metadata, never validated as text — and the two sinks share one lookup
16. A name that one layer CREATES and another layer READS BACK has exactly one producer
17. Quote table identifiers; never quote column identifiers
18. A qualifier resolves against a bare ALIAS, not against a quoted table — and that is what makes the rule above total instead of per-sink
19. An identifier that reaches SQL as a string VALUE rather than as an identifier must be PRE-FOLDED — the parser's case-folding never runs on it, and that is the opposite of the quoting rule above
20. Those two treatments have ONE producer each, on the connector — and the escaping underneath them has one producer for the whole framework
21. A provider whose paging syntax has a precondition needs that precondition supplied where it is KNOWN, not where the clause is rendered — and a feature nobody tested is a feature nobody has
22. A WRITE that cannot be applied must never report success — and "recover and continue" is not a thing a handler can do if it neither repairs nor retries
23. A diagnostic that rides on a THROWN exception is blind on every path that answers instead of throwing — and the path that answers is where the wrong answer lives
24. A recovery branch is only as good as the state it can actually reach — and a promise made in one class about a flag owned by another is a promise nobody keeps
25. A diagnostic channel must survive its own subscriber — and the damage a throwing handler does is not where you look for it
26. A reader that answers an ERROR with an empty result is giving a wrong answer, so what it swallows must be exactly one thing
27. A decorator never writes to an object the inner store returned, and it never persists the caller's object with a value that belongs somewhere else — the two halves are one rule, and CAPTURE-AND-RESTORE satisfies neither
28. A value that a driver INFERS a type for and a value the framework types EXPLICITLY are two producers, and the inferring one fails quietly
29. A column has ONE meaning, so a type that can mean two things needs an opt-in — and the opt-in promises only what the weakest provider can keep
30. A query against another product's catalogue has an expiry date, and nothing in the type system says so — write the version down
31. A name a CALLER supplies may be qualified; a name the framework resolved never is — and quoting the whole string conflates them
32. Birko's OWN bookkeeping tables are dialect-rendered like anything else — and the bare-column rule two entries up does NOT extend to them
33. A bare-emitted identifier has no enclosure, so its containment is REFUSAL — the third mechanism, and the guard that provides it is separated from its sibling by the MESSAGE, not by the check
34. A parameter documented as "SQL" has no containment story, so the fix is the API's SHAPE, never a validator — and where the fix needs an open set, a validated IDENTIFIER contains it without closing it
35. A statement the server refuses inside a transaction is a provider limit the framework must ROUTE AROUND, and the statements in one family will not all need the same treatment — one may be fixable in place and its neighbour not at all
36. Per-caller, per-operation state never goes on a process-wide cached object — and when the last user of such a mechanism moves off it, the mechanism goes too
37. A column type is only correct for the operations the provider allows ON it — and where a key restricts the type, "is this column an index key" is resolved ONCE, never OR'd at a connector
38. An operation that can take its tenant from more than one source resolves it ONCE, and refuses rather than picking a winner
39. Scope the read, not just the write
40. Any middleware that resolves a tenant must publish it via `ResolvedTenant.Publish(context, guid, source)` (`Birko.Data.Tenant/Middleware/ResolvedTenant.cs`)
41. A mapper that cannot express something refuses; it never drops it quietly
42. A translator that cannot express a node REFUSES it, and "nothing was produced" is never a scope answer — because the same empty state also means "constrains nothing", which is a different thing
43. A collapse that unwraps a group must COMBINE the enclosing flags, not assign over them — and the destructive guard cannot help, because the clause it produces is non-empty and simply wrong
44. A sub-translation that fails must not leave a query that MEANS something else — and a guard on the top-level result cannot see it, because the result is non-null
45. A caller's value that reaches a query LANGUAGE is contained by choosing a query type with no grammar, not by escaping the grammar
46. Where a driver has no usable default, the framework picks one — once, at a funnel, with the consumer winning
47. A language-level overload change is a framework-wide event: normalise it ONCE, wire it where it actually breaks
48. Where two layers can each define an identity, ONE of them owns it — and the tell is a silently empty result, not an error
49. Lazy schema-ensure degrades and reports; an explicit schema call throws
50. TASK-204's degrade-and-report rule has a SECOND sink, and the test for "may I degrade this?" is whether anything DECLARED it — not whether it sounds important
51. A provider without a conditional form has to fake one, and the flag that turns it off has to be honourable everywhere — otherwise it is a silent no-op wearing a parameter's name
52. A provider-specific ceiling is fixed at the provider, and "the declaration is wrong" is a claim to measure before acting on it
53. A fallback branch nobody can reach is not a safety net — it is a second implementation that drifts, and it can invalidate the tests of the first
54. A hypothesis you cannot reproduce gets FALSIFIED or recorded — never quietly adopted, and never "confirmed" by a run of green
55. A builder whose every method returns `this` has a silent option at every step — so it must honour a declaration or refuse it, never accept one and do nothing
56. A deliberately-unfixed gap is closed by the measurement it was waiting for — and the test that recorded it is inverted, not deleted
57. A constraint whose SHAPE cannot express the rule must change shape — and the change is scoped to the declarations that are actually broken
58. A constraint whose scope is "some rows" has to be DECLARED, and where a provider cannot express it each polarity is answered separately, on measurement
59. State a host reads must be current state, keyed — not an append-only log
60. `IsNullOrEmpty` on a parameter whose `null` MEANS something converts a missing configuration value into a different behaviour — and the tell is that its neighbours fail loudly on the same input
61. A test teardown that reaches process-wide state damages a PARALLEL sibling, and the victim is never the file that caused it
62. A process-wide cached object keeps attracting per-caller state, and the only thing that stops the next instance is a test
63. A check that compares DECLARED against STORED asks the schema for the stored side, never the driver — and the declared side is the method that emits the DDL

## Task tracking — this directory is the monorepo's aggregator

The framework lives in **one git repo** (`Birko-Framework/Birko.Framework`): every `Birko.*`
shared project is a top-level directory, every `Birko.*.Tests` project sits under `tests/`, and
this directory (`Birko.Framework/`) is its **aggregator** — the `.slnx`, the shared CLAUDE docs,
and the plan. This is the aggregator override the generic `tasks` skill's shape detection defers to:

- **All tasks live in this directory's `tasks/`.** There is one repo, so the walk-up-to-`.git`
  rule lands every task here by construction. An EPIC still lists the projects it touches in
  `affects:` (e.g. `affects: [Birko.AI, Birko.Data.Core]`) — that is now a *scope* annotation for
  spec regeneration, not a routing instruction.
- `docs/features/` and `docs/specs/` likewise live here, family-wide.

> **This closes a contradiction the old model carried.** § Task tracking used to prescribe filing
> single-sub-project work "in that sub-repo's own `tasks/`" while **1 of 178** sub-projects
> actually had a `tasks/` folder, and `/tasks pick`, the dashboard and [[fix-next]] all ran from
> the aggregator — so the prescribed location was filed and scheduled by nothing (recorded at
> TASK-449, deliberately left as a convention decision). The monorepo removes the split rather
> than resolving it.

### Integration model — commit to `main`, one commit per repo

`tasks/.config.yml` sets `integration: single-branch`. **This family does not branch per task**, so
`/tasks pick` offers no `task/TASK-NNN` branch and `/tasks close` skips its merge step. `done` still
means *landed on the default branch* — only the mechanism differs from the generic PR-per-task default.

**One fix is now ONE commit.** The production change, its regression suite and the task file are
paths in the same repo, so they land together:

| Path | Contents |
|---|---|
| `Birko.{Project}/` | the production change |
| `tests/Birko.{Project}.Tests/` | the regression suite |
| `Birko.Framework/` (here) | task file + spec + dashboard |

Message shape: `fix(<FINDING-ID>): <what now holds>`, with the task id in the body.

**Why this matters beyond convenience.** Under the old three-repo model a fix and its test could
not be atomic, and three things silently followed: `git bisect` on the framework ran tests from the
*test repo's* HEAD — a tree from a different day, so you were not testing the commit you thought;
CI had no way to know which test-repo commit corresponded to a framework commit; and `git revert`
of a fix left its test behind, asserting the fixed behaviour and going red for the wrong reason.
Given how much of this file rests on mutation testing — *"a revert that fails nothing is a missing
test"* — the code and the tests that measure it have to be one versioned unit. **Do not split a fix
from its test across commits.**

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
- **Tests live under `tests/` in the same repo:** `Birko.{Project}`'s tests are at
  `tests/Birko.{Project}.Tests/`, and import the project under test as
  `..\..\Birko.{Project}\Birko.{Project}.projitems`. A fix and its regression suite are **one
  commit** — see the integration model above. Run with `dotnet test --nologo` from the test project.
- Every new public functionality must have corresponding tests in `Birko.{ProjectName}.Tests`
- Test both success and failure cases; include edge cases and boundary conditions
- Each test project has its own `CLAUDE.md` describing scope and conventions
- See [CLAUDE-maintenance.md](CLAUDE-maintenance.md) for test requirements on new projects and health check patterns

## Recent Updates

The rolling per-change log now lives entirely in [CHANGELOG.md](CHANGELOG.md) (newest-first). Add new architectural / behavioral change notes here as `### Title (YYYY-MM-DD)` entries; when this section grows past ~5–8 entries, roll the oldest into CHANGELOG.md (the project-local `/roll-birko-changelog` skill does this). Granular code-review-remediation progress is tracked in `tasks/EPIC-014-code-review-remediation`, not here.

### A consumer can ship a package older than the framework, and NuGet's own guard cannot see it (2026-09-19)

[[TASK-473]] + [[TASK-474]], both from one Symbio build failure on a Linux box. The reported errors were
32 × `NU1605` from a stale `Tmds.DBus` pin — already fixed in Symbio and merely unpushed — but underneath
them sat 7 × `NU1504` nobody had chased, one of which was `Npgsql 9.*` against the framework's `10.*`. The
standing rule is in [CLAUDE-maintenance.md](CLAUDE-maintenance.md) § External dependencies; it is
deliberately **not** in [CLAUDE-conventions.md](CLAUDE-conventions.md), which holds runtime-code rules —
same placement as [[TASK-229]] and [[TASK-234]], noted so a later sweep does not read it as a skipped
promotion. Six things worth carrying:

- **⚠ NuGet already enforces this rule for free, and the `.projitems` model gives it up.** "Consumer
  declares something older than its dependency" *is* `NU1605`, an **error** by default — but `NU1605`
  compares across a **package dependency edge**, and a shared project compiled into the consumer's own
  assembly has no package identity to hang one on. So the two declarations are just two items in one
  project: `NU1504`, a warning, about *duplication*, silent on which side is older. A named cost of the
  "ship as real packages is deferred" decision, which nobody had priced.
- **The policy, settled with the user:** a consumer never declares **lower**; **higher** is allowed and it
  owns the breakage; **equal** means do not declare it at all. The middle shape is the trap — a second
  `Include` is a duplicate, not an override, so it must be `Update=`, and an `Update` placed **before** the
  `$(BirkoSrc)` imports is a **silent no-op**, because `Update` only reaches an item that already exists
  and an aggregator's imports sit at the bottom of the file.
- **Measured across all 8 importing consumers: 13 findings in 4 of them** — 3 BELOW, 2 PINNED, 8 EQUAL.
  Symbio owns 8 of the 13; DraCode 3; BardStudio and WorkoutTracker 1 each.
- **⚠ A verdict the first version of the check did not have changed a result.** Comparing *floors* called
  DraCode's `JwtBearer 10.0.0` identical to `10.*`. It is not: the framework floats so a published advisory
  heals on the next restore, and an exact pin freezes that — invisibly, when the pin sits on the floor.
  `PINNED` is not a violation, it is a decision that was living in a file as an unremarked line.
- **⚠ Two BELOW rows are [[TASK-230]]'s leftovers seen from the other end.** That task set the remedy floor
  at `Microsoft.Data.Sqlite ≥ 9.0.19 or ≥ 10.0.11` past a **High** advisory; BardStudio declares `9.0.3`
  and DraCode `9.0.4`. It recorded them as *"8 projects carry their own `SQLitePCLRaw` 2.1.10"* — consumer
  rows to report. One pin below the floor produces many advisory rows: **the audit names the line, the
  sweep names the rows.**
- **⚠ The fixture found a defect in the checker before anyone trusted it, and `audit-dependencies.ps1` had
  the same one for real.** The new script printed "could not be compared" in magenta and then a **green**
  "nothing found" underneath — the exact failure its sibling's header warns about. And reading that sibling
  to copy its discipline is how [[TASK-474]] surfaced: [[TASK-457]]'s migration rewrote `'Framework.Tests'`
  → `'Framework\tests'` with a **literal tab** for the `\t`, so the bucket silently vanished, the
  `if (-not $buckets) { throw }` guard could not fire because `Consumers` still resolved, and the sweep has
  been reporting **81 of 248 projects** as a whole-tree result since 2026-09-18. Six occurrences in three
  files, two of them in the scaffolding skills that create test projects. **A guard for "none" is not a
  guard for "fewer than asked for."**

### The framework is one repo: 349 repos consolidated, with every commit preserved (2026-09-18)

[[TASK-457]], closing the open question `docs/adr/ADR-001` (the former untracked
`WORKSPACE-STRUCTURE.md`) left in June. The family was **365 git repos** on a personal account; the
framework is now `Birko-Framework/Birko.Framework` — 178 projects at the root, 167 test projects under
`tests/` — beside `Birko-Framework/Birko.Web` and `Birko-Framework/Birko.Sandbox`. Consumers untouched.
**349/349 absorbed, 0 failures.** Eight things worth carrying:

- **Commit reconciliation was exact: 4,096 = 2,676 + 1,074 + 1 root + 345 merges**, and history
  survives as history — oldest commit **2019-03-15**, with `git log --follow` and `git blame`
  resolving back *through renames* (`Birko.Data.SQL/Attribute/` → `Attributes/`). `filter-repo
  --to-subdirectory-filter` then merge, 349 times.
- **⚠ The strongest argument for the migration was a defect already on file.** [[TASK-131]] measured
  that every spec area globbed *out* of the aggregator's repo while `generated-at` stamped only that
  repo's HEAD, so `/specs verify`'s staleness primitive **could never observe a source change** — not
  weak, *decorative*, with `roadmap` DV7/DV8 inheriting it. One repo fixes it outright. **A polyrepo
  had been the root cause of a filed defect for six weeks and nobody had connected the two.**
- **A fix and its test could not be atomic, and three things silently followed:** `git bisect` ran
  tests from the test repo's **HEAD** — a tree from a different day, so you were not testing the
  commit you thought; CI could not know which test-repo commit matched a framework commit; and
  `git revert` of a fix left its test behind, asserting the fixed behaviour and going red for the
  wrong reason. For a codebase whose method is mutation testing, that is the real cost.
- **The size argument was measured away, and inverted.** 178 repos ≈ 47 MB of history; the
  consolidated repo packs to **14 MB** — the 200 MB aggregator was **6,566 never-gc'd loose objects**,
  not content. So **no history rewrite was needed**: `mermaid.min.js` (3.2 MB) and the 1.5 MB audit
  file stayed, and the audit file turned out to be referenced by 8 task/story files anyway.
- **⚠ `.github/workflows/` is read only from the REPOSITORY ROOT.** The four `token-parity.yml` copies
  existed per-repo *deliberately* — *"a gate that only fires on the source cannot catch an edit made to
  the output"*, the failure that had already happened twice — and three of them became **inert files**
  the moment their repos became directories. Replaced with one root workflow per repo, four checkouts
  down to two. **When a repo becomes a subdirectory, everything that only works at a repo root dies
  silently.**
- **⚠ Deleting duplicated boilerplate ripples into the rules that mandate it.** 334 identical
  `.gitignore` and 266 `License.md` went to one root each — and that invalidated the New Project
  Checklist, two scaffolding skills, and a `verify-birko-conventions` check that asserts *every project
  dir must have `License.md` and `.gitignore`*, which would have failed **345 times**. The root
  `.gitignore` also had to keep the aggregator's `!.claude/skills/` un-ignore and an exception for the
  tracked `.code-workspace`, or it would have started ignoring files the repo deliberately keeps.
- **⚠ Windows MAX_PATH bit twice and `safe.directory` is protected-config only.**
  `--to-subdirectory-filter` doubles the name depth, so `Birko.Communication.OAuth.Providers.Tests`
  blew 260 chars under a long scratch path — git needs `core.longpaths=true` and Python's `io.open`
  fails outright. And `-c safe.directory=*` is **ignored by design**; a temp `GIT_CONFIG_GLOBAL` that
  `[include]`s the real one works and leaves the machine's config untouched.
- **⚠ 46 repos had unpushed commits, so the local disk was the source of truth, not GitHub.** The
  migration cloned from local paths. Worth stating because the instinct is to clone from the remote.
  Still outstanding by explicit decision: **433 commits in WorkoutTracker (298), Presenter (72),
  BardStudio (50) and Latent (13) are local-only with no remote.**

### A migration filter that named a field but matched nothing deleted the whole collection (2026-09-17)

TASK-314, the last high-tier `migrations` task and the fifth member of the scope-guard family — the first
where the filter is **data** rather than a predicate, so `PredicateScope` has nothing to analyse. All five
findings **CONFIRMED, 2 wider than filed, 0 refuted.** 263/263 green across 7 suites (205 before, 58 new),
plus 894 in four `Birko.Data.Core`-consuming suites to show the exception change did not ripple. **Eight
disjoint mutations.** The standing rules are in § Conventions. Nine things worth carrying:

- **`{"status":{}}` is a typo that meant "everything".** It takes the object branch in every translator and
  the operator loop adds nothing, so the clause came back empty and each caller appended its constraint
  *only when non-empty*: SQL emitted `DELETE FROM {table}` with no `WHERE`, RavenDB sent
  `FROM '{collection}'` unfiltered, Cosmos selected every document and deleted them one at a time.
  Confirmed on all four named backends.
- **ElasticSearch reached the same end state with a NON-NULL query.** `BoolQuery { Must = [] }` is
  well-formed and means match-all, so the obvious guard — refuse a null query — never fires. Third time
  this family has arrived as *a one-term thing that looks ordinary and means everything*, after `1 = 1`
  and `{ "$nin": [] }`. The discriminator has to be the **term count**.
- **One producer in `Birko.Data.Migrations`, not four copies**, guarding on each backend's own
  translation rather than re-parsing the JSON — so the guard and the emitted statement cannot disagree.
  Reusing `WholeTableWriteException` was priced first: all 4 consumer aggregators importing Migrations
  already import `Birko.Data.Core`.
- **Guard the whole verb family — including `CountDocuments`, which the finding did not name.** A count
  answering for the whole collection beside a delete refused on the identical filter is § TASK-313's
  defect. Safe to widen because the only way to mean "everything" here is the explicit `{}` door, which is
  untouched and pinned on every backend.
- **⚠ MongoDB and InfluxDB are immune by a DIFFERENT mechanism, measured and pinned rather than "fixed
  from symmetry".** Mongo hands the parsed document to the driver, where `{"status":{}}` is an exact match
  on an empty subdocument; Influx refuses a JSON filter outright (CR-M111).
- **`SH-H029` was wider: the gate one line ABOVE the filed one has the identical defect and fires first.**
  NEST's `ExistsResponse.Exists` is `HttpStatusCode == 200`, so an unreachable cluster answers "the index
  is not there". Measured — reverting only the unfiled half reds all 3 tests, i.e. fixing what the finding
  named would have changed nothing observable.
- **`SH-H033` was wider in the other direction: points are stamped with the migration's AUTHORED date.**
  So the 365-day expiry did not merely age records out — a migration authored over a year ago fell outside
  the retention window the moment it was written and was never durably recorded at all.
- **⚠ Two mutations failed ZERO, and both were my tests rather than the fix.** `SH-H030`'s suite never
  reached the swallow: `GetAppliedVersions` opens with `EnsureInitialized()` → `FindBucketsAsync()`,
  **outside** the try (§ TASK-291's exact trap) — and the second attempt, which did reach `QueryAsync`
  inside the try, met a raw `HttpRequestException` that is not an `InfluxException` and was never
  swallowed either. So that swallow only ever fired for failures Influx itself *reports*, which needs a
  live server; the offline cover is now an honest source scan and both dead ends are written into the test
  file. The Raven mutation exposed the same shape: a scan asserting the guard's call site but not the
  helper's counting.
- **⚠ And `SH-H031`'s fix had to go where the state is, not where the defect is.** `IMigrationStore` cannot
  carry a session in its signature without changing every backend, so it is ambient on the concrete store
  — entered through a **self-restoring scope**, because per-caller state assigned onto a longer-lived
  object is the trap § Conventions keeps recording. Its behavioural assertion needs a replica set, not
  merely a mongod, and skips loudly rather than passing when it finds one.

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
