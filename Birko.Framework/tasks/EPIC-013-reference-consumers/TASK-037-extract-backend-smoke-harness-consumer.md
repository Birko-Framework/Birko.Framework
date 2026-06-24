---
id: TASK-037
parent: EPIC-013
feature: null
status: done  # todo | in-progress | review | blocked | done | cancelled
priority: P2
assignee: ai
created: 2026-06-18
depends-on: []
blocks: []
related: [TASK-036, TASK-038]
pr: null
github-issue: null
jira-key: null
---

# Replace the TUI example with an extracted backend integration smoke-harness consumer

## Context

`C:\Source\Birko\Framework\Birko.Framework\` is doing two unrelated jobs at once:

1. **Compile-validation aggregator** — `Birko.Framework.csproj` `<Import>`s virtually every
   `.projitems` (Data, Communication, Security, Caching, Messaging, Telemetry, BackgroundJobs,
   MessageQueue, Workflow, Serialization, Models, …) so a single `dotnet build` proves the whole
   framework compiles together. This role is referenced by `CLAUDE.md`, the `.slnx`, and the
   aggregator-pattern docs and must be preserved.
2. **TUI demo** — `Program.cs` (~47 KB) + `Examples/`, `Services/`, `Configuration/` built on
   `XenoAtom.Terminal.UI`. This is the "example" the user wants gone and replaced with something
   better.

Decision (2026-06-18): keep the in-repo project as a **bare, headless compile gate** and extract
the runnable example into a **new sibling consumer checkout** — a realistic integration smoke
harness that consumes the framework the documented way (own aggregator project +
`Directory.Build.props` resolving `$(BirkoSrc)`, per `README.md` § "Usage in Consumer Solutions"),
not by importing every `.projitems`. After [[TASK-036]] this sibling lands in `Birko\Consumers\`.

Name: `Birko.Sandbox` (decided 2026-06-18). Use it consistently across the repo, aggregator
project, `.gitignore`, and docs.

### Why a smoke harness rather than a Web API / kept-TUI

A console/worker that *wires up and runs* a representative slice of every layer gives the broadest
compile **and** runtime coverage per line — it is the "first test place" the user described, where
a framework change is sanity-checked end to end before touching real consumers (Symbio, etc.). A
Web API would be narrower; keeping the TUI keeps the dead-weight UI dependency.

## Progress (2026-06-18)

**Part 1 — compile gate narrowed AND relocated into Birko.Sandbox (decision revised 2026-06-18):**
Revision of the original "keep the gate in the framework repo" decision — the gate now moves into the
Birko.Sandbox consumer as a **separate `Library` project** (Option A: two projects in one repo), leaving
`Birko\Framework\Birko.Framework` as a **docs/meta-only repo**.
- `Birko.Framework.csproj`: `OutputType` Exe → **Library**, dropped `XenoAtom.Terminal.UI` + demo `appsettings.json`; **162 imports converted** `$(MSBuildThisFileDirectory)..\..\Birko.X` → `$(BirkoSrc)\Birko.X` (consumer-style; inherits Birko.Sandbox's `Directory.Build.props`).
- `Birko.Framework.slnx` aggregator entry re-pointed cross-bucket → `../../Consumers/Birko.Sandbox/Birko.Framework/Birko.Framework.csproj` (slnx stays in the framework repo, per user).
- Gate `README.md`/`CLAUDE.md` + Birko.Sandbox `README.md`/`CLAUDE.md` updated for the two-project layout.
- ✅ **Done & verified (2026-06-18):** user deleted the TUI files + moved the gate into `Birko\Consumers\Birko.Sandbox\Birko.Framework\`. Framework repo is now docs/meta only. **Gate builds green** (`dotnet build` → 0 errors, all 162 projitems via `$(BirkoSrc)`); **harness runs green** (6/6, exit 0). Remaining (repo hygiene, not a build gate): user `git init` the Birko.Sandbox repo.

**Part 2 — new sibling smoke-harness consumer `Birko.Sandbox` (`Birko\Consumers\Birko.Sandbox`):**
- Universal layer (`README.md`, `CLAUDE.md`, `License.md`, `.gitignore`) + `Directory.Build.props` (`$(BirkoSrc)` default `..\..\Framework`) + `Birko.Sandbox.csproj` (lean projitems closure, not the all-projects list).
- `Program.cs` smoke runner — **6/6 checks green via `dotnet run`, exit 0**: Configuration, InMemory CRUD, Serialization round-trip, Workflow build+run, Background-job enqueue/process (InMemoryJobQueue), AI provider wiring (LlmProviderFactory, no live call). Non-zero exit on any failure (try/catch per check → `failed` count → exit 1).
- Registered in the framework `README.md` "Live examples" consumer list.
- **Pending:** user runs `git init` + initial commit for the new repo (per the no-auto-init rule).

## Acceptance criteria

### In-repo aggregator (compile gate) — reduce, don't delete
- [ ] `Program.cs`, `Examples/`, `Services/`, `Configuration/` TUI/demo content removed
- [ ] `XenoAtom.Terminal.UI` (and any other demo-only `PackageReference`) dropped from `Birko.Framework.csproj`
- [ ] Project still `<Import>`s all `.projitems` and builds green from CLI (`dotnet build Birko.Framework/Birko.Framework.csproj`) — the "all projects compile" gate is intact
- [ ] `OutputType` becomes `Library` (or an `Exe` with an empty `Main`) — headless, no runtime demo
- [ ] `Birko.Framework/README.md` + `Birko.Framework/CLAUDE.md` updated to describe the narrowed role and point at the new sibling for the runnable example

### New sibling smoke-harness consumer
- [ ] New sibling checkout created with the standard universal layer (`README.md`, `CLAUDE.md`, `License.md`, `.gitignore`) — use the [[new-project]] / [[birko-new-project]] skills so the aggregator wiring is correct
- [ ] `Directory.Build.props` resolves `$(BirkoSrc)` per the convention (`/p:BirkoSrc` → `BIRKO_SRC` env → relative default); after [[TASK-036]] the relative default is `..\..\Framework`
- [ ] One aggregator project bundling the `Birko.*` `.projitems` the harness actually uses (not a copy of the all-in import list)
- [ ] A smoke pass that, for a representative slice, constructs settings → store/service → does a tiny CRUD/round-trip and asserts success: e.g. an `InMemory` (and/or `JSON`) store CRUD, a serializer round-trip, a workflow build/run, a background-job enqueue/process, an `ILlmProvider`/agent wiring (no live call), a config-settings load
- [ ] Runs green via `dotnet run`; non-zero exit on any smoke failure (so it's CI-usable)
- [ ] Registered wherever sibling consumers are tracked (and noted in the framework `README.md` consumer list if one exists)

## Out of scope

- Performing the `C:\Source` folder move itself — that's [[TASK-036]]; this task only ensures the new consumer is `$(BirkoSrc)`-correct at both the current flat layout and the post-move depth.
- Exhaustive coverage of *every* provider/backend — a *representative* slice per layer is the goal; deep per-provider tests live in the `*.Tests` projects.
- The Web playground — that's [[TASK-038]].
- Turning this into the project's formal test suite (xUnit) — it's a runnable smoke harness, complementary to the unit/integration test projects.

## Human test plan

- [ ] `dotnet build Birko.Framework/Birko.Framework.csproj` from the framework repo — compiles green with no TUI deps
- [ ] In the new sibling: `dotnet build` resolves `$(BirkoSrc)` and the aggregator `.projitems` imports succeed
- [ ] `dotnet run` in the new sibling — smoke pass prints per-layer OK lines and exits 0; deliberately break one wiring and confirm it exits non-zero
- [ ] Confirm `Birko.Framework/README.md` + `CLAUDE.md` no longer describe a TUI and point to the new consumer
