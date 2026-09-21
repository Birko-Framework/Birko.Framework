---
id: TASK-476
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: review
priority: P2
assignee: ai
created: 2026-09-19
depends-on: [TASK-481]
blocks: []
related: [TASK-267, TASK-457, TASK-473, TASK-474, TASK-475]
findings: []
pr: null
github-issue: null
jira-key: null
---

# The four root helper scripts are PowerShell, and they do not run on Linux

## Context

The aggregator root ships four committed tooling scripts — `audit-declarations.ps1`,
`audit-dependencies.ps1`, `audit-consumer-versions.ps1` and `install-skills.ps1`. PowerShell 7 itself
is cross-platform, so the language was never the blocker. **Every one of them is written with Windows
path assumptions**, and three fail in the silent-wrong-answer direction rather than by erroring — the
shape § Conventions keeps recording.

Raised by the user: pwsh is present on *some* of the family's machines, unknown on the rest, and a
second implementation (bash siblings) is explicitly not something they will maintain. That pair of
constraints is what selects the remedy; see § Decision.

## The Linux defects, measured by reading each script

| Script | Defect on Linux |
|---|---|
| `audit-dependencies.ps1` | `Join-Path $PSScriptRoot '..\..'` → `Resolve-Path` throws under `$ErrorActionPreference = 'Stop'`. With `-Root` supplied it gets worse: the bucket literal `'Framework\tests'` does not exist, `Consumers` does, so `if (-not $buckets) { throw }` **cannot fire** and the sweep silently covers a third of the tree. Also `-notmatch '\\(bin\|obj)\\'` never matches a `/`-separated path, so `bin`/`obj` copies are swept. |
| `audit-consumer-versions.ps1` | Same `'..\..'` throw. Underneath it, consumer imports are `$(BirkoSrc)\Birko.Helpers\Birko.Helpers.projitems` — backslash-separated, across 173 projitems — so after substitution `Test-Path` fails and `GetFileNameWithoutExtension` returns garbage. Result: **every consumer reports 0 imports**, which the script's own header names as *"a defect in this script, not a clean result"*. Plus a hard-coded `+ '\'` for `$(MSBuildThisFileDirectory)`. |
| `audit-declarations.ps1` | Runs, but `-notmatch '\\(obj\|bin)\\'` admits generated `obj/**/*.cs` into the `using` scan → false findings. |
| `install-skills.ps1` | `New-Item -ItemType Junction` is **Windows-only**; Linux needs a symlink. The one genuinely OS-shaped difference rather than a path-separator one. |

⚠ **This is [[TASK-474]] a second time.** That task's literal-tab `'Framework\tests'` and this task's
Linux `'Framework\tests'` are the same expression failing for two different reasons, and both defeat
the same `if (-not $buckets) { throw }` guard. *A guard for "none" is not a guard for "fewer than
asked for"* — recorded there, unfixed here.

## Decision — .NET 10 file-based apps, measured against the alternatives

| Shape | Needs on a Linux box | First run | After an edit | Unchanged |
|---|---|---|---|---|
| `.ps1` | **pwsh** — cannot be guaranteed | 4.2s | 4.2s | 4.2s |
| `.csx` (dotnet-script) | **`dotnet tool install -g dotnet-script`** — a new dependency | ~2s | 2.0s | 0.9s |
| **`.cs` file-based app** | **nothing — SDK only, already required** | ~13s¹ | 1.0s | 0.6s |

¹ first-ever run on a machine seeds the build directory; thereafter the right-hand columns hold.
All measured 2026-09-19 on the 173-projitems tree, BCL only, no `#:package` directives.

`dotnet run audit-dependencies.cs` needs nothing a Birko build does not already need — the SDK is
`10.0.401` and is the *only* SDK installed, and `audit-dependencies` already shells out to
`dotnet list package`. One implementation, no pwsh, and **4–7× faster than the PowerShell it replaces**.

- ⚠ **The `csharp-script` skill's *"never in the repo tree"* rule does not forbid this, and reading it
  carefully is what selected the shape.** That rule is about loose `.csx` scratchpad files that need a
  tool install; it says the alternative is *"a real project with a recorded decision"*. A committed
  file-based app is exactly that — minus the `.csproj` and `.slnx` registration a console project would
  drag in. The recorded decision is `docs/adr/ADR-002`.
- **`.csx` was rejected on the dependency, not on speed.** It is the faster cold start of the two, and
  the `~15–20s` figure in the skill did not hold here — that figure assumes a `#r "nuget:"` restore.
  It loses on needing `dotnet tool install -g dotnet-script` on machines nobody can survey.
