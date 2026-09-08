---
id: TASK-195
parent: STORY-055
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [CMC-1, CMC-2, CMC-3, CMC-4, SLI-1, SLI-2, SLI-3, SLI-4, SLI-5, SLI-6, UOW-1, UOW-2, UOW-3, UOW-4, UOW-5, UOW-6]
pr: "documentation + routing only — no production code changed"
github-issue: null
jira-key: null
---

# Rate, ID and fold the 16 recovered findings into the severity backlog

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09. [[STORY-055]] carried its remaining work as
unticked acceptance criteria with no task behind them, so nothing scheduled it — a checklist line is filed,
not scheduled. This task is all of that remaining work; it is bounded, and it is the whole of it.

**What already happened.** The 2026-07-30 harvest swept 25 areas but aggregated by `severity`, a field the
first pass's output schema did not have. Three areas ran only under the old schema — `core-model-contracts`
(4), `store-lazy-initialization` (6), `unit-of-work-and-transactions` (6) — so their findings matched no
section of `SPEC-HARVEST-FINDINGS-2026-07-30.md` and survived only as a count in a note that reads
*"complete"*. All 16 were recovered verbatim from the workflow journal on 2026-07-31 and committed to
[`RECOVERED-FINDINGS.md`](RECOVERED-FINDINGS.md), off that perishable source. Nothing needs re-sweeping:
`/specs regen` is diff-based and none of these areas' sources have moved since the harvest.

**What is left** is a documentation and routing pass over one file — no production code changes.

**The severity split is a proposal, not a verdict.** [[STORY-055]] proposes **1 high** (UOW-1 — an
ElasticSearch commit failure that leaves the buffer queued and the UoW active, so a retry double-applies
items that already succeeded), **9 medium**, **6 low**. The harvester emitted no severity for any of them,
so each rating has to be justified against the same bar the other 865 were rated on: high means silent data
loss, cross-tenant leakage, auth bypass, or a destructive op on the wrong rows.

**Two duplicates are already known, and they are exact.** `SLI-4` (non-volatile `_initialized` read outside
the lock) is `SH-L297`, and `SLI-6` (undisposed `SemaphoreSlim _initLock`) is `SH-L298` — both filed under
`store-crud-contract` because that area's globs include the same `AbstractStore.cs` / `AbstractAsyncStore.cs`
files. `SH-L297` even says so in its own body: *"(Also reported under store-lazy-initialization, which shares
these files.)"* Those two are cross-referenced, not re-filed, and [[TASK-182]] already owns them. Check the
other 14 for the same shape before assigning any id — areas whose source globs overlap will have produced
overlapping findings elsewhere too.

**A renumbering constraint this intake just created.** [[TASK-151]]–[[TASK-194]] each carry an **explicit,
contiguous** `findings:` list (that is how [[fix-next]] builds its pool — a range string would match
nothing). Appending new ids past `SH-M421` / `SH-L387` is safe; **inserting or renumbering anywhere inside
the existing ranges silently invalidates up to 44 task files at once.** [[STORY-055]]'s "do not renumber"
criterion was already there; this is now the concrete cost of breaking it.

**And a routing gap the fold-in will expose.** The 44 per-area triage tasks cover the **22** areas that had
medium/low findings. These three areas are precisely the ones that did not, so they have **no triage task**.
Folding 16 ids in without creating one leaves them in exactly the state this whole intake existed to fix:
filed, and scheduled by nothing.

## Acceptance criteria

- [x] All 16 findings carry a confirmed severity, each justified against the high/medium/low bar rather than
      inherited from [[STORY-055]]'s proposal — including whether `UOW-1` really clears the high bar
- [x] `SH-` ids assigned, **appended past** the existing `SH-H054` / `SH-M421` / `SH-L387` maxima. No id
      inside an existing range is renumbered, moved, or reused
- [x] Duplicates cross-referenced, not double-filed. `SLI-4 → SH-L297` and `SLI-6 → SH-L298` at minimum; the
      other 14 are checked against the areas whose globs overlap `Birko.Data.Stores` and
      `Birko.Data.Patterns` before any id is minted
