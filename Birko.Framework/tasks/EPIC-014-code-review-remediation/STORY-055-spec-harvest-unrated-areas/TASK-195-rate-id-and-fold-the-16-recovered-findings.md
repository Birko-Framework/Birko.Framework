---
id: TASK-195
parent: STORY-055
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [CMC-1, CMC-2, CMC-3, CMC-4, SLI-1, SLI-2, SLI-3, SLI-4, SLI-5, SLI-6, UOW-1, UOW-2, UOW-3, UOW-4, UOW-5, UOW-6]
pr: null
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

- [ ] All 16 findings carry a confirmed severity, each justified against the high/medium/low bar rather than
      inherited from [[STORY-055]]'s proposal — including whether `UOW-1` really clears the high bar
- [ ] `SH-` ids assigned, **appended past** the existing `SH-H054` / `SH-M421` / `SH-L387` maxima. No id
      inside an existing range is renumbered, moved, or reused
- [ ] Duplicates cross-referenced, not double-filed. `SLI-4 → SH-L297` and `SLI-6 → SH-L298` at minimum; the
      other 14 are checked against the areas whose globs overlap `Birko.Data.Stores` and
      `Birko.Data.Patterns` before any id is minted
- [ ] The surviving findings are folded into `SPEC-HARVEST-FINDINGS-2026-07-30.md` under their severity
      sections, with a new `### area:` block per area, keeping the existing per-area ordering convention
- [ ] Header total corrected from **865** to the real number, with the per-severity split recomputed after
      duplicate removal — **not** assumed to be [[STORY-055]]'s predicted `881 (58 · 430 · 393)`, which
      counts `SLI-4`/`SLI-6` twice
- [ ] `finding-count` updated on [[STORY-051]], [[STORY-053]] and [[STORY-054]] to match
- [ ] The `## Coverage gaps` note rewritten — it currently says these three areas are *"complete"*, which is
      the sentence that hid the loss. State what actually happened: swept, results lost to a mid-project
      schema change, recovered from the journal on 2026-07-31
- [ ] Every newly-folded id is reachable from a `status: todo` **task** — either appended to an existing
      per-area task's `findings:` list, or, for the three areas that have none, a new per-area triage task
      spawned under the right severity story. No id lands as a checklist bullet
- [ ] [[STORY-055]] flips to `done` with its Progress line reflecting the final routing

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
