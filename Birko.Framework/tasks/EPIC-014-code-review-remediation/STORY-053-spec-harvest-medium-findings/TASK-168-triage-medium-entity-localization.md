---
id: TASK-168
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-09
depends-on: []
blocks: [TASK-679, TASK-680]  # EPIC-021 (Symbio STORY-081): triage before building on Birko.Data.Localization
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M070, SH-M071, SH-M072, SH-M073, SH-M074, SH-M075, SH-M076, SH-M077, SH-M078, SH-M079, SH-M080, SH-M081, SH-M082, SH-M083]
pr: null
github-issue: null
jira-key: null
---

# Triage the 14 medium spec-harvest findings in `entity-localization`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **14** findings
that fall in the `entity-localization` area.

**Findings:** `SH-M070`–`SH-M083` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: entity-localization`, lines 874–959.

**The contract under review** is specced in [`docs/specs/entity-localization.md`](../../../docs/specs/entity-localization.md), harvested from
1 source glob — `../Birko.Data.Localization/**/*.cs`. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/entity-localization.md` lying until `/specs regen entity-localization` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 14 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen entity-localization`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 407 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-194]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A — fully covered by automated tests. `Birko.Data.Localization` is a library contract with no UI surface, and no
consumer calls the wrappers yet; every behaviour changed here is asserted by `LocalizationMediumFindingsTests`.

## Implementation plan

Drafted 2026-10-19 from a read of all four wrappers, the analyzer and the order-by helper. Line numbers in the
findings predate SH-H015–H018 and TASK-555, so every verdict cites today's lines.

**Ownership.** EPIC-021 rewrites the code four findings sit in, and TASK-168 blocks it. Those four are verified
here and **handed over**, not fixed twice:

| Finding | Read in code | Goes to |
|---|---|---|
| M070 no base-value fallback in filters | `ResolveMatchingGuids` builds the set from translation rows only (`LocalizedStoreWrapper.cs:197-226`, ×4) | TASK-681 (its fallback criterion) |
| M082 whole field+culture slice read, all tenants | same method: no value term, predicate applied in memory | TASK-681 (push into SQL) + TASK-680 (tenant model) |
| M081 localized order-by loads everything, N+1 | `LocalizedBulkStoreWrapper.cs:86` reads with no limit; `Localize` is one query per entity | TASK-679 (batch); the unbounded read stays inherent to in-memory sort — record it in the spec |
| M074 upsert is read-then-create, no unique key | `SaveTranslations` `FirstOrDefault()` then `Create` (×4) | TASK-680 (unique key) |

**Fixed here** (one commit, regression tests red-verified by reverting each fix):

1. **M071** — `ApplyInMemoryOrderBy` branches on `ordered == null`, not `i == 0`.
2. **M080** — `ApplyInMemoryOrderBy` takes the context's `CultureInfo`; strings compare with its `CompareInfo`
   (test: Slovak `č` sorts after `c` and before `d`, which ordinal and invariant get wrong).
3. **M077 + M079** (one root cause: a condition on a localizable field that is not extracted reaches the base
   column) — after the split, a residual filter that still reads a localizable field **refuses** with
   `NotSupportedException` naming the field (rule 42). The documented `x.Name == null` / `!= null` base-column
   compare stays exempt and stays documented. `EvaluateExpression`'s bare `catch` goes: a non-evaluable operand
   is then simply not extracted, and the refusal reports it. Default culture is untouched (filter passes through).
4. **M078** — `Contains/StartsWith/EndsWith(string, StringComparison)` honour the comparison.
5. **M073** — a `null` localizable value under a non-default culture **deletes** that field's translation row,
   so the read falls back to the base value (the same rule TASK-682's `SetAsync` states).
6. **M076** — `Save` already returns the created GUID (TASK-555, `StoreSave`): that half is refuted as fixed.
   The remaining half: `Create` passes the GUID the inner store **returned** to translation persistence, so a
   store that does not write `data.Guid` back no longer drops the translations.
7. **M072** — translation rows are stamped with `typeof(T).Name` (what every query uses), not the runtime type;
   an upsert that finds a row under the old name rewrites its `EntityType`.
8. **M075** — filter-based `Update(filter, Action)` (sync and async) captures the action's translated values,
   restores the base values, lets the inner store write, and **only then** writes the translations.

Shared shape for 5–8: translation persistence becomes one internal helper (sync + async) taking
`(guid, entityType, culture, values)`, called by all four wrappers, instead of four edited copies.

**Found while verifying M075, confirmed and fixed here (no separate task, decided by the user 2026-10-19)** —
filter-based `Update` under a non-default culture handed the action the inner store's *untranslated* entity and
then saved **every** localizable field, so a field the action did not touch got its base (English) value written
over the real translation (measured: `Update(x => x.Code == "C1", e => e.Code = "C2")` on `sk` replaced the Slovak
`Name` with the English one). The `PropertyUpdate` fallback reached the same path. Fixed in the M075 rework, since it
is the same code: the action now sees the translated values and only the fields it changed are written. Regression
tests: `A_filter_update_that_does_not_touch_a_localized_field_keeps_its_translation`, its async twin, and
`A_filter_update_action_sees_the_translated_value` — 3 tests, each red under its own revert. Recorded in the findings
file beside SH-M075.

**Decide after tracing — M083** (no transaction across entity + translation writes): confirmed by reading (every
path is two independent store calls). Too large for a triage; `/tasks spawn` a task under EPIC-021 that uses
TASK-669's ambient-transaction machinery where the two stores share a connection.

**Close-out:** verdicts in `SPEC-HARVEST-FINDINGS-2026-07-30.md`, `/specs regen entity-localization`, STORY-053
progress line, Human test plan → `N/A — library contract, covered by automated tests`.

## Progress log

- 2026-10-19 — picked; plan written; all 14 findings read against today's code (line numbers predate SH-H015–H018).
- 2026-10-19 — 26 tests in `LocalizationMediumFindingsTests`, 21 red before the fix (plus a filter-update defect found
  while verifying M075, confirmed red). After the fix 152/152 green, no new warnings.
- 2026-10-19 — red-verified by reverting each fix: **22 tests fail under their own revert, 4 are contract pins**
  (null compare passes through, default culture untouched, `Ordinal` stays case-sensitive, a successful filter update
  writes its row). The first M079 test passed either way and was replaced.
- 2026-10-19 — verdicts written to the findings file; M070/M082 → TASK-681, M081 → TASK-679, M074 → TASK-680,
  M083 spawned as TASK-683; spec `entity-localization` regenerated; STORY-053 at 385/428.
- 2026-10-19 — close gate. **Conventions:** one 🛑 fixed — the refusal message printed the expression (rule 42: it
  carries captured values into logs); it now names the node type and the field. ⚠ rule 16 fixed: `EntityType` has
  one producer, `LocalizedTranslationRows.EntityTypeOf<T>()`, used by the writer and all four filter readers.
  ⚠ rule 22 kept, with the reason: `SaveTranslations` returns on a null or empty GUID, which only a store that
  breaks its own create contract produces (every loaded or created entity has a key); throwing there would turn
  that store's bug into a failed write of an entity that did persist.
  **Correctness:** one should-fix fixed — a value computed through a nested lambda
  (`names.First(n => n.StartsWith("S"))`) was refused, because the nested lambda's parameter counted as the entity;
  `ParameterFinder` now ignores parameters bound inside the value. New test, red with the old finder. Kept as nits:
  a null-compared value that is a call is evaluated twice by the analyzer (harmless unless it has side effects);
  the sync/async difference when an action throws mid-batch → recorded in TASK-683; rows written before this change
  under a subclass name match filters only after their next save (no consumer stored any: none calls the wrappers).
  153/153 green, no warnings; Sandbox builds.
- 2026-10-19 — second correctness pass. Fixed: single-argument `StartsWith` / `EndsWith` had become ordinal; they
  keep the .NET culture-sensitive default again (`Contains` stays ordinal) — new test, red when ordinal. Fixed: the
  refusal missed a field read through a cast (`((INamed)x).Name`) — new test, red without the unwrap. Rule 27 on the
  sync filter update's restore-in-place: kept, the rule names this path as its one exception. Recorded in TASK-683:
  concurrently deleted rows (sync writes orphan rows, async stops part-way). Kept as known limits: `Create(IEnumerable)`
  still keys translations by `entity.Guid`, since a bulk create returns no GUIDs (a store that does not write them back
  loses them, as before); a whole-entity `Update(T)` still writes every localizable field, so an untranslated field
  read back with its base text becomes a translation row — the explicit `SetAsync` of TASK-682 is the answer there;
  more translation reads per write (one per field, one per filter-matched row) — TASK-679 batches them.
  155/155 green, no warnings.