- [x] The surviving findings are folded into `SPEC-HARVEST-FINDINGS-2026-07-30.md` under their severity
      sections, with a new `### area:` block per area, keeping the existing per-area ordering convention
- [x] Header total corrected from **865** to the real number, with the per-severity split recomputed after
      duplicate removal — **not** assumed to be [[STORY-055]]'s predicted `881 (58 · 430 · 393)`, which
      counts `SLI-4`/`SLI-6` twice
- [x] `finding-count` updated on [[STORY-051]], [[STORY-053]] and [[STORY-054]] to match
- [x] The `## Coverage gaps` note rewritten — it currently says these three areas are *"complete"*, which is
      the sentence that hid the loss. State what actually happened: swept, results lost to a mid-project
      schema change, recovered from the journal on 2026-07-31
- [x] Every newly-folded id is reachable from a `status: todo` **task** — either appended to an existing
      per-area task's `findings:` list, or, for the three areas that have none, a new per-area triage task
      spawned under the right severity story. No id lands as a checklist bullet
- [x] [[STORY-055]] flips to `done` with its Progress line reflecting the final routing

## Closed 2026-09-08 — 16 recovered → **11** folded, 5 duplicates, 0 high

A documentation and routing pass, as scoped: **no production code changed.** Every criterion above is met.
What the pass actually found, in the order it mattered:

### 1. All 16 were re-verified against the code before being rated

39 days had passed since recovery and the framework has moved a great deal, so each finding was checked
against current source rather than rated from its text. **All 16 still describe the shipped code** — none
had been fixed, none had gone stale. That is worth stating because it was not a foregone conclusion:
[[TASK-270]] changed re-entrancy handling in `DataBase`, [[TASK-288]] added
`CanTrustRememberedInitialization` to the store bases, and neither touched these.

### 2. ⚠ Five were duplicates, not two — and the three extra were found by the check this task required

The task file predicted `SLI-4`→`SH-L297` and `SLI-6`→`SH-L298` and then instructed: *"Check the other 14
for the same shape before assigning any id."* Doing so found three more:

| Recovered | Already filed as | Owner |
|---|---|---|
| `SLI-1` (Destroy cannot reset the init latch) | `SH-M307` — same file, **same line 48**, same mechanism | [[TASK-163]] |
| `SLI-2` (sync `InitCore` recursion → StackOverflow) | `SH-M316` | [[TASK-163]] |
| `SLI-3` (async `InitCoreAsync` deadlock) | `SH-M316` — one finding spanning both halves | [[TASK-163]] |

`SH-M316` is titled *"InitCore re-entrancy fails two different ways in the sync and async bases for
identical subclass code"*, so it deliberately covers both and **two recovered ids collapse into one
existing finding**. That is the only one of the five that needed judgement rather than a match.

**The check that found them is generalisable:** the findings doc was searched for every file the recovered
findings name. `AbstractStore.cs` / `AbstractAsyncStore.cs` returned 15 existing ids (because
`store-crud-contract` globs the same files), while `AbstractModel.cs`, `AbstractLogModel.cs`,
`SqlUnitOfWork.cs`, `SqlTransactionContext.cs` and `ElasticSearchUnitOfWork.cs` returned **0**. So the
duplication is confined to the two store bases, and the other 10 findings are provably new — a much
stronger statement than "I looked and did not see any".

### 3. ⚠ `UOW-1` does not clear the high bar, so the recovered set contains NO high finding

The task asked explicitly *"including whether `UOW-1` really clears the high bar"*. Answer: **no**,
downgraded to medium as `SH-M426`, on two measurements:

- **Every operation the UoW can buffer is idempotent.** `BulkOperationContext` offers exactly three —
  `Index` (full document), `Delete` (by id) and `Update` (with `Doc(partialDocument)`, **not** a script) —
  and there is no `create`. So the filed consequence, *"a retry double-applies succeeded items"*, re-sends
  a value identical to the one already stored. Materially harmless.
- **Partial application is documented, not silent.** The class's own doc comment reads *"This is NOT a true
  ACID transaction. Individual operations within the bulk may succeed or fail independently"*, and the
  caller receives an exception from the failed commit.

