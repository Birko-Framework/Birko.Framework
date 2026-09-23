# Birko Framework — Maintenance Guidelines

## README Updates
When making changes that affect the public API, features, or usage patterns of any project, update its README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

## CLAUDE.md Updates
When making major changes to a project, update its CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

## Breaking changes in a shared project

⚠ **A `.projitems` shared project has no package identity, so a breaking change in one reaches
consumers with NO signal at all** — nothing to bump, no `NU1605`, no restore warning. The first a
consumer learns of it is a compile error, whenever they next happen to build. [[TASK-473]] measured
this property from the security side; this is the same property from the API side.

So the signal has to be written by hand. **A breaking change to a shared project's public or
abstract surface requires a `CHANGELOG.md` entry** naming:

- the type and member,
- the **old and new signatures**, spelled out,
- the migration — what an implementer must edit, and whether the compiler will point at it.

Updating the project's `README.md` is *not* sufficient and never was: it is a file inside the
framework, and the people who need the warning are outside it.

**Measured, [[TASK-483]]:** `6b4e374b` (2026-07-09) changed `Tool.ExecuteAsync`'s abstract signature
to take a `CancellationToken`. The framework migrated its own 10 implementers the same day. It
appeared in no changelog. **DraCode's 41 tool classes have not compiled since**, and nobody noticed
for 2½ months — the framework's CI builds one consumer, `Birko.Sandbox`, which implements no `Tool`.
BardStudio, the other implementer, migrated fine. One of two followed; the other was never told.

⚠ **Do not "fix" this by softening the break.** The rejected alternative was a virtual overload
forwarding to the old abstract, which compiles everywhere and **discards the token** — rule 51's
"silent no-op wearing a parameter's name". A compile error naming every file is the cheapest failure
mode available; the defect was the silence, not the breakage.

## Integration model — commit to `main`, one commit per repo


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

## New Project Checklist
Every project directory must contain:

1. **`README.md`** — Project name, overview, features, test framework (if test project), running instructions, and License section.
2. **`CLAUDE.md`** — Overview, project location, components, dependencies, and maintenance instructions.

> **No per-project `License.md` or `.gitignore`.** The repo has one root `LICENSE` (MIT, 2026
> František Bereň) and one root `.gitignore`; both cover every project below them. The 266
> per-project licence copies and 334 identical `.gitignore` copies were artefacts of the
> one-repo-per-project era and were removed with the monorepo migration. Seven `.gitignore`
> files with genuinely project-specific rules survive and are the only ones that should exist.

