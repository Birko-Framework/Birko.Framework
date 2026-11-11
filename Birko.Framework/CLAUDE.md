# Birko Framework

Modular .NET framework with data access, communication, AI, and model infrastructure. General-purpose across enterprise back-office, e-commerce, presentation/CMS, desktop, IoT, and real-time domains.

See also:
- [CLAUDE-conventions.md](CLAUDE-conventions.md) — **the rulebook (67 rules with their measurements); § Conventions below indexes it and is not a substitute for it**
- [CLAUDE-projects.md](CLAUDE-projects.md) — Full project catalog
- [CLAUDE-maintenance.md](CLAUDE-maintenance.md) — Maintenance guidelines, new project checklist, solution registration
- [CHANGELOG.md](CHANGELOG.md) — Historical architectural changes
- [README.md](README.md) + [docs/](docs/) — User-facing documentation

Each project has its own `CLAUDE.md` at `Birko.{ProjectName}/CLAUDE.md` (tests: `tests/Birko.{ProjectName}.Tests/CLAUDE.md`) with project-specific details.

## How we work — feature lifecycle

Real work flows through a repeatable lifecycle so it stays **tracked, testable, reviewable, and visible to stakeholders** (the maintainers of the consumer solutions — Symbio, DraCode, FisData). Don't jump straight to code for anything non-trivial.

```
idea ─▶ prototype ─▶ decisions ─▶ tasks ─▶ human-test ─▶ review
```