What remains is real — `_context = null` still sits after both throw sites, so the UoW stays `IsActive`
with an intact buffer and no per-item outcomes to drive a targeted retry — but it is not *silent* data
loss, which is the bar. **Consequence: [[STORY-051]]'s `finding-count: 57` is unchanged and its 15-task
decomposition (filed hours earlier) still covers the high tier completely.** That is recorded on STORY-051
as *"no change, for this reason"* rather than left as an omission.

### 4. The predicted total was wrong in both directions

[[STORY-055]] predicted **881** (`58 · 430 · 393`). The answer is **876** (`57 · 428 · 391`): −2 for the
duplicates it knew about but still counted, −3 for the duplicates it did not know about, and the high it
predicted moved to medium. The task file had anticipated the first correction and not the others.

### 5. Routing — 5 new tasks, because these three areas had none

The 44 per-area tasks under [[STORY-053]]/[[STORY-054]] cover the 22 areas that had rated findings, and
these three are precisely the ones that did not, so folding ids in alone would have left them *"filed, and
scheduled by nothing"* — the exact state this epic's intake exists to remove.

| Task | Story | Area | Findings | Priority |
|---|---|---|---|---|
| [[TASK-323]] | STORY-053 | `core-model-contracts` | `SH-M422`–`SH-M424` | P2 |
| [[TASK-324]] | STORY-053 | `store-lazy-initialization` | `SH-M425` | **P1** |
| [[TASK-325]] | STORY-053 | `unit-of-work-and-transactions` | `SH-M426`–`SH-M428` | **P1** |
| [[TASK-326]] | STORY-054 | `core-model-contracts` | `SH-L388` | P2 |
| [[TASK-327]] | STORY-054 | `unit-of-work-and-transactions` | `SH-L389`–`SH-L391` | P2 |

**They are named "Fix", not "Triage", and the distinction is load-bearing.** Their 44 siblings begin with
confirm-or-refute; these arrive pre-verified, so a triage step would be busywork and the task would invite
someone to re-do what this pass did. Each says so explicitly.

Priorities are measured, not assumed: `SqlUnitOfWork` is named in **3** consumer `.cs` files (so
`SH-M427`'s pooled-connection leak per failed `BeginAsync` is live), `SH-M425` sits on the base every async
store inherits, and the `core-model-contracts` trio has **0** `.CopyTo(` call sites anywhere in the
framework's non-test code.

**Verified mechanically**, which is what the Human test plan asked for: all 11 new ids resolve to a
`status: todo` task's `findings:` list, the id set went 865 → 876 with **0 lost**, and both severity
sections remain alphabetically ordered by area (25 and 24 areas).

### ⚠ One cross-link that changed a finding's reachability

`SH-M428` (a failed commit skips `CleanupAsync`) names a deadlock-victim commit as its trigger. That was
hypothetical when filed; [[TASK-306]] reproduced it on live SQL Server 2022 earlier the same day — error
**1205**, *"Rerun the transaction"*, 1 run in 12 under CPU load. Noted on the finding and on [[TASK-325]],
because a measured trigger is the difference between a theoretical cleanup gap and one this tree has seen.

## Out of scope

- **Fixing anything.** [[STORY-055]] ends when the findings are rated, ID'd and routed; the fixes belong to
  whichever severity story receives them. `UOW-1` in particular is a real defect and is *not* fixed here.
- Re-sweeping the three areas. Their sources last moved 2026-03-26 / 2026-03-10 / 2026-07-14, so a regen
  produces an identical spec and reports nothing — that is why the journal recovery was used instead.
- Re-verifying the other 865 findings' severities. Only the 16 are rated here.
- The per-sub-repo spec trees that would make these areas' staleness measurable from their own repos —
  [[TASK-131]].

## Human test plan

N/A — this is a documentation and routing pass over `SPEC-HARVEST-FINDINGS-2026-07-30.md` and task
frontmatter. There is no runtime surface, and the routing criterion above is machine-checkable: grep every
newly-minted `SH-` id and confirm it appears in some task's `findings:` list.

## Implementation plan

_Populated by `/tasks plan TASK-195` — leave empty until then._
