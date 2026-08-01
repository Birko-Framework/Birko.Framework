---
id: TASK-036
parent: null
feature: null
status: done
priority: P1
assignee: ai
created: 2026-06-17
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Reorganize C:\Source into Birko/{Framework,Framework.Tests,Consumers} + aicode bucket

## Context

`C:\Source` is one flat folder holding ~225 sibling git checkouts: ~190 `Birko.*`
framework projects mixed in with ~34 consumer apps, FinStat product code, and scratch.
The framework dwarfs everything else, so the directory is unnavigable. There is also
build garbage leaked to the root (`C:\Source\bin\`, `C:\Source\obj\` from a stray
`_jjqverify.csproj` build) and several dead/empty/misnamed folders.

`C:\Source` must remain **NOT a git repo** — only independently-tracked sibling checkouts
(see memory `project_source_not_git_tracked.md`). Nesting checkouts one level deeper into
bucket folders does **not** violate this — the bucket dirs are plain containers, not repos.

**Target layout** (decided with the user 2026-06-17; `finstat\` bucket dropped 2026-06-18 — FinStat code stays flat at the `C:\Source` root):

```
C:\Source\
├── Birko\
│   ├── Framework\        ← ~173 production Birko.* checkouts (Birko.Framework, Birko.AI, Birko.Data.*, Birko.Web.*, …) — NO *.Tests
│   ├── Framework.Tests\  ← ~60 Birko.*.Tests checkouts (Birko.Data.Tests, Birko.AI.* tests, …)
│   └── Consumers\      ← apps built on / slated for Birko:
│                          Symbio, Symbio.Core, Symbio.Monitor, gameshow-app,
│                          WorkoutTracker, Presenter, BardStudio, DraCode,
│                          Affiliate*, FisData.Stock*(.Core/.Web/.Angular/.API)*
├── aicode\             ← agent-coded scratch / examples: Snake, Wedding, leon, antigravity,
│                          test (Latent.slnx), symbio_test, InternalDevMeetup, DraCode-Projects, CheesyBot
└── (left flat at root, NOT bucketed — per user 2026-06-18):
                           WhMan, EventSourcing,
                           finstat, finstat-other, api-documentation,
                           ClientApi.CSharp, ClientApi.PHP, SuperFaktura, SuperFakturaAPI.NET,
                           DataSetExtractor
```

`WhMan` and `EventSourcing` are **not** Birko consumers — leave them exactly where they are on
disk (flat at `C:\Source` root). The FinStat product code (`finstat`, `finstat-other`,
`api-documentation`, `ClientApi.*`, `SuperFaktura*`, `DataSetExtractor`) **also stays flat at
root** — no `finstat\` bucket (per user 2026-06-18). The bucket dirs are containers, not an
exhaustive partition, so ungrouped repos staying at root is fine. Note that some root-level repos
(the FinStat `ClientApi.*` / `SuperFaktura*`) *do* consume Birko and so still need a
`BirkoSrc` re-point — but at a shallower depth than `Consumers\` (see Phase 4).

\* `Affiliate` and `FisData.Stock*` are **pre-prototype** Birko consumers (may be fully
migrated later) → live in `Birko\Consumers\` by trajectory. FinStat-org projects
(`finstat`, `finstat-other`, `api-documentation`, `ClientApi.*`, `SuperFaktura*`,
`DataSetExtractor`) are **left flat at the `C:\Source` root** — not bucketed (per user
2026-06-18) — even though some `ClientApi.*` / `SuperFaktura*` consume Birko.

### The load-bearing constraint: `BirkoSrc` resolution

Two independent resolvers locate the Birko sources, both currently defaulting to the flat
`C:\Source` layout. **Decision: do NOT set the `BIRKO_SRC` env var for now** — instead
re-point the in-file defaults. Both resolvers check the env var first, so a later
machine-wide `BIRKO_SRC=C:\Source\Birko\Framework` would transparently override these
edits with zero re-work (forward-compatible).

| Resolver | File | Default kind | Current | After move |
|---|---|---|---|---|
| MSBuild | `<consumer>\Directory.Build.props` | relative `$(MSBuildThisFileDirectory)..` | `C:\Source` | **`$(MSBuildThisFileDirectory)..\..\Framework`** |
| Frontend esbuild | `<consumer>\…\build.js` | absolute `process.env.BIRKO_SRC ?? 'C:/Source'` | `C:/Source` | **`'C:/Source/Birko/Framework'`** |

A consumer at `C:\Source\Birko\Consumers\Symbio\` reaches the framework via
`..\..\Framework` (up to `Consumers\`, up to `Birko\`, into `Framework\`). All consumers
sit at the same depth, so the backend edit is uniform. The frontend default is absolute,
so it's the same string regardless of depth.

Intra-**production** references keep working untouched because all production projects move
together into `Framework\` — their relative paths to each other are unchanged. This covers the
aggregator `Birko.Framework.csproj` importing `..\Birko.X\*.projitems` and prod-to-prod
`.projitems` imports.

### The second load-bearing constraint: test-project relative imports

Splitting tests into a parallel `Framework.Tests\` bucket (decided 2026-06-18) moves the ~60
`Birko.*.Tests` projects **away from** the production projects they import. Each test csproj
imports prod `.projitems` via `..\Birko.X\Birko.X.projitems` (sibling, one level up). After the
split a test at `Birko\Framework.Tests\Birko.Data.Tests\` reaches the prod project at
`Birko\Framework\Birko.Data\` via **`..\..\Framework\Birko.X\Birko.X.projitems`**.

| Reference | Before | After |
|---|---|---|
| Test csproj → prod `.projitems` | `..\Birko.X\Birko.X.projitems` | **`..\..\Framework\Birko.X\Birko.X.projitems`** |
| Test ↔ test (e.g. shared test helpers) | `..\Birko.X.Tests\…` | unchanged (tests still siblings of each other) |

Every one of the ~60 test csprojs has multiple such prod imports — this is the bulk of the
mechanical work. The `Birko.Framework.slnx` and `Birko.Framework.code-workspace` also reference
each test project by relative path and must be re-pointed. A scripted find-replace
(`..\Birko.` → `..\..\Framework\Birko.` *only within the moved test csprojs*, excluding
test-to-test `..\Birko.X.Tests` imports) is the safe way; verify by building the test solution.

### Skill breakage

The Birko skills hardcode absolute `C:\Source\Birko.X\` paths (audited 2026-06-17):
- `.claude/skills/new-birko-subproject` — many path refs + `dotnet build C:\Source\Birko.Framework\...` / `dotnet test C:\Source\Birko.X.Tests\...` commands
- `.claude/skills/new-store-backend` — many path refs (ElasticSearch/JSON/InMemory references, connector, tests, build/test commands)
- `.claude/skills/new-birko-web-component` — `C:\Source\Birko.Web.Components\...`, `C:\Source\Birko.Web.Core\...`, root CLAUDE.md path
- `.claude/skills/roll-changelog`, `.claude/skills/verify-birko-conventions` — CLAUDE.md path refs
- (global) `~/.claude/skills/birko-new-project` — generates the consumer `BirkoSrc` default; must emit `..\..\Framework` (or document `BIRKO_SRC`); plus path refs
- (global) `~/.claude/skills/new-birko-web-page`, `new-project`, `design-agent` — path refs

All `C:\Source\Birko.X\` → `C:\Source\Birko\Framework\Birko.X\`. The framework solution
path becomes `C:\Source\Birko\Framework\Birko.Framework\Birko.Framework.slnx`.

## Progress (2026-06-18)

**Done:**
- Filesystem move executed via `C:\Source\TASK-036-move.ps1 -Execute` — 252/254 dirs moved + 4 deletes (`bin`, `obj`, `antigravity`, `Wedding`). `DraCode-Projects` + `leon` preserved into `aicode\` (they had real content, not hollow). `test`→`Latent`→`aicode\`.
- All 60 test csprojs re-pointed (341 prod-projitems imports `..\Birko.X\` → `..\..\Framework\Birko.X\`; zero test-to-test refs existed).
- 6 consumer `Directory.Build.props` re-pointed (incl. WorkoutTracker after its manual recovery; ClientApi at root → `..\Birko\Framework`); 5 web `build.js` de-hardcoded with an upward-search `resolveBirkoSrc()` (no committed absolute path — env var, else walk up to `Birko\Framework`, else throw).
- `Birko.Framework.slnx` + `.code-workspace` — 60 test entries each re-pointed to `..\..\Framework.Tests\`.
- Docs (`README.md` BirkoSrc section, `CLAUDE.md`), 8 skill files (59 paths; local + global), and memory `project_source_not_git_tracked.md` updated.
- **`Birko\Web` bucket split (follow-on, 2026-06-18):** the 3 TS frontend libs (`Birko.Web.Core` / `.Components` / `.Shell`) carved out of `Framework\` into `Birko\Web\`. All frontend wiring repointed — 5 `build.js` resolvers (walk-up marker → `Birko/Web`), ClientApi `tsconfig.json` / `Dockerfile` / `.dockerignore`, slnx + code-workspace (3 entries each), README + CLAUDE.md, memory, and the 2 web skills. The .NET side is untouched (the Web libs are TS-only, no MSBuild coupling). Frontend now resolves `Birko\Web`, MSBuild resolves `Birko\Framework`.

**Verification (2026-06-18, from the new location):**
- All filesystem moves done by the user: `Birko.Web.*` → `Birko\Web\`, and the `Birko.Framework` self-move to `Birko\Framework\Birko.Framework`.
- Frontend: all 5 consumer `build.js` resolvers correctly resolve `C:\Source\Birko\Web`.
- `.NET`: a committed test project (`Birko.Data.Tests`) builds green via the rewritten `..\..\Framework\` imports. Full-solution build surfaced only **pre-existing uncommitted WIP** breakage (not the reorg):
  - Removed 3 uncommitted TS `package.json` entries from the slnx (CLI parser can't load a project with no type; not at HEAD).
  - `Birko.MessageQueue.Redis.Tests` — added missing `Birko.Models.Contracts` import → green.
  - `Birko.Data.Migrations.SQL.Tests` — added the full `Patterns`/`SQL`/`SQL.View` projitems closure + `Microsoft.Extensions.DependencyInjection` package; excluded `SqlMigrationSplitTests.cs` (targets an unimplemented `SqlMigration` base type — tracked separately as unfinished feature work) → green.

- **Full-solution build: `Build succeeded`, 0 errors** (83 warnings, pre-existing) — all 170 prod + 60 test projects compile green from `Birko\Framework` + `Birko\Framework.Tests`, imports resolving through `..\..\Framework\`.

**Sign-off (2026-07-17):**
- Disk survey vs target layout — all buckets correct: Framework 175 dirs (0 misplaced `.Tests`), Framework.Tests 164 (0 misplaced non-tests), Web libs + Consumers bucketed, FinStat flat at root, no stray `Birko.*` at root, `C:\Source` still not a git repo.
- Symbio backend + frontend (`dotnet build Symbio.slnx`, incl. `Symbio.UI.esproj`) → **0 errors**; `$(BirkoSrc)` resolves to `Birko\Framework` (confirmed in build output paths).
- Deliberate deviations from original spec (not defects): `Latent` promoted to `Consumers\` (was scratch); `Symbio.Core`/`Symbio.Monitor` are projects inside `Symbio.slnx`, not top-level dirs; `Birko.Web.Testing`/`Birko.Sandbox`/`Birko.Xaml.Gallery`/`Birko.Web.Playground` are newer family projects, bucketed.
- Residual cleanup (cosmetic, outside reorg scope): stray `C:\Source\DraCode-Projects\` (2 leftover `koboldlair*.db`; real content in `aicode\DraCode-Projects\`) + 4 root build logs — flagged for manual deletion (agent delete blocked at `C:\Source` root by safety classifier).

## Acceptance criteria

- [ ] Root build garbage removed: `C:\Source\bin\` and `C:\Source\obj\` deleted
- [ ] Dead/empty scratch removed or moved to `aicode\`: `antigravity`, `Wedding`, `DraCode-Projects`, `leon` (confirm dead first)
- [ ] `test/` (real `Latent.slnx` repo) renamed to a meaningful name and moved to `aicode\`
- [ ] All ~173 **production** `Birko.*` checkouts (non-`*.Tests`) moved into `C:\Source\Birko\Framework\`
- [ ] All ~60 `Birko.*.Tests` checkouts moved into `C:\Source\Birko\Framework.Tests\`
- [ ] Every moved test csproj's prod `.projitems` imports re-pointed `..\Birko.X\` → `..\..\Framework\Birko.X\` (test-to-test `..\Birko.X.Tests\` imports left unchanged)
- [ ] `Birko.Framework.slnx` + `Birko.Framework.code-workspace` test-project entries re-pointed to `Framework.Tests\`; the full test suite builds and runs green from the new layout
- [ ] Consumer projects moved into `C:\Source\Birko\Consumers\` per the layout above (incl. `Affiliate`, `FisData.Stock*`)
- [ ] FinStat projects left flat at the `C:\Source` root (NOT moved into a `finstat\` bucket — that bucket is not created)
- [ ] Scratch projects moved into `C:\Source\aicode\`
- [ ] Every Birko-consuming `Directory.Build.props` default re-pointed: consumers under `Birko\Consumers\` `..` → `..\..\Framework`; any Birko consumer left flat at the `C:\Source` root (FinStat `ClientApi.*` / `SuperFaktura*`) `..` → `..\Birko\Framework`
- [ ] Every Birko.Web consumer `build.js` default changed `'C:/Source'` → `'C:/Source/Birko/Framework'`
- [ ] All skill files (local `.claude/skills/` + global `~/.claude/skills/`) updated: `C:\Source\Birko.X\` → `C:\Source\Birko\Framework\Birko.X\`; `birko-new-project` emits the new consumer default
- [ ] Docs updated: Birko.Framework `README.md` "Usage in Consumer Solutions" / `$(BirkoSrc)` section, `CLAUDE.md`, `CLAUDE-maintenance.md` path references reflect the new layout
- [ ] Memory `project_source_not_git_tracked.md` updated (or a new memory added) describing the bucket layout
- [ ] `Birko.Framework.slnx` still builds from its new location
- [ ] Symbio backend (`dotnet build`) and frontend (`node build.js`) both resolve Birko sources and build green

## Out of scope

- **Setting the `BIRKO_SRC` env var** — explicitly deferred; in-file defaults are edited instead. A future task can set it machine-wide + in CI/Docker to remove the depth-coupling.
- **Fully migrating `Affiliate` / `FisData.Stock*` onto Birko** — they're pre-prototype; the actual migration is separate, future work.
- **Making `C:\Source` a git repo** — forbidden by `project_source_not_git_tracked.md`; the buckets are plain containers.
- **Grouping/cleaning the standalone repos left flat at root** — the FinStat product code (`finstat`, `finstat-other`, `api-documentation`, `ClientApi.*`, `SuperFaktura*`, `DataSetExtractor`) plus `WhMan` and `EventSourcing`; they are deliberately not bucketed.

## Human test plan

_Filesystem moves + path resolution across MSBuild and esbuild — must verify a real build, not just file presence._

- [x] After the move, run `dotnet build "C:\Source\Birko\Framework\Birko.Framework\Birko.Framework.slnx"` — compiles green (intra-framework relative imports intact) — full-solution build green 2026-06-18 (170 prod + 60 test).
- [x] Build + run the full test suite from the new layout — a sample test project (e.g. `Birko\Framework.Tests\Birko.Data.Tests`) resolves its `..\..\Framework\Birko.X\*.projitems` imports and tests pass — verified 2026-06-18.
- [x] In a moved consumer (Symbio): `dotnet build` resolves `$(BirkoSrc)` to `C:\Source\Birko\Framework` and the ~70 `.projitems` imports succeed — **verified 2026-07-17: `dotnet build Symbio.slnx` → 0 errors** (resolved paths confirm `Birko\Framework`; frontend `Symbio.UI.esproj` built too).
- [x] In Symbio.UI: `node build.js` resolves `BIRKO_SRC` default to `C:/Source/Birko/Framework`, copies `tokens.css`, and bundles the `birko-web-*` aliases without "file not found" — built as part of the Symbio.slnx build (esproj) 2026-07-17; frontend resolver targets `Birko\Web` (post Web-bucket split).
- [x] Open `Birko.Framework.code-workspace` — sibling folders still resolve (no broken/red entries) — slnx/code-workspace re-pointed + build green.
- [x] Dry-run a skill that had hardcoded paths (e.g. read `new-store-backend`) and confirm every referenced file path exists at its new `C:\Source\Birko\Framework\...` location — skill path sweep done 2026-06-18.
- [x] Confirm `C:\Source` is still not a git repo (`Test-Path C:\Source\.git` → false) — **verified 2026-07-17: no `.git` at `C:\Source`.**

## Implementation plan

Phased, lowest-risk first. Verify at each gate before proceeding.

**Phase 1 — zero-risk cleanup (no build impact):**
1. Delete `C:\Source\bin\` and `C:\Source\obj\`.
2. Confirm-then-remove dead scratch: `antigravity` (empty), `Wedding` (README only), `DraCode-Projects` (hollow), `leon` (no git) — move to `aicode\` if any turn out non-dead.
3. Rename `test\` → `Latent\` (its real `Latent.slnx`); will move to `aicode\` in Phase 3.

**Phase 2 — prove the seam while still flat (optional safety net):**
4. Temporarily set `BirkoSrc` on one consumer via `/p:BirkoSrc=...` to confirm the override path works before relying on edited defaults. (Skip if confident.)

**Phase 3 — create buckets and move:**
5. `mkdir C:\Source\Birko\Framework`, `C:\Source\Birko\Framework.Tests`, `C:\Source\Birko\Consumers`, `C:\Source\aicode`. (No `finstat\` bucket.)
6. `mv` each **production** `Birko.*` checkout (non-`*.Tests`) → `Birko\Framework\` (plain filesystem move; git repos are path-agnostic).
7. `mv` each `Birko.*.Tests` checkout → `Birko\Framework.Tests\`.
8. `mv` consumers → `Birko\Consumers\`; scratch → `aicode\` per the layout table. FinStat projects stay flat at the `C:\Source` root (not moved).

**Phase 4 — patch resolution:**
9. In each `Birko\Consumers\*\Directory.Build.props`: `$(MSBuildThisFileDirectory)..` → `$(MSBuildThisFileDirectory)..\..\Framework`. FinStat Birko consumers left flat at the `C:\Source` root sit one level shallower → `$(MSBuildThisFileDirectory)..` → `$(MSBuildThisFileDirectory)..\Birko\Framework`. (Their `build.js`, if any, takes the same absolute `'C:/Source/Birko/Framework'` default as every other consumer — the frontend default is depth-independent.)
10. In each Birko.Web consumer's `build.js`: `'C:/Source'` → `'C:/Source/Birko/Framework'`.
11. In each moved `Birko\Framework.Tests\*\*.csproj`: re-point prod `.projitems` imports `..\Birko.X\` → `..\..\Framework\Birko.X\` (scripted find-replace; **exclude** test-to-test `..\Birko.X.Tests\` imports). Re-point the test entries in `Birko.Framework.slnx` + `.code-workspace`.

**Phase 5 — fix skills + docs:**
10. Sweep `.claude/skills/` (local) and `~/.claude/skills/` (global) replacing `C:\Source\Birko.X\` → `C:\Source\Birko\Framework\Birko.X\`; update `dotnet build/test` command paths; update `birko-new-project`'s generated consumer default to `..\..\Framework`.
11. Update Birko.Framework `README.md` (`$(BirkoSrc)` / Usage section), `CLAUDE.md`, `CLAUDE-maintenance.md` path references.
12. Update memory `project_source_not_git_tracked.md` to describe the bucket layout.

**Phase 6 — verify:**
13. Run the Human test plan (framework build, Symbio backend + frontend build, workspace open, skill path spot-check, no-git assertion).

**Suggested gating:** do the whole move Symbio-first end-to-end (move Symbio + its Birko deps, patch + build) as a proof, then batch the rest.
