# Birko Framework

Modular .NET framework with data access, communication, AI, and model infrastructure. General-purpose across enterprise back-office, e-commerce, presentation/CMS, desktop, IoT, and real-time domains.

See also:
- [CLAUDE-conventions.md](CLAUDE-conventions.md) — **the rulebook (65 rules with their measurements); § Conventions below indexes it and is not a substitute for it**
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

The full layering graph moved to [CLAUDE-projects.md](CLAUDE-projects.md) § Dependency Flow, beside
the catalog it describes — **that file is the one producer; edit the graph there, not here.** Consult
it before adding any project reference: the graph is what says whether an edge is legal, and a leaf
documented as "zero deps" is a promise consumer aggregators rely on.

### Reference Implementations
- **ElasticSearch** store — reference for async/bulk operations
- **JSON** store — reference for file-based storage
- **XML** store — reference for file-based storage with `System.Xml.Serialization` (note: no native `Dictionary` support — use wrapper types)
- **InMemory** store (`Birko.Data.InMemory`) — simplest possible store (thread-safe `ConcurrentDictionary`, no persistence); the canonical test double / prototyping backend

## Usage in Consumer Solutions

When using Birko.Framework projects in your solution, create **one or more aggregator library projects** that bundle the `Birko.*` shared projects you need (e.g. `FisData.Birko`, `Symbio.Birko`, or split by layer like `{Solution}.Birko.Core` + `{Solution}.Birko.Edge` + `{Solution}.Birko.Ai`). Your other projects reference the aggregator(s) instead of importing `.projitems` directly. Default to a single aggregator; split only when concrete pain shows up — bloated binaries, leaky transitive deps, or unused-heavy-dependency pull-ins (camera, AI, hardware). This avoids compilation and transitive reference issues that arise when multiple projects import overlapping sets of shared projects independently.

Use `$(BirkoSrc)` (resolved from a root `Directory.Build.props`) for all `Import Project="…\Birko.X\Birko.X.projitems"` paths instead of hard-coded absolutes. The property reads `/p:BirkoSrc=…` first, then the `BIRKO_SRC` environment variable, then defaults to the `Birko\Framework` checkout relative to the consumer repo (the recommended layout nests the framework under a `Birko\Framework` bucket with consumers under a sibling `Birko\Consumers`, so the default resolves `..\..\Framework`; flat-sibling checkouts would use `..`). TypeScript bundlers consuming `Birko.Web.*` sources resolve a **separate `Birko\Web` bucket** — the frontend libs (`Birko.Web.Core` / `.Components` / `.Shell`) live there, apart from the .NET `Birko\Framework`. Their `build.js` walks up to find `Birko\Web` (or honors `BIRKO_SRC`). So the two builds resolve sibling buckets: `Birko\Framework` for MSBuild, `Birko\Web` for esbuild. See [README — Usage in Consumer Solutions](README.md#usage-in-consumer-solutions) for the full pattern.

## Conventions

Day-to-day API conventions are below. **The rulebook proper — 65 rules, each with the measurement that
earned it — lives in [CLAUDE-conventions.md](CLAUDE-conventions.md).** The index after these bullets carries
each rule's opening statement so you can tell whether one applies; open that file for the reasoning,
the counter-examples and the measurements. They are not optional: every one exists because the defect
it describes shipped, and several because it shipped twice.

- All stores implement: `IStore`, `IAsyncStore`, `IBulkStore`, `IAsyncBulkStore`
- All repositories implement: `IRepository`, `IAsyncRepository`, `IBulkRepository`, `IAsyncBulkRepository`
- Bulk stores support filter-based Update/Delete: `Update(filter, PropertyUpdate<T>)`, `Update(filter, Action<T>)`, `Delete(filter)`
- Use `PropertyUpdate<T>` for native platform operations (SQL SET, MongoDB $set/$inc, ES UpdateByQuery); use `Action<T>` for complex mutations. A counter is `Increment` / `Decrement`, never a read-modify-write `Action<T>`
- New platform stores should override `Update(filter, PropertyUpdate<T>)` and `Delete(filter)` for native performance — translating every `PropertyAssignment` through `Match(set, increment)`: an increment is `col = col + delta`, never a Set of the delta. A kind that must not be mishandled is a closed hierarchy reached through `Match`, not an enum beside a shared `Value` (TASK-498)
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
64. DDL that names something only a registration provides makes every connection a participant — the connector is its one producer, and any other connection must register or it fails
65. A denied action answers 403 when the caller may read the resource and 404 when they may not — and "unauthenticated" is 401, decided before anything is looked up

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

`tasks/.config.yml` sets `integration: single-branch`: **this family does not branch per task**, so
`/tasks pick` offers no `task/TASK-NNN` branch and `/tasks close` skips its merge step. **One fix is
ONE commit** — production change, regression suite and task file land together. The full rules, and
why atomicity is load-bearing rather than tidy, are in [CLAUDE-maintenance.md](CLAUDE-maintenance.md)
§ Integration model.

⚠ Three of them bite on *every* commit, so they stay here rather than one file away:

- **Stage explicitly. Never `git add -A`.**
- **No `Co-Authored-By:` trailer.** Standing preference; overrides the harness default. Don't copy it
  from older commits that carry it.
- **Do not split a fix from its test across commits.** `git bisect`, CI and `git revert` all break
  quietly when you do — see the maintenance file for the measurement.

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
design-agent) are shared user-level via [install-skills.cs](install-skills.cs)
(`dotnet run install-skills.cs` — a junction on Windows, a symlink on Linux; edit here, live
immediately).

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
### A SQL bulk write commits all of its items or none (2026-10-15)