- **Porting to C# fixes none of the path bugs by itself.** `Path.Combine(root, "Framework\\tests")` is
  exactly as wrong on Linux as the PowerShell. Every one of the ten path expressions is fixed by hand
  in the port; the language change is what removes the pwsh dependency, nothing more.

## One producer for the path handling

Path handling is what broke, so it does not get copied into four files. `#:project` from a file-based
app is **verified working** (2026-09-19), so the shared helpers live once in `tools/AuditCommon/` and
each script references it. Named outside `Birko.*` deliberately: `audit-declarations` enumerates
`Birko.*` directories at the repo root, and a helper project called `Birko.Audit.*` would be swept by
the audit it exists to serve.

## Faithfulness fixture — captured BEFORE the originals were deleted

The port is proved against the live output of the scripts it replaces, captured 2026-09-19:

- `audit-declarations` → `0 (project,package) pairs across 0 projects`
- `audit-consumer-versions` → `24 packages across 22 shared projects`, 8 consumers importing:
  BardStudio 32 / Birko.Sandbox 207 / DraCode 41 / gameshow-app 13 / Latent 1 / Presenter 20 /
  Symbio 102 / WorkoutTracker 24; CPM yes for BardStudio, Latent, Presenter, Symbio; inherited
  2 / 0 / 3 / 16; verdict `No consumer declares a framework-owned package.`
- `audit-dependencies` → see § Outcome (248 projects, captured in the same session)

⚠ **A rewritten checker is a NEW checker.** Each original header carries *"verify the check can fail
before believing it"*, and between them they encode four measured defects — the magenta-over-green
bug ([[TASK-473]]), the dropped `tests/` bucket ([[TASK-474]]), the CPM blind spot found the day
Symbio adopted it, and the floors-vs-`PINNED` verdict that *changed a result*. Reproducing the numbers
above is necessary and **not sufficient**: each defect is re-proven against its own fixture below.

## Acceptance criteria

- [x] Four `.cs` file-based apps replace the four `.ps1`, which are deleted
- [x] `tools/AuditCommon/` is the single producer for path handling, bin/obj filtering and reporting
- [x] Every path expression is OS-neutral — no separator literal outside `AuditCommon`
- [x] Each audit reproduces its captured baseline **exactly** on Windows (one deliberate exception, below)
- [x] Each inherited defect is re-proven: it must be possible to make the check fail
- [x] `install-skills.cs` creates a junction on Windows and a symlink on Linux, idempotently
- [x] `CLAUDE.md` and `CLAUDE-maintenance.md` updated; closed task files left as the record
- [x] `docs/adr/ADR-002` records the decision
- [x] `tests/AuditCommon.Tests` covers the shared path helpers, registered in `.slnx` +
      `.code-workspace` so **CI runs it on ubuntu-latest**
- [x] The rule is recorded, not just the event — `CLAUDE-maintenance.md` states how repo tooling
      is written from now on

## Outcome — 2026-09-19

**All four ported, all four verified against the output of the script they replace.**

| Script | Baseline match | Fail-fixture |
|---|---|---|
| `audit-declarations.cs` | **identical** (0 pairs / 0 projects) | mutated a `TASK-234` comment in `Birko.BackgroundJobs.ElasticSearch.projitems` → reported exactly that project + `NEST`, nothing else |
| `audit-consumer-versions.cs` | **identical** — 24 packages / 22 shared projects, all 8 consumers' projitems counts | set Symbio's CPM `Npgsql` to `9.*` → **1 BELOW**, naming `Directory.Packages.props` as the holding file and `Birko.Data.SQL.PostgreSQL` as owner |
| `audit-dependencies.cs` | **one line differs, deliberately** — see below. 248 projects, 4 findings across 4 projects, 0 unauditable, identical grouping and ordering | partial-root fixture → refuses, names the missing bucket, exit 1 |
| `install-skills.cs` | reads the junctions the `.ps1` created; idempotent | removed one link → recreated a real `<JUNCTION>`, unelevated, then idempotent again |

Both mutated files were restored and confirmed byte-identical to `HEAD`.

- **⚠ The single diff line is PowerShell corrupting its own output, and the port fixes it.** The
  `.ps1` source contains an em dash (`E2 80 94`); the captured baseline contains an ASCII hyphen. The
  console encoding downgraded it on redirection — so every logged run of that script has been quietly
  mangling non-ASCII for as long as it has existed. The C# version emits what the source says.
- **⚠ TASK-474's leftover is CLOSED, not carried.** The original threw only when EVERY bucket was
  missing. Now each declared bucket is checked, each missing one named, and the run stops — the
  fixture (a root with `Consumers` but no `Framework/tests`) reproduces the exact 81-of-248 shape and
  refuses. *A guard for "none" is not a guard for "fewer than asked for."*
