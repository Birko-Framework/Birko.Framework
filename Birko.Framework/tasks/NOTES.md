# Tasks — hand-written notes

_Companion to [README.md](README.md), which is **generated**. `/tasks triage` never writes this file:
everything here was typed by a person and has to survive a regeneration._

⚠ **Why this file exists (2026-09-26).** Until now these notes lived in `README.md`, whose banner says
*"Do not hand-edit"*. A regeneration from the template would have deleted them, which is exactly what
happened to Symbio on 2026-09-11 (~1000 lines of measured provenance). They were moved here **verbatim**
before the first full regeneration. Each keeps its own date. Nothing below was re-measured by the move.

---

## Standing callouts (moved verbatim from README.md, as of its 2026-09-20 refresh)

> ⚠ **Feature drift (3 groups, 111 items)** — **all three re-measured 2026-09-19**, and two grew
> since the 2026-09-16 pass.
> **DV9 ×74** — 199 tasks carry `feature: FEATURE-014` and its `decisions.md` names 128 TASK ids, so
> the ledger does not know about **74** of its own tasks (×64 on 2026-09-16, ×63 on 2026-09-09, ×62
> and ×59 earlier that week, ×31 on 2026-09-04). Measured rather than incremented, per § TASK-283.
> The reverse gap is unchanged: **3** ids in the ledger are not FEATURE-014 tasks ·
> **DV5 ×33** (every task in `_loose/` has no epic *and* no feature, so none appears in a feature row
> — was ×19; the 14 added are the `_loose` intake since 2026-09-16, TASK-468, TASK-469 and TASK-470 among them) ·
> **DV3 ×4** (TASK-285/286/287/288 sit under EPIC-014 with `feature: null` while every sibling links
> to FEATURE-014 — a broken back-link; unchanged).
> Run `/roadmap --check` for the full audit, or `/tasks audit --fix` for the safe ones.
>
> ⚠ **DV7 was NOT recomputed in this pass and is not being reported as clean.** Spec staleness needs a
> `git diff` per area against `generated-at` — a cost this dashboard refresh did not pay. Note the
> reason has changed since the last pass, which cited areas globbing *sibling repos*: [[TASK-457]]
> consolidated the framework into one repo on 2026-09-18, so `generated-at` can now observe a source
> change for the first time and the measurement is newly *worth* taking. The last measured value was
> **DV7 ×3** (`filter-expression-translation`, `bulk-filter-operations`,
> `unit-of-work-and-transactions`), owned by [[TASK-251]]. Do not carry the previous pass's "very
> likely ×1" forward as a number — it was arithmetic on a stale total then, and the monorepo has since
> moved the baseline underneath it.
> DV8/DV10/DV11 were clean at the last measurement: all 25 mapped areas exist on disk and carry
> `shaped-by-derived: true`.
>
> ℹ **Known false positive, left as-is:** EPIC-018 reads `in-progress` with all 4 tasks `done`. It is
> an area-of-concern epic giving `Birko.Web.Core` an owner, and closing it would recreate the orphaning
> it exists to prevent — the reasoning is in its own `EPIC.md` (§ *Why this epic stays `in-progress`
> with no open tasks*), which is why this dashboard no longer restates it.

_Generated 2026-09-20 (`/tasks new TASK-479`/`TASK-480`). **Statuses and the priority breakdown were
recounted from the files; the feature-drift block above was NOT re-measured in this pass** and carries
its own 2026-09-19 dating. Run `/tasks triage` for a full refresh. **Do not hand-edit** — changes will
be overwritten._

> ⚠ **The previous refresh reported `review: 0` while four tasks were sitting in it**, and `todo: 157`
> against an actual 159 before this pass added two. Both are the dashboard being older than the tree
> rather than a miscount at the time — [[TASK-473]], [[TASK-474]], [[TASK-476]] and [[TASK-477]] all
> closed to `review` on 2026-09-19 after it was generated. Verification debt that the snapshot is
> supposed to surface is exactly what goes missing when a dashboard is not refreshed on close, so it is
> recorded here rather than quietly corrected.


## Notes that sat under the counts table (moved verbatim)

> ℹ Recounted from the files on each refresh rather than incremented — a breakdown that sums correctly
> is not thereby correct (before TASK-315 this line carried a P0 that had just been closed, and was
> one P3 short, while still totalling right). No P0 is open.
>
> ⚠ **The review queue is empty for the first time since it was introduced** — the 11 tasks this
> dashboard listed on 2026-09-17 were all signed off on 2026-09-19 (TASK-035, TASK-038, TASK-091,
> TASK-118, TASK-201 and the rest). That is a drained queue, not a dropped section: `review` is
> rendered in the counts table at 0 rather than omitted, so the distinction stays visible.
>
> ⚠ **[[TASK-235]] was cancelled 2026-09-19, not completed** — its subject is
> `FisData.Stock.Angular.Server`, and Symbio is FisData's successor, so the net10 migration it was
> `blocked` on will never land. That is the only `blocked` task gone; **TASK-148 remains the single
> blocked entry**. The file is kept, per the tree's never-delete rule, because the process lesson in
> its § Context (sweep consumers by ownership, not by path glob) outlives the consumer.