[[TASK-629]]. Outside a transaction, `Create` / `Update` / `Delete` of a collection committed item by item, so a failure
at item k left items 0..k-1 written. It is now one transaction, rolled back on any failure or cancellation. Inside an
existing boundary it joins, as before. A collection update still writes its stored items and reports the missing ones.
### Repositories: no store, no silent success; the locator keeps one store per key (2026-10-15)

[[TASK-162]]. A repository with no store answered every call as if it had worked. It now throws (plain and ViewModel).
The locator's default key never matched its `Destroy`, ignored a second store on a cache hit, and destroyed the store
under a sibling. `AddRepository(store, lifetime)` takes only Singleton. ViewModel paged reads are ordered.
### Tenant isolation: tenant-scoped bulk writes check the caller's filter; the header guard is enforced (2026-10-15)

[[TASK-161]]. Through the tenant bulk wrapper, a null filter deleted every row of the tenant. Tenant repository
registration always built the non-bulk wrapper, and its async form accepted no async repository. **BREAKING:**
`AddBirkoSecurity` now refuses to start without `UseBirkoTenantHeaderGuard()`, unless `RequireTenantHeaderMatchesClaim =
false`. The tenant sync provider's 11 divergences are [[TASK-630]].
### Rule specifications agree with the in-memory evaluator; paging orders totally (2026-10-15)

[[TASK-171]]. A `RuleSpecification` threw for `IsNull` on an `int` or `>` on a string, matched nothing for a Guid or
enum given as text, parsed numbers in the host's culture, and rounded `10.7` to `11`. A null `Contains` matched every
row. Composites now evaluate through their operands. Paging sorts by the caller's order, then `Guid`, and never sends
a negative offset. Three engine divergences are now [[TASK-144]].
### Filtered bulk writes: an unconfigured SQL store refuses, a vanished row is skipped (2026-10-15)

[[TASK-170]]. A SQL store with no settings answered every delete, update and read as done; it now throws on first
use. An empty `PropertyUpdate` rewrote every matched row on the portable path. A row deleted mid-`Update(filter,
action)` aborted the loop halfway; it is skipped. Two `Set`s of one property are last-wins on SQL too, and a nested
`Set` is refused. Atomic bulk writes by default are [[TASK-629]].
### Schema and index metadata: 19 medium defects fixed (2026-10-15)

[[TASK-154]]. A `char` or `byte`-enum column threw on every read or write, and a get-only property broke every read.
An inherited `[IndexedField]` created the index on one table only. The index managers misreported uniqueness,
direction and other schemas. `LoadTable` returns no null (throws; `TryLoadTable` for null), and ElasticSearch index
management no longer reads a failed request as "absent". Spawned [[TASK-627]] and [[TASK-628]].
### Filter translation: 18 medium defects fixed on SQL and ElasticSearch (2026-10-15)

[[TASK-153]]. `25 > x.Age` returned the other rows, `IsNullOrEmpty` matched nothing, `StartsWith("A_1")` matched
`A21`, and a `Between` rule lost its upper bound. All are measured against real databases and fixed. Every LIKE now
declares `ESCAPE '!'`. A CASE bool test is `(col = '1')`, the form all four providers accept. An ElasticSearch store
given foreign settings throws instead of answering empty. Join rendering is [[TASK-626]].
### The event-tenant bridge refuses a tenant context it cannot see (2026-10-15)

[[TASK-328]]. With `AddTenantContext*`, the bridge read `Tenant.Current` while the app set its tenant on a separate
`TenantContext`, so every tenant event was dispatched across ALL tenants, silently. The first resolve of the bridge now
throws unless the app's `ITenantContext` is the same object. `AddBirkoSecurity` (scoped `_ => Tenant.Current`) passes.
`AllTenantsDispatches` records each all-tenants dispatch by event source. System events still widen.
### SQL attributes from another assembly keep their values (2026-10-15)

[[TASK-577]]. In the split-aggregator layout, attributes from a second compilation of `Birko.Data.SQL` are rebuilt by
name. The rebuild read `MaxLength`, `Precision` and `Scale` as properties, though they are fields, so a
`[MaxLengthField(64)]` column was unbounded and `decimal(18,4)` lost its precision. It also skipped `[UtcField]`, making
an instant a wall clock. All values are now read, and an attribute the build cannot rebuild throws.
### A SQL filter comparing two columns finds its rows (2026-10-15)

