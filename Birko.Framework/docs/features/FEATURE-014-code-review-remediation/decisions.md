---
id: FEATURE-014
created: 2026-06-18
---

# Code review — audit remediation — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | Critical findings ([[STORY-024]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-18 | ai | — (tracked in prose) |
| D2 | High findings ([[STORY-025]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-18 | ai | — (tracked in prose) |
| D3 | Medium findings ([[STORY-026]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-06-18 | ai | — (tracked in prose) |
| D4 | Low findings ([[STORY-027]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-06-18 | ai | — (tracked in prose) |
| D5 | Integration-test tier — the Docker-gated remediation findings ([[STORY-042]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `planned`. | 2026-07-14 | ai | — (tracked in prose) |
| D6 | Workflow backends — unify the serialization seam (ISerializer everywhere) ([[STORY-043]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `done`. | 2026-07-17 | ai | — (tracked in prose) |
| D7 | Spec-harvest — high findings ([[STORY-051]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`. | 2026-07-30 | ai | [[TASK-108]], [[TASK-109]], [[TASK-110]], [[TASK-111]], [[TASK-112]], [[TASK-113]], [[TASK-114]], [[TASK-115]], [[TASK-116]], [[TASK-117]], [[TASK-118]], [[TASK-125]], [[TASK-126]], [[TASK-128]], [[TASK-129]], [[TASK-137]], [[TASK-141]], [[TASK-207]], [[TASK-209]], [[TASK-212]], [[TASK-213]], [[TASK-214]], [[TASK-215]], [[TASK-218]], [[TASK-219]], [[TASK-220]], [[TASK-221]], [[TASK-222]], [[TASK-223]], [[TASK-224]], [[TASK-225]] |
| D8 | Spec-harvest — medium findings ([[STORY-053]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Decomposed for real on 2026-08-09 — one task per spec area, replacing the on-demand policy that had produced nothing. | 2026-07-30 | ai | [[TASK-151]], [[TASK-152]], [[TASK-153]], [[TASK-154]], [[TASK-155]], [[TASK-156]], [[TASK-157]], [[TASK-158]], [[TASK-159]], [[TASK-160]], [[TASK-161]], [[TASK-162]], [[TASK-163]], [[TASK-164]], [[TASK-165]], [[TASK-166]], [[TASK-167]], [[TASK-168]], [[TASK-169]], [[TASK-170]], [[TASK-171]], [[TASK-172]] |
| D9 | Spec-harvest — low findings ([[STORY-054]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Decomposed for real on 2026-08-09 — one task per spec area. | 2026-07-30 | ai | [[TASK-173]], [[TASK-174]], [[TASK-175]], [[TASK-176]], [[TASK-177]], [[TASK-178]], [[TASK-179]], [[TASK-180]], [[TASK-181]], [[TASK-182]], [[TASK-183]], [[TASK-184]], [[TASK-185]], [[TASK-186]], [[TASK-187]], [[TASK-188]], [[TASK-189]], [[TASK-190]], [[TASK-191]], [[TASK-192]], [[TASK-193]], [[TASK-194]] |
| D10 | Spec-harvest — the three unrated areas ([[STORY-055]]) | approved | Backfilled: decomposed into tracked work, so the scope decision was taken. Story is `in-progress`; its remaining work became one task on 2026-08-09. | 2026-07-30 | ai | [[TASK-195]] |
| D11 | Work tracked directly on the epic, outside any story | approved | Backfilled: these tasks exist and are tracked, so the scope decision was taken. | 2026-06-18 | ai | [[TASK-131]], [[TASK-208]], [[TASK-226]], [[TASK-227]] |

**States:** `proposed` (fresh from grill, awaiting decision) · `approved` (build it) · `deferred` (not now — note unblock condition) · `changed` (approved but altered — record the delta) · `removed` (rejected / out of scope).

Only `approved` and `changed` rows generate tasks at `/feature decompose`. No row is terminal: a `deferred`/`removed` decision overturned by later evidence (incl. production feedback) is **reopened** by adding a *new* `proposed` row that links the superseded one — the old row is never deleted.

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-08-01 — **Ledger opened by backfill**, not by a `/feature new` interview. This feature was created to
  close the [[roadmap]] DV5 gap: `EPIC-014` had been tracked in `tasks/` since 2026-06-18 while this repo's
  `CLAUDE.md` committed to a family-wide `docs/features/` tree that did not exist.
- 2026-08-01 — What the rows above **do** claim: each names a real story or a real set of epic-direct tasks,
  and `→ Tasks` lists task IDs that exist. `Date` is the story's/epic's own `created`; `By` is the epic's
  recorded `owner`. State is `approved` because the work was decomposed and tracked — decomposition is
  the observable decision.
- 2026-08-01 — What they **do not** claim: no rationale text, alternative, or rejected option has been
  reconstructed. Where a real dated decision with reasoning exists it lives in `CHANGELOG.md` or
  `CLAUDE.md` § Recent Updates, which remain the authority for *why*. Rows carry no invented `deferred`
  or `removed` history, so the absence of such rows means "not recorded", not "never considered".
- 2026-08-03 — **D7** — [[TASK-137]] spawned from [[TASK-109]] while planning it: the empty-`NOT IN` → `1 = 1`
  rendering shipped 2026-07-27 is a false SQL-injection signal in query logs, by the same argument that led
  TASK-109 to *reject* `1 = 1` as its all-rows idiom. No state change — D7 is already `approved` and this is
  one more task realizing it, filed under STORY-051 alongside the other two non-`SH-` tasks the remediation
  itself produced ([[TASK-128]], [[TASK-129]]). Blocked on TASK-109, because dropping an always-true term can
  leave a `DELETE` with no `WHERE` and must reach TASK-109's deliberate-all-rows path rather than its refusal.
- 2026-08-03 — Also spawned from TASK-109, but **deliberately outside this feature**: [[TASK-138]]
  (`ReadAsync()` with no arguments does not compile — CS0121). Filed to `tasks/_loose/` with `feature: null`,
  because it is an API-ergonomics papercut rather than code-review remediation. Recorded here so the spawn is
  traceable from the feature the origin belongs to, without widening this feature's scope to cover it.
- 2026-08-06 — **D7** — [[TASK-109]] closed (SH-H002 + SH-M023): a null or untranslatable filter no longer
  renders a whole-table `DELETE`/`UPDATE`. Two findings, one decision — the SQL native paths and the portable
  bases shared the defect, so one policy took two edits. No state change; D7 stays `approved`.
- 2026-08-06 — **D7** — [[TASK-141]] spawned at TASK-109's **close gate**, not during its coding: MongoDB's
  four repeated null-filter guards have no test, while the InMemory half of the same sweep was *discovered*
  by a failing one. Adjacent scope rather than an unmet criterion (criterion 9 named the SQL and portable
  suites), so it is filed rather than folded in. No state change — one more task realizing D7.
- 2026-08-07 — **D7** — [[TASK-116]] closed (SH-H041 + SH-H042 + SH-H043 + SH-H044): a rule leaf that cannot
  be evaluated no longer widens the filter to every row. Four findings, one root cause, plus two unfiled
  sites of the same species pulled in. The fix also moved `Birko.Rules`' in-memory evaluator onto match-none
  for a string operator against a non-string member — a **user decision**, taken because the expression path
  cannot portably stringify a column and the two engines had to converge somewhere. No state change; D7
  stays `approved`.
- 2026-08-07 — **D7** — [[TASK-125]] closed (SH-H036): the ordered `ReadOne` no longer reads around the
  store decorator chain, so it can no longer return another tenant's row. The bypassing extension was
  removed rather than repaired — `Store` is `protected`, so an extension cannot reach the decorated chain
  and the capability is only implementable safely as an instance method. No state change; D7 stays
  `approved`.
- 2026-08-07 — **D7** — [[TASK-126]] closed (SH-H019): `TagServiceBase` now re-checks the tenant of every
  record its data-access hooks return — throwing on a by-identity load, filtering a collection — instead
  of depending on each implementor to filter. Hardening rather than a reproduced leak: the framework ships
  no implementation of the base. No state change; D7 stays `approved`.

- 2026-08-09 — **D8 / D9 / D10** — decomposed by `/tasks intake --epic EPIC-014` into **45 tasks**
  ([[TASK-151]]–[[TASK-195]]): 22 per-area triage tasks for the mediums, 22 for the lows, and one bounded
  task for the recovered unrated set. No state change; all three stay `approved` — this records *how* the
  approved scope is now tracked, not a change to what was approved.

  **Why now.** All three rows read `— (tracked in prose)` and their stories said "extract on demand". In the
  ten days since filing, **zero** extractions happened, and that is structural rather than a lapse: only
  `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot, or by `/fix-next`, so 808
  findings sat where no picker could see them. The `/roadmap` DV12 audit is what surfaced it — the rule
  exists precisely to catch a review that reads as drained while part of it was never scheduled.

  **Why per-area and not per-finding.** Findings in one area share a spec, a source-glob set and often a root
  cause, so they are fixed in one edit — the intake rule for what belongs in one task. 808 individual tasks
  would have been as unusable as none. Per-finding extraction still happens *inside* an area task, via
  `/tasks spawn`, when triage confirms something too large to fix there.

  **A constraint this created.** Each task carries an **explicit contiguous** `findings:` list, because that
  is what `/fix-next` greps to build its pool — a range string would match nothing. Appending new `SH-` ids
  past the current maxima stays safe; renumbering inside an existing range now silently invalidates up to 44
  files at once. [[TASK-195]] carries that constraint as an acceptance criterion.
- 2026-08-16 — **`→ Tasks` backfilled for 15 tasks** filed since 2026-08-11: D7 gains TASK-207, 209,
  212–215 and 218–225; D11 gains TASK-208. Raised by [[roadmap]] as **DV9** — each already carried
  `feature: FEATURE-014` and sat under STORY-051 (or the epic directly), so the work was tracked and the
  ledger simply had not been told. No decision state changed and no new row was needed: all fourteen D7
  additions are spec-harvest high findings, which is exactly what D7 approved. The gap is a `/tasks spawn`
  omission that recurs whenever a fix uncovers its successor mid-run, and it will recur again — the
  ledger's `→ Tasks` column has no writer other than a human remembering to update it.
- 2026-08-16 — **D11 gains [[TASK-226]]**, spawned from [[TASK-131]] and backfilled *at creation* rather
  than by a later audit — the first task to exercise the rule change that closed the gap named in the line
  above (`/tasks new` step 10b now fires on the `feature:` link instead of on `--from-feature`). Inside
  D11's approved scope: epic-direct spec-layer infrastructure, same as TASK-131 itself, so no new decision
  row. **TASK-131 also changed shape without changing state** — it set out to build per-sub-repo spec
  trees and measured that those cover 4 of 25 areas, so it fixed staleness with a per-sibling baseline
  instead and re-homed the per-sub-repo work as TASK-226. The decision D11 approved ("work tracked
  directly on the epic") is unaffected; the *mechanism* inside one of its tasks changed, which is a task
  concern and is recorded there.
- 2026-08-16 — **D11 gains [[TASK-227]]**, spawned while draining the DV7 backlog TASK-131 made visible.
  Backfilled at creation, same as TASK-226. Inside D11's scope (epic-direct spec-layer infrastructure), so
  no new decision row. It records a defect in the **generic** specs skill rather than in this repo:
  `generated-at` is stamped before the spec file is committed, so it always names the preceding commit and
  staleness is measured from too early — true for 25 of 25 areas here, and the reason TASK-131's first
  pass over-reported 15 stale areas where the real count is 6.