- **Sort determinism was incidental and is now explicit.** PowerShell's `Sort-Object` is not stable
  without `-Stable`, so the finding order of two equal-severity, equal-count packages was luck. The
  port adds an ordinal tiebreak; it produces the same order, now by construction.
- **Two decisions changed during the build**, both in ADR-002: `install-skills` does NOT use the
  portable `Directory.CreateSymbolicLink` on Windows (a directory symlink needs Developer Mode or
  elevation; a junction needs neither — the portable call would have broken the platform the script
  already worked on), and a bad argument exits **2**, distinct from `--fail-on-finding`'s 1.
- **Robustness beyond parity, deliberately:** a version component too large for an `int` crashed the
  PowerShell `[int]` cast; it now routes to UNPARSEABLE, which the caller already knows how to report
  as "could not be compared" rather than as clean.
- Measured: `AuditCommon` builds with **0 warnings** under `TreatWarningsAsErrors`; nothing globs the
  root `.cs` files into any project; build output is already covered by `.gitignore`.

### The gate found something against this change, and it mattered

`verify-birko-conventions` step 0b (*register-on-introduce*) flagged that the diff established a
new cross-cutting pattern — repo tooling as a file-based app with a shared `#:project` helper —
recorded only as an *event* (a `Recent Updates` entry) and not as a *rule*. Now stated in
`CLAUDE-maintenance.md`, beside the audits: **tooling committed at the aggregator root is a `.cs`
file run with `dotnet run`, and its path handling has one producer.** Placed there rather than in
`CLAUDE-conventions.md` on the [[TASK-229]]/[[TASK-234]] precedent — that file holds runtime-code
rules — and noted so a later sweep does not read it as a skipped promotion.

### ⚠ The Linux assertions cannot fail on Windows, which is the whole reason CI registration is in scope

**37/37 green**, and the mutation proves the suite bites: gutting `Paths.FromMsBuild` to return its
input reds **2 of 37**. But it reds `FromMsBuild_also_normalises_forward_slashes` and
`…mixed_separator_path` — **not** the backslash test, which passes *even when mutated* on Windows,
because the local separator already is a backslash. So the assertion that guards the exact defect
that broke `audit-consumer-versions` is **inert on a Windows dev box**.

That is why the test project is registered in `Birko.Framework.slnx` rather than left standalone:
`build-and-test.yml` builds the `.slnx` and then loops `tests/*/` with `--no-build` on
**ubuntu-latest**, so the assertions become real on every PR. Proved end-to-end rather than
assumed: the new projects' `bin`/`obj` were deleted, the `.slnx` alone rebuilt them, and CI's
exact `dotnet test "$p" --no-build` command then ran **37/37**. Full solution: **0 errors**, and
none of its 85 warnings come from the new projects.

## Out of scope

- **Running these in CI.** No workflow invokes them today (all 8 jobs are `ubuntu-latest`, none
  references a `.ps1`). Wiring `--fail-on-finding` into a scheduled job is a separate decision and
  gets its own task if wanted.
- **Verifying the shebang path on Linux.** .NET 10 file-based apps support `#!/usr/bin/env dotnet`,
  which would make these directly executable. Added, but untestable from Windows — needs confirming
  on a Linux box.

## Human test plan

⚠ **This section was absent, and an absent plan is not an `N/A` one.** The task was closed to
`review` on 2026-09-19 with no section at all, which parks it on a step that may not exist and is
afterwards indistinguishable from one that was written and never run. Resolved 2026-09-20 by writing
the step that is genuinely outstanding rather than back-dating an `N/A` the work does not support:
**this task is about Linux, and it has only ever been exercised on Windows.** § Out of scope above
already says as much about the shebang.

- [ ] On a Linux box (or the `ubuntu-latest` runner), from the repo root, run all four:
      `dotnet run audit-declarations.cs`, `audit-dependencies.cs`, `audit-consumer-versions.cs`,
      `install-skills.cs`. Expected: each completes and none reports a path it could not open.
- [ ] `audit-dependencies` reports **248** projects, not 81 — the [[TASK-474]] bucket regression is what
      this port was most likely to reintroduce, and it is invisible on Windows, where a backslash
      resolves either way.
- [ ] `audit-consumer-versions` reports a **non-zero** import count for a consumer known to import
      Birko. Its own header names the alternative as a defect in the script: *"a consumer you know
      imports Birko and that shows 0 is a defect in this script, not a clean result."*
- [ ] `install-skills` creates a working **symlink** on Linux (the junction path is Windows-only), and
      an edit to a skill here is visible through it immediately.
- [ ] `#!/usr/bin/env dotnet` makes each of the four directly executable — the one item § Out of scope
      explicitly left unverified.