[[TASK-574]]. `x.A == x.B` (any operator) parsed both sides into one condition, the right overwriting the left with no
value bound: `B = NULL`, zero rows, no error, on every SQL provider. `x.A.Date == x.B.Date` did the same as `DATE(B) =
NULL`, and on SQL Server failed outright. Both operands are now rendered; `==` / `!=` between nullable columns are
null-safe, as C# is. A `.Date` of one column against another throws `NotSupportedException`: no portable SQL form.
### A stale SQL job is reclaimed by exactly one worker (2026-10-15)

[[TASK-575]]. A reclaim updated `WHERE Status = Processing`, the status it writes, so two workers could both reclaim and
run one job, and the Dead burial overwrote a job its slow worker had completed (both measured). Reclaim and burial now
also require `LastAttemptAt < staleBefore`.
### Birko.Health.Data.ElasticSearch: a health check for mapping drift (2026-10-15)

[[TASK-624]]. New project. `ElasticSearchMappingDriftHealthCheck` runs one `DetectMappingDriftAsync` probe per store
([[TASK-621]]): Degraded on drift or on a store it could not ask, Healthy only for what it verified. Its own project
because `Birko.Health.Data` stays dependency-free.
### Schema drift reports a declared index the table lacks (2026-10-15)

[[TASK-622]]. Drift compared columns only. Measured on all four SQL providers: a dropped index, or a unique one made plain,
read clean. `SchemaDriftReport.IndexDrifts` now reports `Missing` / `ColumnsDiffer` / `UniquenessDiffers`, read through
each provider's index manager. The health check counts them separately. Also fixed: `MSSqlIndexManager.ListAsync`
always threw on SQL Server (a `tinyint` read with `GetInt32`).
### BREAKING: Create never overwrites; a key-less SQL model is keyed by its Guid (2026-10-15)

[[TASK-567]]. SQL stored a second row with the same Guid, RavenDB overwrote, and JSON/XML bulk upserted. Every provider
now refuses a create of a stored Guid with `StoreException` ([[TASK-625]] unified MongoDB, CosmosDB and ES). A
key-less SQL model's `Guid` is its `PRIMARY KEY`. A fluent key replaces it, and declared composites are untouched.
On a TimescaleDB hypertable it is left out (user decision). **Migration** for existing tables: CHANGELOG (drift reports `KeyMismatch`).
### ElasticSearch stores report mapping drift (2026-10-15)

[[TASK-621]]. An index is mapped once and never revisited, so a model changed later queries an index mapped for the
old one. Measured: a member made `[Keyword]` over a `text` index matched nothing, silently. `DetectMappingDriftAsync()` /
`DetectMappingDrift()` compare the store's index descriptor (the one it creates the index with) against the live mapping:
`Missing`, `Unexpected`, `TypeMismatch`. The health check is [[TASK-624]].
### Schema drift reports a column whose nullability differs from the model (2026-10-15)

[[TASK-620]]. `DetectDrift` compared types only. Measured on all four SQL providers: a column made nullable where the
model says NOT NULL, or the reverse, read clean. It is now `ColumnDriftKind.NullabilityMismatch`, read from each catalogue
(`birko_nullable`) against `IsNotNull`. Key columns are skipped. **Expect new drift** on tables created by hand, or
altered with a `MODIFY` / `ALTER COLUMN` that dropped `NOT NULL`.
### An ElasticSearch `PropertyUpdate` Set of null stores null (2026-10-15)

[[TASK-501]]. The update script bound a null as `""`, so a string stored `""`, and a nullable number or date failed the
mapping: a 400 that, before [[TASK-502]], silently dropped the whole update. A null is now a literal `null`. The script
resolves its field through `StoredFieldName`, so a mapped member is written where it is read. A non-property selector
throws `ArgumentException`.

The rolling per-change log now lives entirely in [CHANGELOG.md](CHANGELOG.md) (newest-first). Add new architectural / behavioral change notes here as `### Title (YYYY-MM-DD)` entries; **roll the oldest into CHANGELOG.md whenever the ENTRIES exceed ~10 KB** — measure it, do not eyeball it:

```sh
awk '/^## Recent Updates/{s=1} s&&/^### /{f=1} f' CLAUDE.md | wc -c
```

The project-local `/roll-birko-changelog` skill performs the roll; mind the name, because the generic `/roll-changelog` resolves user-level first (§ Skills) and does not know this file. Granular code-review-remediation progress is tracked in `tasks/EPIC-014-code-review-remediation`, not here.

⚠ **The budget counts BYTES OF ENTRIES, and both halves of that were earned.** The rule it replaces said "past ~5–8 entries" — measured 2026-09-19, **7 entries, inside that limit, were 31 KB: 52% of this file**, because an entry had grown from a paragraph to a 36–59 line essay since the rule was written. A file *just* cut from 408 KB to 50 KB (`68b26b5b`, the rulebook split) was back to 59.6 KB four commits later, reporting itself compliant the whole way — and the monorepo migration, which got the blame, had changed it by **24 bytes**. Then the first byte budget written here measured the whole **section**, counted this very paragraph as log content, and tripped on itself the moment it was saved. **A threshold counts the thing that grows — not the thing that is easy to count, and not itself.**