**GUID requirements for `.shproj` and `.projitems` files:**
- `ProjectGuid` in `.shproj` and `SharedGUID` in `.projitems` must be valid GUIDs containing **only hex characters** (`0-9`, `a-f`).
- Format: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` (8-4-4-4-12 characters). Do NOT use human-readable names or non-hex letters (`g-z`) in GUIDs.
- Each project must have a unique GUID. Generate a proper random GUID (e.g., `b3a8c1d4-e5f6-4a7b-9c0d-1e2f3a4b5c6d`).

**External dependencies — the project that WRAPS a library owns it:**
- If a `Birko.X` shared project `using`s an external package, **declare it in that project's `.projitems`**,
  with a **floating** version (`Version="9.*"`). Do not leave it for the consumer to discover as a compile
  error. 6 of 8 storage backends already do this; the two that did not are [[TASK-229]].
- **This is the framework's whole proposition, not bookkeeping.** Birko is the unifying middleware between
  necessary libraries — drivers, cache providers — and the consumer's code, so a consumer imports
  `Birko.Data.MongoDB` and never learns the package is `MongoDB.Driver 3.*`.
- **Ownership does not cost a consumer that imports only a subset — measured, because the opposite is the
  obvious guess** (TASK-234). A `PackageReference` inside a `.projitems` materialises **only in a project
  that imports that `.projitems`**: an ElasticSearch consumer resolves 24 libraries with NEST and no Redis;
  a Redis consumer resolves 25 with StackExchange.Redis and no NEST. Ownership *is* the subset model.
- **Three shapes, and the third is the one that gets argued about:**
  1. **The wrapping project owns it.** One declaration.
  2. **A sibling built on that project does not re-declare** — a consumer imports both, and two
     `PackageReference` items in one project file is `NU1504`. Record the pairing in a comment instead;
     MSBuild gives a shared project no way to say "I depend on that other shared project", so the comment is
     the only place it can live. Without it the next audit reads the absence as the defect and adds it back.
  3. **A project that uses the same library *independently* documents it as consumer-supplied.** It cannot
     declare (it would collide wherever both are imported — `Birko.Sandbox` imports everything), and it must
     **not** take a dependency on the owning project to inherit the declaration: `Birko.Health.Redis` doing
     so would drag `Configuration`, `Data.Core`, `Data.Stores`, `Contracts` and `Time` into a consumer that
     wanted one health check. **A design fix that violates the subset rule is not a fix.** Cost is one line
     for a standalone consumer and nothing for anyone else.
- **A `FrameworkReference` is never owned.** `Microsoft.AspNetCore.App` is not a library Birko wraps — the
  host already has it from `Microsoft.NET.Sdk.Web`. All such projects document it. A different category, not
  an exception to the rule.
- **Floating, not pinned** — a published advisory then self-heals on the next restore instead of needing an
  edit in every consumer. The accepted cost is that builds are not reproducible from source alone and a bad
  upstream release lands without anyone opting in; the periodic audit below is the safety net that choice
  depends on.
- A package declared in a `.projitems` is *injected* into the importing project, so a consumer that also
  declares it gets **NU1504 duplicate PackageReference** — a warning normally, an **error** under the
  `-warnaserror` that `verify-birko-conventions` check 1 runs. So when you add a declaration, remove it from the
  dependents in the same change.
- **A consumer never declares a version BELOW the framework's; higher is allowed and it owns the breakage.**
  The rule the NU1504 bullet above only half-states. Three shapes, and the mechanism differs for each:
  **equal** → do not declare it at all, delete the line and leave a comment naming the owning `.projitems`;
  **higher** → allowed, but a second `Include` is a *duplicate*, not an override, so it must be
  `<PackageReference Update="…" Version="…" />` placed **after** the `$(BirkoSrc)` imports — an `Update`
  above them is a **silent no-op**, because `Update` only reaches an item that already exists and the
  imports are at the bottom of an aggregator; **lower** → refused. If a framework major genuinely breaks a
  consumer, the consumer fixes forward or the **framework** lowers its own declaration. Re-pinning the
  consumer is the move this rule exists to prevent.
- **⚠ NuGet already enforces exactly this rule for free, and the `.projitems` model gives it up.** "Consumer
  below framework" is `NU1605` *detected package downgrade*, an **error** by default — but `NU1605` compares
  across a *package dependency edge*, and a shared project compiled into the consumer's own assembly has no
  package identity to hang one on. So the framework's `10.*` and a consumer's `9.*` are two items in one
  project: `NU1504`, a warning, about duplication, saying nothing about which is lower. Measured 2026-09-19:
  Symbio carried `Npgsql 9.*` against the framework's `10.*` and restore reported only a duplicate. **This is
  a named cost of the deferral two bullets down** — shipping as real packages deletes the problem. Until then
  the rule is enforced by [`audit-consumer-versions.cs`](audit-consumer-versions.cs), not by restore.
- **⚠ A CPM consumer writes its version somewhere else, and the rule follows it there.** Under central
  package management the framework's declaration is the *bare* half of its conditioned pair and the version
  comes from the consumer's `Directory.Packages.props` — so the consumer's project files declare nothing,
  and a check that reads project files reports it clean. Measured 2026-09-19, the day Symbio adopted CPM:
  its 16 entries were all correct, and the audit said so **by looking in the wrong file**. Four of the eight
  importing consumers are now CPM, so this is the common case, not the exotic one. A missing entry is at
  least loud (`NU1010` names the package); a *lower* one is silent, and that is the whole point of the rule.
- **A consumer pinning an exact version where the framework floats is not a violation, but it is a decision.**
  It opts that consumer out of the self-healing the float bullet above exists for, and it does so invisibly
  when the pin happens to sit on the framework's floor. Measured: DraCode's `JwtBearer 10.0.0` against
  `10.*` compares as *equal* on floors and is nothing of the kind. The audit reports it as `PINNED`; record
  why, or drop it.
- Shipping the backends as real NuGet packages is **deferred** until the libraries stabilise. Declaring here
  is forward-compatible with that: a package's dependency list is exactly this set.
- **Write the declaration in the dual, CPM-compatible form.** A consumer using Central Package Management
  cannot accept a `PackageReference` that carries a `Version` — restore fails `NU1008`. Three Birko consumers
  (BardStudio, Latent, Presenter) use CPM, and a version-carrying declaration made **14 of their projects
  unrestorable**. So every declaration is two conditioned items:

  ```xml
  <PackageReference Include="Npgsql" Version="10.*" Condition="'$(ManagePackageVersionsCentrally)' != 'true'" />
  <PackageReference Include="Npgsql"                Condition="'$(ManagePackageVersionsCentrally)' == 'true'" />
  ```

- **And add it to [`Birko.Packages.props`](Birko.Packages.props) in the same change.** Under CPM the version
  has to come from the consumer's central file, so that file carries a `PackageVersion` for every package a
  shared project declares, and CPM consumers import it. **Forgetting an entry breaks every CPM consumer with
  `NU1010`.** That file also sets `CentralPackageFloatingVersionsEnabled`, because CPM rejects a floating
  `PackageVersion` outright (`NU1011`) and every version here floats — the property is a consequence of the
  float decision, not a separate choice.
- **A `FrameworkReference` is stricter than a `PackageReference`: a duplicate is an ERROR** (`NETSDK1087`),
  not a warning. So when a shared project declares one, every importer that also declares it must drop
  theirs in the same change — there is no tolerated-duplicate state to land in. Measured: adding one to
  `Birko.Data.Tenant` broke 8 test projects, `Birko.Sandbox` and a consumer at once.

## Dependency vulnerability audit

**NuGet's audit is already on** — `NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low` are SDK
defaults (verified 2026-08-17), so every ordinary `dotnet build` already prints `NU1901`–`NU1904` for an
affected project. **Do not add these properties to a props file; it is a no-op.**

What the build cannot do is notice an advisory published against code nobody is building. That is a
time-based gap, not a build-configuration one, so it is covered by a **periodic sweep**:

```bash
dotnet run audit-dependencies.cs                        # report
dotnet run audit-dependencies.cs -- --fail-on-finding   # exit 1 — for a scheduled job
```

Run it on a schedule, before a release, and after any dependency bump. Two rules it enforces by construction
and that are easy to get wrong by hand:

- **Sweep consumers, not just `tests/`.** A test-only sweep once reported `SQLitePCLRaw` 2.1.10 and
  implied anything newer was fine, while `Birko.Sandbox` — on a *newer* `Microsoft.Data.Sqlite` — was still
  affected at **2.1.11**. Scoping to one tree produced a remedy that looked complete and was not.
- **Check the resolved transitive, not the top-level version number.** `Microsoft.Data.Sqlite` 10.0.0 is
  newer than 9.0.19 and *worse*: 10.0.0 resolves `SQLitePCLRaw` 2.1.11, 9.0.19 resolves the fixed 2.1.12.

Promotion to a build error stays where it is — `verify-birko-conventions` check 1, on the diff of the task in hand,
where a human is present to judge it. Making it a global error would break every affected project today and,
with floating versions, could break any build at any time from an upstream publication nobody chose.

### The three audits, and the different question each asks

They are siblings on purpose; a zero from one says nothing about the others.

```bash
dotnet run audit-declarations.cs        # does every shared project ACCOUNT for the packages it uses?
dotnet run audit-dependencies.cs        # is any RESOLVED package vulnerable?  (sweeps tests/ + Consumers/)
dotnet run audit-consumer-versions.cs   # does any consumer DECLARE a version below the framework's?
```

They are **.NET 10 file-based apps**, not PowerShell, and that is deliberate: they need nothing a Birko
build does not already need — no pwsh, no `dotnet tool install`. The four originals were `.ps1` and
**none of them ran on Linux**; three failed by silently answering the wrong question rather than by
erroring. See [`ADR-002`](docs/adr/ADR-002-audit-scripts-as-dotnet-file-based-apps.md) and TASK-476.
Shared path handling has ONE producer, [`tools/AuditCommon/Paths.cs`](tools/AuditCommon/Paths.cs) —
no separator literal belongs anywhere else.

> **Rule — repo tooling is a .NET file-based app, not a shell script.** Anything committed at the
> aggregator root that a maintainer runs by hand (an audit, a sweep, an installer) is a `.cs` file
> run with `dotnet run`. Reasons, in order: it needs no runtime the repo does not already require,
> so it works on every machine in the family; it is ONE implementation rather than a `.ps1`/`.sh`
> pair that drifts; and its helpers are testable — `tests/AuditCommon.Tests` is registered in the
> `.slnx`, so CI builds it and the `tests/*/` loop **runs it on Linux**, which is the only place
> the path assumptions it guards can actually be proved. A shell one-liner is still a shell
> one-liner; this is about tooling that gets committed and relied on.
>
> ⚠ **Path handling inside such a tool has one producer.** Every separator literal lives in
> `tools/AuditCommon/Paths.cs`. Four scripts each carrying their own path expressions is exactly
> how TASK-476 happened, and three of the four failed by silently answering the wrong question.

The third exists because `NU1605` cannot see a shared-project boundary — see the ⚠ bullet under § External
dependencies. It is also the one that finds the *cause* where the second finds the *symptom*: a consumer
pinned below the framework's floor is one line, while the advisory rows it produces are many.

## Solution & Workspace Registration
When adding a new project, register in **all four**:

1. **`Birko.Framework.slnx`** — Add `<Project>` in the appropriate `<Folder>`. Shared projects use `.shproj`, test projects use `.csproj`. Paths relative to `.slnx`.

2. **`Birko.Framework.code-workspace`** — Add folder entry with `"Group / Birko.ProjectName"` name convention. Keep entries sorted alphabetically. **A test project needs its own entry too**, under the `Tests /` group — the `.slnx` and the workspace are separate lists and it is easy to add to one and not the other.

3. **A sibling `Birko.{ProjectName}.Tests` project** in `tests/`, importing the new `.projitems`. This is not only about coverage: a shared project is `.shproj`/`.projitems` and **cannot build on its own**, so until something imports it, *nothing in the family compiles it*. The test project is the cheapest thing that does.

4. **The build-validation aggregator** (`Consumers/Birko.Sandbox/Birko.Framework/Birko.Framework.csproj`) — add the `<Import>` beside its siblings, so the project is compiled by the smoke harness as well as by its tests.

> **Why steps 3 and 4 are listed.** `Birko.EventBus.Outbox.SQL` was added with steps 1 and 2 missed and steps 3 and 4 absent, so a finished project — own repo, `IOutboxStore` implemented — was **compiled by nothing and tested by nothing** for as long as it existed. It happened to still build when this was found, which is luck, not a guarantee: a change to its interface or to `Birko.Data.SQL` would have broken it silently. See [[TASK-231]].
>
> Verify with a sweep rather than by eye — every project directory at the repo root and under `tests/` holding a `.shproj` or `.csproj` should appear in both the `.slnx` and the `.code-workspace`. As of 2026-08-17 that is **342 of 342**.

Existing folder groups:
- **BackgroundJobs/** — Birko.BackgroundJobs.*
- **Caching/** — Birko.Caching, Birko.Caching.Redis, Birko.Caching.Hybrid
- **Communication/** — Birko.Communication.*
- **Data/** — Birko.Contracts, Birko.Data.Core, Birko.Configuration, Birko.Data.Stores, Birko.Data.Repositories
- **Health/** — Birko.Health, Birko.Health.Data, Birko.Health.Redis, Birko.Health.Azure
- **Data.Migrations/** — Birko.Data.Migrations.*
- **Data.NoSQL/** — ElasticSearch, InfluxDB, JSON, MongoDB, RavenDB, TimescaleDB stores
- **Data.Patterns/** — Birko.Data.Patterns, EventSourcing, Tenant
- **Data.SQL/** — Birko.Data.SQL, MSSql, MySQL, PostgreSQL, SqLite, View
- **Data.Sync/** — Birko.Data.Sync.*
- **Data.ViewModels/** — Birko.Data.*.ViewModel
- **Helpers/** — Birko.Helpers, Birko.Structures, Birko.Random
- **Models/** — Birko.Models.*
- **Redis/** — Birko.Redis
- **Security/** — Birko.Security, Birko.Security.Jwt/AspNetCore/BCrypt/Vault/AzureKeyVault/NFC/OAuth.Server
- **Serialization/** — Birko.Serialization, .Newtonsoft, .MessagePack, .Protobuf, .Yaml
- **Storage/** — Birko.Storage, Birko.Storage.AzureBlob
- **Telemetry/** — Birko.Telemetry, Birko.Telemetry.OpenTelemetry
- **Tests/** — All *.Tests projects
- **CQRS/** — Birko.CQRS
- **Rules/** — Birko.Rules
- **Validation/** — Birko.Validation
- **Time/** — Birko.Time.Abstractions, Birko.Time
- **Workflow/** — Birko.Workflow, Birko.Workflow.SQL/ElasticSearch/MongoDB/RavenDB/JSON/CosmosDB
- **AI/** — Birko.AI.Contracts, Birko.AI, Birko.AI.Providers, Birko.AI.Agents, Birko.AI.Resilience, Birko.AI.Orchestration
- **Data.Views/** — Birko.Data.Views, Birko.Data.SQL.Views, Birko.Data.MongoDB.Views, Birko.Data.ElasticSearch.Views, Birko.Data.RavenDB.Views, Birko.Data.CosmosDB.Views
- **EventBus/** — Birko.EventBus, Birko.EventBus.MessageQueue, Birko.EventBus.Outbox, Birko.EventBus.EventSourcing, Birko.EventBus.Tenant
- **Localization/** — Birko.Localization, Birko.Localization.Data, Birko.Data.Localization
- **Messaging/** — Birko.Messaging, Birko.Messaging.Razor
- **Web/** — Birko.Web.Core, Birko.Web.Components, Birko.Web.Shell

## Documentation Index Registration
A new project is not "registered" until it appears in the framework's **documentation index**, not just the build files. `.slnx` / `.code-workspace` / `.csproj` make it compile; the doc index makes it discoverable. Every new non-test project (`.shproj`) must be added to **all three**:

1. **`README.md`** — a row in the "Projects" table (`| Birko.X | one-line purpose |`), placed next to its siblings.
2. **`CLAUDE-projects.md`** — a bullet in the appropriate category (`- **Birko.X** - short description`).
3. **`docs/{topic}.md`** — the topic page for the project's area (e.g. a new EventBus variant → a row in `docs/event-bus.md`'s layer table **and** its dependency table). If the project opens a brand-new area with no existing topic page, add a new `docs/{topic}.md` and link it from `README.md`.

Exclusions: `.Tests`, `.ViewModel`, `.Views` companions inherit their parent's documentation and need no separate index row (but confirm the parent is indexed). Test projects are never listed in the project index.

This is the gap that build-file registration alone misses — a project can compile and ship yet be invisible in every human-facing doc. `verify-birko-conventions` check #7b lints for it.

## Test Requirements
Every new public functionality must have corresponding unit tests:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions

## Health Check Requirements
When creating a project connecting to an external service, **automatically create a health check**:
- **Birko.Health.Data** — database/data store providers
- **Birko.Health.Redis** — Redis-specific checks
- **Birko.Health.Azure** — Azure cloud services
- **New Birko.Health.X project** — if doesn't fit existing

Health check pattern:
1. Implement `IHealthCheck` with lightweight connectivity probe (ping, SELECT 1, list maxResults=1)
2. Dual constructors: `Func<T>` factory and singleton instance
3. Three-level status: Healthy (OK), Degraded (slow > threshold), Unhealthy (exception)
4. Include `latencyMs` in result `Data` dictionary
5. Add unit tests for constructor validation, factory exception handling, cancellation
6. Update `docs/health.md`, health examples, and Health tab in Program.cs
7. Register in solution (.slnx), workspace (.code-workspace), and framework .csproj
