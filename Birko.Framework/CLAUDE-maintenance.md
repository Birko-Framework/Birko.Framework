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

## New Project Checklist
Every project directory must contain:

1. **`License.md`** — MIT license (Copyright 2026 František Bereň). Copy from any existing project.
2. **`README.md`** — Project name, overview, features, test framework (if test project), running instructions, and License section.
3. **`CLAUDE.md`** — Overview, project location, components, dependencies, and maintenance instructions.
4. **`.gitignore`** — Standard Visual Studio .gitignore. Copy from any existing project.

**GUID requirements for `.shproj` and `.projitems` files:**
- `ProjectGuid` in `.shproj` and `SharedGUID` in `.projitems` must be valid GUIDs containing **only hex characters** (`0-9`, `a-f`).
- Format: `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` (8-4-4-4-12 characters). Do NOT use human-readable names or non-hex letters (`g-z`) in GUIDs.
- Each project must have a unique GUID. Generate a proper random GUID (e.g., `b3a8c1d4-e5f6-4a7b-9c0d-1e2f3a4b5c6d`).

**External dependencies — the shared project declares its own driver:**
- If a `Birko.X` shared project `using`s an external package, **declare it in that project's `.projitems`**,
  with a **floating** version (`Version="9.*"`). Do not leave it for the consumer to discover as a compile
  error. 6 of 8 storage backends already do this; the two that did not are [[TASK-229]].
- **Floating, not pinned** — a published advisory then self-heals on the next restore instead of needing an
  edit in every consumer. The accepted cost is that builds are not reproducible from source alone and a bad
  upstream release lands without anyone opting in; the periodic audit below is the safety net that choice
  depends on.
- A package declared in a `.projitems` is *injected* into the importing project, so a consumer that also
  declares it gets **NU1504 duplicate PackageReference** — a warning normally, an **error** under the
  `-warnaserror` that `verify-conventions` check 1 runs. So when you add a declaration, remove it from the
  dependents in the same change.
- Shipping the backends as real NuGet packages is **deferred** until the libraries stabilise. Declaring here
  is forward-compatible with that: a package's dependency list is exactly this set.

## Dependency vulnerability audit

**NuGet's audit is already on** — `NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low` are SDK
defaults (verified 2026-08-17), so every ordinary `dotnet build` already prints `NU1901`–`NU1904` for an
affected project. **Do not add these properties to a props file; it is a no-op.**

What the build cannot do is notice an advisory published against code nobody is building. That is a
time-based gap, not a build-configuration one, so it is covered by a **periodic sweep**:

```powershell
.\audit-dependencies.ps1                 # report
.\audit-dependencies.ps1 -FailOnFinding  # exit 1 — for a scheduled job
```

Run it on a schedule, before a release, and after any dependency bump. Two rules it enforces by construction
and that are easy to get wrong by hand:

- **Sweep consumers, not just `Framework.Tests`.** A test-only sweep once reported `SQLitePCLRaw` 2.1.10 and
  implied anything newer was fine, while `Birko.Sandbox` — on a *newer* `Microsoft.Data.Sqlite` — was still
  affected at **2.1.11**. Scoping to one tree produced a remedy that looked complete and was not.
- **Check the resolved transitive, not the top-level version number.** `Microsoft.Data.Sqlite` 10.0.0 is
  newer than 9.0.19 and *worse*: 10.0.0 resolves `SQLitePCLRaw` 2.1.11, 9.0.19 resolves the fixed 2.1.12.

Promotion to a build error stays where it is — `verify-conventions` check 1, on the diff of the task in hand,
where a human is present to judge it. Making it a global error would break every affected project today and,
with floating versions, could break any build at any time from an upstream publication nobody chose.

## Solution & Workspace Registration
When adding a new project, register in **all four**:

1. **`Birko.Framework.slnx`** — Add `<Project>` in the appropriate `<Folder>`. Shared projects use `.shproj`, test projects use `.csproj`. Paths relative to `.slnx`.

2. **`Birko.Framework.code-workspace`** — Add folder entry with `"Group / Birko.ProjectName"` name convention. Keep entries sorted alphabetically. **A test project needs its own entry too**, under the `Tests /` group — the `.slnx` and the workspace are separate lists and it is easy to add to one and not the other.

3. **A sibling `Birko.{ProjectName}.Tests` project** in `Framework.Tests/`, importing the new `.projitems`. This is not only about coverage: a shared project is `.shproj`/`.projitems` and **cannot build on its own**, so until something imports it, *nothing in the family compiles it*. The test project is the cheapest thing that does, and it is tracked in its own repo.

4. **The build-validation aggregator** (`Consumers/Birko.Sandbox/Birko.Framework/Birko.Framework.csproj`) — add the `<Import>` beside its siblings, so the project is compiled by the smoke harness as well as by its tests.

> **Why steps 3 and 4 are listed.** `Birko.EventBus.Outbox.SQL` was added with steps 1 and 2 missed and steps 3 and 4 absent, so a finished project — own repo, `IOutboxStore` implemented — was **compiled by nothing and tested by nothing** for as long as it existed. It happened to still build when this was found, which is luck, not a guarantee: a change to its interface or to `Birko.Data.SQL` would have broken it silently. See [[TASK-231]].
>
> Verify with a sweep rather than by eye — every project directory under `Framework/` and `Framework.Tests/` holding a `.shproj` or `.csproj` should appear in both the `.slnx` and the `.code-workspace`. As of 2026-08-17 that is **342 of 342**.

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

This is the gap that build-file registration alone misses — a project can compile and ship yet be invisible in every human-facing doc. `verify-conventions` check #11 lints for it.

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
