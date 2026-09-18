---
id: TASK-229
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-17
depends-on: [TASK-228]
blocks: []
related: [TASK-210, TASK-230]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Two shared projects `using` a driver they do not declare — and three sources disagree about whose job it is

## Context

Surfaced while fixing [[TASK-210]]. That task's advisory was a one-line fix because
`Birko.Data.MongoDB.projitems` declares its own driver. The *same class* of advisory in SQLite needs edits
in **ten** repositories, because `Birko.Data.SQL.SqLite` declares nothing. The difference is not a
convention — nobody ever wrote one down.

**Measured 2026-08-17 across all 171 `.projitems`:**

| | Count |
|---|---|
| Declare a `PackageReference` | **6** — CosmosDB, InfluxDB, MongoDB, RavenDB, SQL.MSSql, SQL.MySQL |
| Declare none, and need none | 163 |
| **`using` a driver they do not declare** | **2** — SQL.PostgreSQL (`using Npgsql;`), SQL.SqLite (`using Microsoft.Data.Sqlite;`) |

Those two therefore **cannot compile unless the consumer supplies the package**, and every consumer
independently discovers this as a compile error.

**One of the two is an accident, provably.** `Birko.Data.SQL.PostgreSQL` *did* declare
`Npgsql` 9.0.2. It was removed on 2026-03-13 in `2faec6f`, a commit titled *"Refactor PostgreSQL connector
and store settings methods for improved null handling and override consistency"* — the package is not
mentioned in the message. Collateral damage in an unrelated refactor. `SQL.SqLite` never declared one.

**Three sources currently disagree**, which is the real defect:

| Source | Says |
|---|---|
| `README.md` § Usage in Consumer Solutions | the **consumer** declares them — its example names `Npgsql` *and* `MongoDB.Driver`, floating (`9.*`, `3.*`) |
| Practice | the **framework** declares 6, the consumer declares 2 |
| `CLAUDE-maintenance.md` new-project checklist | nothing at all — it covers `.shproj` GUIDs and is silent on dependencies |

The README is actively harmful, not merely stale: a consumer following it verbatim adds `MongoDB.Driver`
alongside the `.projitems` one and gets **NU1504 duplicate PackageReference** — measured, and it is a
*warning* on a normal build but an **error** under the `-warnaserror` that `verify-conventions` check 1
runs. Nothing collides today only because `Birko.Sandbox` did not follow the example literally: it declares
exactly the two the framework does not, and none of the six it does.

**The decision (2026-08-17): shared projects declare their own drivers, with floating versions.** Reasons,
in the order they carried weight:

- **Security blast radius is measured and one-directional.** Framework-declared → one line, one repo, every
  consumer fixed. Consumer-declared → N repos, and the framework cannot see or reach N. TASK-210 was one
  edit; its SQLite twin is ten *that we can see*.
- **It is not throwaway work if the family later ships real NuGet packages** (deferred until the libraries
  stabilise). A package's dependency list *is* the set declared here, so this is a step toward that, not a
  detour from it.
- **Floating (`9.*`) was chosen over pinning** so an advisory self-heals on the next restore rather than
  needing N edits. **The cost is accepted deliberately**: builds stop being reproducible from source alone
  and a bad upstream release lands without anyone opting in. With no lockfiles and CI on only 8 of 176
  repos, nothing would catch that — which is why the NuGet audit wiring ([[TASK-210]]) is the safety net
  this choice depends on, and should land with or before it.

## Acceptance criteria

- [x] `Birko.Data.SQL.PostgreSQL.projitems` declares `Npgsql` with a floating version, restoring what
      `2faec6f` removed
- [x] `Birko.Data.SQL.SqLite.projitems` declares `Microsoft.Data.Sqlite` with a floating version whose
      resolved `SQLitePCLRaw.lib.e_sqlite3` is **≥ 2.1.12** (see [[TASK-230]] — 2.1.11 is also vulnerable)
- [x] The existing 6 are converted to floating too, or a reason is recorded for leaving them pinned — a
      mixed policy is what produced this task
- [x] `README.md`'s aggregator example no longer lists framework-declared packages, and states which side
      owns what. Verified by building a project that follows the example and confirming **no NU1504**
- [x] `CLAUDE-maintenance.md`'s new-project checklist records the rule, so the next backend does not drift
- [x] The ~15 dependents that currently declare `Npgsql` / `Microsoft.Data.Sqlite` drop those lines —
      10 test projects for SQLite, 5 for Npgsql, plus `Birko.Sandbox`. **This must land with the declaration
      change, not after**: until it does, every one of them emits NU1504, which is an error under the
      framework's own lint