1. **Capture + grill the idea** — `/feature new`. The idea is interrogated until every branch is resolved; each branch becomes a *decision*.
2. **Prototype for stakeholders** — `/feature prototype`. A clickable HTML mockup, a markdown wireframe, or a code spike — whatever lets a stakeholder react to something concrete.
3. **Record decisions** — `/feature decide`. Every decision gets a state: **approved / deferred / changed / removed**, with rationale, date, and who decided. The ledger in `docs/features/FEATURE-NNN/decisions.md` is append-logged — we keep *why* things changed, not just the current value. This is for stakeholders to see the state of the work, not only for devs.
4. **Decompose into small tasks** — `/feature decompose` → `/tasks new`. Approved/changed decisions become atomic, independently-completable tasks under `tasks/`. Each task links back via `feature: FEATURE-NNN`. **This step comes before writing implementation code, always**.
4b. **Entering an existing feature** — `/feature pick FEATURE-NNN`. It resolves what stage the feature is really at and offers the verb that unblocks it: undecided rows → `/feature decide`; **approved rows with no tasks → `/feature decompose`**; all tasks done → `/feature review`. It hands off to `/tasks pick --feature FEATURE-NNN` only once real tasks exist — you never start implementing straight out of a feature folder.
5. **Make it testable** — every task carries a `## Human test plan` (in the TASK file). When unit/AI tests can't cover it (UI/UX, edge cases, system integrations), write concrete manual steps a tester can follow. Mark `N/A — fully covered by automated tests` only when that's genuinely true.
6. **Stakeholder status** — `/feature status` regenerates a plain-language rollup for the maintainers of the consumer solutions — Symbio, DraCode, FisData: decision counts, build progress, what's testable, where it stands.
7. **Review gate** — `/feature review`: a **completeness** check (every decision built + every task merged — code correctness was already reviewed *per task* at `/tasks close`, the merge gate) + verify every human-test plan was run + stakeholder sign-off. A feature is "done" only when all three pass; it's a completeness gate, not a wholesale code re-review. **Automated tests passing ≠ the feature works** — exercise the change in the real runtime (run the app / hit the endpoint / load the UI) before calling it done; if you only ran tests, say "verified headlessly, sign-off pending" rather than claiming it works.
8. **Changing something already shipped** — a later change to a `done`/signed-off feature is still a recorded **`changed`** decision, logged in the feature that *owns* the behavior (re-home if you hit it elsewhere), with the ripple traced (downstream constants, comments, other features' decisions). If the change has a **human-verifiable surface** (visual/UX/feel), **preview it first** — show a mockup or 2–3 options and get the user's pick before editing live files (a request that reads like "a small fix" is still a change to a stakeholder-facing surface) — then, when the change is already in the code, that feature reverts to `review` and its implementing task to `verify` until re-confirmed; when it is not built yet, the feature reverts to `idea`, the old task stays `done`, and the change arrives as new work through `/feature decompose`. A built **tests-only** change stays `done`. After the revert, run `/feature status FEATURE-NNN` — it touches four surfaces (`idea.md`, the task file(s), `status.md`, the features index) and isn't complete until all four agree. Never let an unverified change keep reading as done.
9. **Spec check at story close** — `docs/specs/` holds capability specs **generated from the code** (`/specs regen`), never hand-written. Closing a story (`/tasks close STORY-NNN`) offers a scoped `/specs regen` over the areas the story touched; the resulting spec **diff** is reviewed as a "was this behavioral change intended?" check. An unexpected spec diff at story close is a finding, not churn. (Needs `docs/specs/.map.yml` — run `/specs init` once the project has real code.)
10. **Production feedback re-enters the pipeline** — work flows back, not only forward. A field signal (user report, incident, monitoring alert) is triaged like any new work: a **regression** → a `tasks/` bug that ships **with a regression test** so it can't recur; a **missing capability** → a new feature; evidence that a `removed`/`deferred` **decision** was wrong → reopen it (`/feature decide` — a new `proposed` row linking the old). `done` is never the terminus; `done → field → triage → …` is the cycle.

**Ground truth & altitude — what gets recorded where.** `docs/BRIEF.md` stores the user's requests **verbatim**: the opening ask is **immutable**, and later requirement-changing requests are **appended verbatim** (dated, naming the feature they became). Every distilled doc (this file, `README.md`, each `idea.md`) reconciles *against* it — if they disagree, the brief wins (or the divergence is a logged decision). Record each change at the altitude that fits it:

| The change… | …is recorded in |
|---|---|
| introduces/alters a **requirement or scope** (what we build) | a `docs/BRIEF.md` amendment → new/changed feature |
| alters **how an already-requested feature looks/behaves** | that feature's `decisions.md` (a `changed` decision) |
| is a pure **implementation detail** (naming, data structure) | code / commits / `docs/architecture.md` |
| settles **what a word means here** | `docs/glossary.md` — vocabulary only, never decisions |
| settles **why we chose it**, for a choice that is hard to undo | a record under `docs/adr/` |

**The last two rows are `/domain`'s.** `docs/adr/` already holds its records; `docs/glossary.md` does not
exist yet and is written on the **first real term** — not scaffolded, because an empty glossary claims the vocabulary
was examined and found thin. Run `/domain` when you hit either; it decides whether a choice has earned a record, so
don't pre-judge that here.

Append to `BRIEF.md` **only** for the top row — a refinement of already-requested scope is a *decision*, not a brief amendment. Recognizing "this is new scope, not a tweak", and capturing the verbatim text *before* paraphrasing it into a feature, is the agent's job, not something to wait to be asked.

**Why this exists:** tracking must go beyond a changelog of code. We track *decisions and their reasoning*, decompose into small verifiable units, and keep stakeholders informed of state throughout — not just at the end.

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

Day-to-day API conventions are below. **The rulebook proper — 67 rules, each with the measurement that
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
66. A retry re-runs a write only when the server says it applied nothing, or when nothing was sent
67. In an ASP.NET host the client address is decided once, by `AddBirkoForwardedHeaders`, and nothing reads the forwarded headers a second time

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

## Commands

- **Build:** `dotnet build Birko.Framework.slnx --nologo`, from this directory — the command `build-and-test.yml` runs.
  `build-and-test.yml` and `wiki-snippets.yml` skip a push that touches only `tasks/`, `docs/` or `*.md` (TASK-810;
  `Birko.Xaml.Avalonia/PARITY.md` is read by a test and re-included). A test that starts reading a doc must be
  re-included in both triggers there, or its change will not be built.
- **Test one project:** `dotnet test --nologo` from `tests/Birko.{Project}.Tests/` (CI loops it over every `tests/*/`
  that has a `.csproj`).
- **Consumer smoke harness:** `dotnet run --nologo` from `Birko/Consumers/Birko.Sandbox` — the only check that the
  framework still composes through `$(BirkoSrc)` + `.projitems`.
- **Spec links:** `dotnet run spec-links.cs` from this directory after changing `docs/specs/.map.yml` or adding a project; `-- --check`
  only reports (TASK-226).
- **Live database suites:** set `BIRKO_PG_HOST`, `BIRKO_MSSQL_HOST`, `BIRKO_MYSQL_HOST` (and `BIRKO_REQUIRE_LIVE=1` to
  fail rather than skip) per `.github/workflows/live-tests.yml`.

## Recent Updates
### Rules give one answer for null, `Like` and enum/Guid values, wherever they run (2026-11-11)
[[TASK-882]] (the user's decisions). A rule comparing a non-nullable column with null, a `Like`, or an enum/Guid value
given as text answered differently in memory, compiled and in SQL (up to 331 of 693 measured cases per provider). All
three now give the C# answer; rule string operators ignore case exactly (`OrdinalIgnoreCase`) on every runner, SQL
included; a `Like` with an interior `%` is refused by name ([[TASK-915]] to support it).
### InfluxDB request timeout is a setting (2026-11-11)
[[TASK-908]] (the user's decision). Every InfluxDB read or aggregate over ~100k points failed at the client's fixed 10 s
timeout (measured: 10.05 s); reading 100k points takes ~50 s because each entity is its own series ([[TASK-910]]). New
`Settings.RequestTimeout` (default: the URL's `timeout`, else 10 s; the consumer's value wins), applied at one funnel;
a value that breaks the client, or one set alongside a URL timeout, is refused.
### SQL GROUP BY on a string makes the groups C# makes (2026-11-11)
[[TASK-911]]. SQL Server and MySQL grouped strings by collation, silently merging keys C# keeps apart (`Zebra`/`zebra`,
`ß`/`ss`, `"a "`/`"a"`; 20 keys became 13 and 14, counts and sums added across them; measured), in store aggregates and
SQL views. GROUP BY now adds the exact form of each string key on every SQL provider, so each group returns its own text.
Cost: an indexed SQL Server key went from ~20 to ~170 ms on 100k rows. **Breaking:** the migrations `ViewSqlGenerator`
refuses a string-grouped aggregating view without a connector.
### Aggregate rows ordered by a string are ordinal on every provider (2026-11-11)
[[TASK-909]] (the user's decision). Ordering aggregate results by a string group key or string MIN/MAX followed the
machine's culture in memory and the collation on SQL: 28–56 of 56 cases wrong per provider (measured; `apple` before
`Banana`, PostgreSQL NULL last). It now follows `AggregateQuery.StringComparison` (default `Ordinal`) everywhere, also in
SQL views; SQL refuses other orders by name. MongoDB aggregates ignore a collection's default collation. Found:
[[TASK-911]] (P1, SQL Server and MySQL GROUP BY merges string keys).
### Aggregate string MIN/MAX are ordinal on every provider (2026-11-10)
[[TASK-907]] (the user's decision). String MIN/MAX followed each database's collation and the in-memory oracle the
machine's culture: 22–30 of 32 cases differed per provider (measured; `apple` vs `Banana`, ß = ss, `a` = `a `). New
`AggregateQuery.StringComparison`, default `Ordinal`, answered exactly by every provider (Elasticsearch and Cosmos no
longer refuse); any other order works in memory and is refused by name elsewhere. MongoDB enums stored as text now take
MIN/MAX by value. Migration-generated SQL views render through the connector's dialect.
### Cosmos DB `TimeSpan` filters answer as C#; one DateTime key producer (2026-11-10)
[[TASK-905]]. Cosmos compared a TimeSpan's stored text (`"1.02:03:04"` < `"23:00:00"`, `"-00:00:01"` < `"-00:00:02"`),
so 60–68 of 124 filter shapes per path answered other rows than C# (measured). Orderings now compare the sign and a
fixed-width magnitude key; equality keeps its index. TimeSpan sorts, members (`Ticks`) and arithmetic on a member are
refused by name. The DateTime wall-clock key used by filters and aggregates now has one producer.
### RavenDB view, MongoDB and InfluxDB aggregates answer as LINQ (2026-11-10)
[[TASK-906]]. RavenDB view MIN/AVG did not skip nulls (MIN of {3, null} was null) and a negative value next to a null
broke the index; the MongoDB store aggregate ignored ordering, paging and time buckets (36 of 38 cases wrong); the
InfluxDB native Flux aggregate was wrong in 24 of 27 cases (one row per stored point, buckets on write time). Raven and
Mongo now follow LINQ; InfluxDB aggregates through `AggregateHelper` (no server-side aggregation).
### Aggregates answer the same edge cases on every provider (2026-11-10)
[[TASK-886]] (the user's decisions). An ungrouped aggregate over no rows now returns one row with Count 0 everywhere
(before: no row on InMemory, MongoDB, RavenDB, Elasticsearch; RavenDB and ES threw on an empty store), and SUM over only
NULLs is 0 everywhere, like LINQ's `Sum` (MIN / MAX / AVG stay null). An empty `AggregateQuery` is refused by name
(five providers gave five answers). **Breaking:** `SqlValueFragmentDialect.Aggregate` lost its last parameter.
### Cosmos DB `DateTime` filters and sorts answer as C# (2026-11-10)
[[TASK-902]]. Cosmos compared the stored ISO text, whose fraction length varies, so inside one second `…00.5Z` sorted
before `…00Z`: 76 of 88 filter shapes and 62 of 120 sorted pages were wrong (measured), e.g. `x.T > 10:00:00Z` missed
the 10:00:00.5 document. Comparisons now use a wall-clock key behind the plain text range (index still used), and sorts
re-order rows that share a second. DateTimeOffset comparisons and members like `T.Year` are refused by name.
### SQL `OnException` subscribers run; migrations and job locks reach `OnExecute` (2026-11-10)
[[TASK-894]]. A consumer's `OnException` subscriber was never called on any provider: each provider subscribed an
always-throwing handler first (measured). The provider's handling is now an override that runs before every subscriber,
subscribers are contained (rule 25), and the caller still gets the same exception, so retry and missing-table filters
match. Statements sent by migrations, view migrations, the TimescaleDB migration and the job lock provider are now
announced. **Breaking for connector subclasses:** `InitException` is no longer virtual.
### Long string IN lists split; rules and view joins compare strings exactly (2026-11-10)
[[TASK-895]] (the user's decisions). A string IN list past the parameter limit is now split like other lists, since
TASK-873 made string IN exact. SQL rules compared strings by collation (MySQL 30, SQL Server 36 of 42 shapes wrong) and
`RuleEvaluator` ignored case; both are now ordinal, like lambdas. String view joins returned extra case- and
space-twin pairs on MySQL and SQL Server; they are exact and keep the index. `string.Equals(.., Ordinal)` renders like
`==`.
### SQL integer arithmetic fails where checked C# would, on every provider (2026-11-10)
[[TASK-890]]. MySQL computed `int` arithmetic in BIGINT, so overflowing filters matched rows and a SET stored the
unwrapped value, silently; PostgreSQL and SQL Server dropped widening casts and failed `(long)x.Hits * 2` where C#
succeeds; `Math.Abs` at the minimum answered (measured). Every provider now fails a statement exactly where checked C#
would. A narrowing SET translates on SQLite, and a fold works on a persistent view. **Breaking:**
`StoresIntegerOverflowLoudly` is replaced by `NarrowedInteger`.
### Cosmos DB: missing members read as their default, nested predicates and long null lists work (2026-11-10)
[[TASK-899]]. A non-nullable member missing from a document failed `A == 0`, `!Del` and 32 more shapes (C# reads the
default), `All(..)` threw and `Any(..)` skipped null members, and a long `Contains` list holding null was refused
(measured). Each now matches C#. View aggregates got the store's decimal and DateTime rules; a view decimal AVG and a
sort by a DateTime group key are refused by name. Found: `DateTime` filters compare stored text ([[TASK-902]], P1).
### SQL and MongoDB job queues stamp a claim when they write it (2026-11-10)
[[TASK-900]]. As on RavenDB and Elasticsearch (TASK-879, TASK-884), the claim carried the dequeue's start time, so a
candidate read slower than the claim timeout made a fresh claim look abandoned and the next dequeue handed the same job
out again (reproduced on SQLite, PostgreSQL, MySQL, SQL Server and MongoDB). Symbio's 30-minute default made it rare.
### A SQL view read over a missing view throws; table-name overloads refuse reserved columns (2026-11-10)
[[TASK-891]]. A view read over a view or source table that did not exist answered 0 rows on all four providers while
its count threw (rule 26, measured): the reader's missing-table swallow, meant for tables a store heals, applied to
views, which nothing heals. Only table reads keep it now. The migrations view generator and the connector's `Table` /
table-name overloads now run TASK-872's reserved-column check; before, they read the database function or rewrote
every row.
The rolling per-change log now lives entirely in [CHANGELOG.md](CHANGELOG.md) (newest-first). Add new architectural / behavioral change notes here as `### Title (YYYY-MM-DD)` entries; **roll the oldest into CHANGELOG.md whenever the ENTRIES exceed ~10 KB** — measure it, do not eyeball it:

```sh
awk '/^## Recent Updates/{s=1} s&&/^### /{f=1} f' CLAUDE.md | wc -c
```

The project-local `/roll-birko-changelog` skill performs the roll; mind the name, because the generic `/roll-changelog` resolves user-level first (§ Skills) and does not know this file. Granular code-review-remediation progress is tracked in `tasks/EPIC-014-code-review-remediation`, not here.

⚠ **The budget counts BYTES OF ENTRIES, and both halves of that were earned.** The rule it replaces said "past ~5–8 entries" — measured 2026-09-19, **7 entries, inside that limit, were 31 KB: 52% of this file**, because an entry had grown from a paragraph to a 36–59 line essay since the rule was written. A file *just* cut from 408 KB to 50 KB (`68b26b5b`, the rulebook split) was back to 59.6 KB four commits later, reporting itself compliant the whole way — and the monorepo migration, which got the blame, had changed it by **24 bytes**. Then the first byte budget written here measured the whole **section**, counted this very paragraph as log content, and tripped on itself the moment it was saved. **A threshold counts the thing that grows — not the thing that is easy to count, and not itself.**
