---
id: TASK-229
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
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

- [ ] `Birko.Data.SQL.PostgreSQL.projitems` declares `Npgsql` with a floating version, restoring what
      `2faec6f` removed
- [ ] `Birko.Data.SQL.SqLite.projitems` declares `Microsoft.Data.Sqlite` with a floating version whose
      resolved `SQLitePCLRaw.lib.e_sqlite3` is **≥ 2.1.12** (see [[TASK-230]] — 2.1.11 is also vulnerable)
- [ ] The existing 6 are converted to floating too, or a reason is recorded for leaving them pinned — a
      mixed policy is what produced this task
- [ ] `README.md`'s aggregator example no longer lists framework-declared packages, and states which side
      owns what. Verified by building a project that follows the example and confirming **no NU1504**
- [ ] `CLAUDE-maintenance.md`'s new-project checklist records the rule, so the next backend does not drift
- [ ] The ~15 dependents that currently declare `Npgsql` / `Microsoft.Data.Sqlite` drop those lines —
      10 test projects for SQLite, 5 for Npgsql, plus `Birko.Sandbox`. **This must land with the declaration
      change, not after**: until it does, every one of them emits NU1504, which is an error under the
      framework's own lint
- [ ] A consumer that genuinely needs a different driver version is shown to still have a way to do it, and
      that way is documented — floating does not remove the need for an override, it changes its shape

## Out of scope

- Shipping the backends as real NuGet packages. Deliberately deferred by the user until the libraries are
  more stable; this task is forward-compatible with it and does not preempt it.
- The advisories themselves — [[TASK-230]].
- The 163 shared projects with no external dependency. They are correct as they are and are not "missing" a
  declaration.

## Human test plan

N/A — NU1504 and a clean `-warnaserror` build are mechanical checks.

## Implementation plan

_Populated by `/tasks plan TASK-229` — leave empty until then._
