---
id: TASK-473
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: review
priority: P2
assignee: ai
created: 2026-09-19
depends-on: []
blocks: []
related: [TASK-229, TASK-230, TASK-234, TASK-474]
findings: []
pr: null
github-issue: null
jira-key: null
---

# A consumer can declare a package OLDER than the framework, and nothing says so

## Context

[[TASK-229]] settled that the shared project which *wraps* a library owns its `PackageReference`;
[[TASK-234]] applied it to 38 more projects and recorded the three shapes. All of that is about the
**framework side**. The consumer side was never stated, and it turns out not to be enforced by
anything either.

Surfaced 2026-09-19 from a Symbio build failure on a Linux box. The reported errors were 32 × `NU1605`
(a stale `Tmds.DBus` pin, already fixed by Symbio's TASK-737 and merely unpushed) — but underneath them
sat 7 × `NU1504` that nobody had chased, and one of those duplicates was **`Npgsql 9.*` against the
framework's `10.*`**. It had been in the tree for weeks, reported only as a duplicate.

**⚠ The interesting part is why restore cannot catch this, because it very nearly can.** "Consumer
declares something older than its dependency" is exactly `NU1605` *detected package downgrade*, which
is an **error by default** and costs nothing. But `NU1605` compares across a **package dependency
edge**. Birko ships `.projitems` — compiled *into* the consumer's assembly, no package identity, no
edge. So the framework's declaration and the consumer's are just two `PackageReference` items in one
project file, which is `NU1504`: a warning, about *duplication*, that says nothing about which side is
older. **The guard exists, is free, and is blind to precisely the shape the framework ships in.**

That is a previously unnamed cost of the "ship as real NuGet packages is deferred" decision
([[TASK-234]] § Out of scope). Shipping as packages deletes this task. Until then it needs a script.

## The policy (settled with the user 2026-09-19)

> A consumer must never declare a package version **lower** than the framework declares. **Higher** is
> allowed — the consumer then owns any breakage. **Equal** means the consumer should not declare it at all.

The mechanism differs per shape, and the middle one is the trap:

| Shape | What to do | Why it is not obvious |
|---|---|---|
| Equal | Delete the line; leave a comment naming the owning `.projitems` | pure `NU1504` |
| Higher | `<PackageReference Update="…" Version="…" />` **after** the `$(BirkoSrc)` imports | a second `Include` is a *duplicate*, not an override; and an `Update` placed **before** the imports is a **silent no-op**, because `Update` only reaches an item that already exists and an aggregator's imports sit at the bottom |
| Lower | refused — fix forward, or lower the **framework** declaration | re-pinning the consumer is the move the rule exists to prevent |

## Measurement — `audit-consumer-versions.ps1`, first run, 2026-09-19

8 consumers import framework `.projitems` (BardStudio 32, Birko.Sandbox 207, DraCode 41, gameshow-app
13, Latent 1, Presenter 20, Symbio 102, WorkoutTracker 24). **13 findings across 4 of them:**

| Verdict | N | Rows |
|---|---|---|
| **BELOW** | 3 | BardStudio `Microsoft.Data.Sqlite 9.0.3`, DraCode `9.0.4` — both vs `10.*`; Symbio `Npgsql 9.*` vs `10.*` |
| **PINNED** | 2 | DraCode `JwtBearer 10.0.0` vs `10.*`, `System.IdentityModel.Tokens.Jwt 8.7.0` vs `8.*` |
| **EQUAL** | 8 | Symbio ×7, WorkoutTracker `Microsoft.Data.Sqlite 10.*` |

**⚠ `PINNED` is a verdict the first version of this check did not have, and adding it changed a
result.** Comparing *floors* alone called DraCode's `10.0.0` identical to the framework's `10.*`. It is
nothing of the kind: the framework floats **deliberately**, so that a published advisory heals on the
next restore, and an exact pin freezes that — invisibly, when the pin happens to sit on the floor. Not
a policy violation, but a decision, and it was sitting in a file as an unremarked line.

**⚠ And the two SQLite rows are not hypothetical.** [[TASK-230]] established the remedy floor as
`Microsoft.Data.Sqlite ≥ 9.0.19 or ≥ 10.0.11`, to reach `SQLitePCLRaw.lib.e_sqlite3 ≥ 2.1.12` past a
**High** advisory. BardStudio declares `9.0.3` and DraCode `9.0.4` — both below **both** thresholds.
TASK-230 saw these as *"8 projects … carry their own `SQLitePCLRaw` 2.1.10"* and closed them as
consumer-owned rows to report. **They are the same defect seen from the symptom end**: one pin below
the framework's floor produces many advisory rows, and this audit names the line instead of the rows.
The resolved graph has not been re-checked here — that needs a restore per consumer, and it is the
first step of the consumer-side follow-ups below, not a claim this task makes.

## What was done

- **The rule**, in `CLAUDE-maintenance.md` § External dependencies — three bullets: the policy and its
  three shapes; the ⚠ on why `NU1605` cannot see a shared-project boundary; and `PINNED` as a decision
  rather than a violation.
- **`audit-consumer-versions.ps1`** — the third audit sibling, beside `audit-declarations.ps1` (does a
  shared project *account* for what it uses) and `audit-dependencies.ps1` (is anything *vulnerable*).
  Walks each consumer `.csproj`'s `$(BirkoSrc)` import graph transitively, collects what the framework
  declares, and compares floors. CPM consumers are compared against their `Directory.Packages.props`.
- **`CLAUDE-maintenance.md` § The three audits** — the three scripts side by side with the different
  question each answers, because a zero from one says nothing about the others.

## Proving the check can fail

Eight cases through a throwaway fixture (a fake `Birko.Fake.projitems` declaring `Fake.Driver 10.*`
and a consumer declaring against it), each asserted to produce exactly one verdict:

| Consumer declares | Verdict |
|---|---|
| `Include=9.*` / `Include=9.0.3` | BELOW |
| `Include=10.*` | EQUAL |
| `Include=10.0.0` | PINNED |
| `Include=11.*` | HIGHER-VIA-INCLUDE |
| `Update=11.*` | *clean* — the supported override |
| `Update=9.*` | **BELOW** — the override mechanism does not let a consumer escape the policy |
| `Include=bogus` | UNPARSEABLE |
| *(no declaration)* | clean |

**⚠ The fixture found a real defect in the checker itself, before it was ever trusted.** On
`Include=bogus` it printed the magenta *"1 declaration COULD NOT BE COMPARED — treat as unknown"* and
then a **green** *"nothing below, nothing duplicated"* underneath it. That is exactly the failure
`audit-dependencies.ps1`'s own header warns about — *a checker that reads "could not check" as "nothing
to report" is worse than no checker, because it is trusted* — reproduced in the sibling written to sit
beside it. An unknown now suppresses the green line and exits 1 under `-FailOnFinding`.

## Out of scope

- **The consumer-side fixes. Each is that consumer's own commit**, per [[TASK-230]]'s precedent
  (*"Consumer-owned rows: reported, not edited"*). Named here so they are not lost:
  - **Symbio** — 1 BELOW + 7 EQUAL + a wrong-cased `Birko.Data.Elasticsearch` import path that cannot
    resolve on Linux. A handoff prompt was written for it on 2026-09-19; it is filed in Symbio's own
    `tasks/EPIC-027-infrastructure-improvements/`.
  - **DraCode** — 1 BELOW + 2 PINNED. Also the largest single contributor to TASK-230's leftover rows.
  - **BardStudio** — 1 BELOW (`Microsoft.Data.Sqlite 9.0.3`, CPM consumer).
  - **WorkoutTracker** — 1 EQUAL.
- **Wiring the audit into a gate.** It takes `-FailOnFinding` and is ready for one, but 13 findings
  exist today, so turning it on now fails every run. It becomes a gate when the consumers are clean —
  and that ordering is the point, not an oversight.
- **Whether the framework should float at all.** Symbio's TASK-738 owns that decision. This task
  assumes the float and protects it; if 738 lands the other way, `PINNED` stops being a finding.
- **`audit-dependencies.ps1` sweeping only a third of the tree** — found while reading it for this
  task, fixed under [[TASK-474]].

## Human test plan

N/A — `audit-consumer-versions.ps1` is mechanical and its fixture cases are listed above.

## Implementation plan

_Populated by `/tasks plan TASK-473` — leave empty until then._