- [x] A consumer that genuinely needs a different driver version is shown to still have a way to do it, and
      that way is documented — floating does not remove the need for an override, it changes its shape

## Out of scope

- Shipping the backends as real NuGet packages. Deliberately deferred by the user until the libraries are
  more stable; this task is forward-compatible with it and does not preempt it.
- The advisories themselves — [[TASK-230]].
- The 163 shared projects with no external dependency. They are correct as they are and are not "missing" a
  declaration.

## Outcome

**All 8 storage backends now declare their own driver, floating within its major.** `Npgsql 10.*`,
`Microsoft.Data.Sqlite 10.*` (both new — `10.*` matches the framework's `net10.0`), plus the six existing
ones converted: Cosmos `3.*`, Influx `5.*`, Mongo `3.*`, Raven `7.*`, SqlClient `6.*`, MySqlConnector `2.*`.
**166 of 166 test projects build clean**, no `NU1504`, no compile break.

**The float paid for itself immediately: it exposed two more undeclared dependencies the task never knew
about.** Both failed *only* on the float, because a driver had been supplying them transitively:

| Shared project | Uses, undeclared | Was borrowing from |
|---|---|---|
| `Birko.Data.Repositories` | `Microsoft.Extensions.DependencyInjection` | `RavenDB.Client` ≤ 7.2.0 |
| `Birko.Data.Tenant` | `Microsoft.AspNetCore.Http` / `.Builder` / `.Routing` | `RavenDB.Client` ≤ 7.2.0 |

Both are the *same defect this task exists to fix*, so both were fixed at the root — DI **Abstractions**
(contracts, not the container, correct for a library) and a **`FrameworkReference`** (these ship in the
shared framework, so not a package) — rather than pinning RavenDB back to hide them. Raven 51/51,
Sync.RavenDB 9/9. **A pin would have preserved both defects indefinitely and looked like success.**

**It also exposed duplicates that were invisible by construction.** MSSql and MySQL test projects had been
declaring their drivers *alongside* the projitems all along, silent because both sides read `6.0.1`
verbatim. Changing one side to `6.*` made `NU1504` fire. Total cleanup: **22 duplicate declarations**
across 22 projects — 18 for Sqlite/Npgsql/DI, 4 for SqlClient/MySqlConnector.

**Advisories cleared as a side effect: 44 findings / 37 projects → 15 / 12.** `Microsoft.Data.Sqlite 10.*`
resolves `SQLitePCLRaw` **2.1.12**, so [[TASK-230]]'s largest row went from 27 projects to 6 — and the 6
left are consumers' own declarations, not the framework's. **29 findings across 25 projects fixed with no
per-project edit**, which is the self-healing the float decision was taken for, demonstrated on the first
restore.

**Two mistakes of mine, both caught before commit.**

- Adding the DI declaration to `Birko.Data.Repositories` — imported by **92** projects — gave `NU1504` to
  the 18 that already declared it. My first check said zero; that was a mangled regex, and a plain `grep`
  found the truth. **A clean result from a check you just wrote deserves one confirmation by another
  route.**
- **`audit-dependencies.ps1` read "could not restore" as "clean".** The float pushed `InfluxDB.Client` to
  5.1.0, which requires `Newtonsoft.Json ≥ 13.0.4` against `Birko.Sandbox`'s pinned 13.0.3 → `NU1605`,
  restore fails, **no vulnerability rows emitted** — and the script reported the sandbox as having no
  findings while it still had four. That is the same green-by-construction failure this backlog keeps
  finding, in a checker committed hours earlier. Fixed: unauditable projects are now reported as a distinct
  category and fail `-FailOnFinding`. Sandbox's own pin was floated to `13.*`.

**The consumer escape hatch is `Update`, not `Include`** — verified rather than assumed:
`<PackageReference Update="Microsoft.Data.Sqlite" Version="10.0.5" />` retunes the item the projitems
contributed, producing **no NU1504** and resolving to 10.0.5. `Include` would duplicate. The README now
shows this, and no longer tells consumers to declare drivers the framework owns.

**Spawned:** [[TASK-233]] — floating Cosmos to `3.*` moved 3.46.1 → 3.62.1, and TASK-220's *premise-pinning*
test failed exactly as its comment predicted: the SDK now translates the span-bound `Contains` natively,
rendering byte-identical SQL. So that rewrite may be retirable. The test was rewritten to assert
equivalence, keeping it a live pin in both directions.

## Human test plan

N/A — NU1504 and a clean `-warnaserror` build are mechanical checks.

## Implementation plan

_Populated by `/tasks plan TASK-229` — leave empty until then._
