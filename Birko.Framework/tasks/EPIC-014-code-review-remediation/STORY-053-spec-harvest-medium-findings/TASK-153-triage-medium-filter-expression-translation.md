---
id: TASK-153
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M123, SH-M124, SH-M125, SH-M126, SH-M127, SH-M128, SH-M129, SH-M130, SH-M131, SH-M132, SH-M133, SH-M134, SH-M135, SH-M136, SH-M137, SH-M138, SH-M139, SH-M140, SH-M141, SH-M142, SH-M143, SH-M144, SH-M145, SH-M146, SH-M147, SH-M148, SH-M149, SH-M150, SH-M151]
pr: null
github-issue: null
jira-key: null
---

# Triage the 29 medium spec-harvest findings in `filter-expression-translation`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **29** findings
that fall in the `filter-expression-translation` area.

**Findings:** `SH-M123`–`SH-M151` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: filter-expression-translation`, lines 1198–1373.

**Closed outside this task:** `SH-M128` (an OR rule group renders as AND) — fixed by [[TASK-559]] (2026-10-11),
which rewrote `RuleConditionConverter`'s group builder. Do not re-verify it here.

**The contract under review** is specced in [`docs/specs/filter-expression-translation.md`](../../../docs/specs/filter-expression-translation.md), harvested from
15 source globs — `../Birko.Data.Core/Expressions/*.cs`, `../Birko.Data.Core/Filters/*.cs`, `../Birko.Data.SQL/SQL/DataBase.cs`, and 12 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/filter-expression-translation.md` lying until `/specs regen filter-expression-translation` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 29 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen filter-expression-translation`, with the spec diff reviewed as the change's — done 2026-10-16 (batched regen of 17 areas, diffs reviewed and confirmed)
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Resolution (2026-10-15)

**28 findings triaged (SH-M128 was closed by TASK-559): 25 confirmed, 2 confirmed-narrower, 1 refuted.** Of the
confirmed: 18 fixed here, 6 already fixed by other tasks (verified, not assumed: TASK-574, TASK-559, TASK-278,
TASK-308, TASK-615, TASK-502), 2 spawned as [[TASK-626]] (SH-M147, SH-M148, the join renderer). Verdicts with evidence
are in `SPEC-HARVEST-FINDINGS-2026-07-30.md`.

- **Measured, not read**: each SQL finding was first run as a probe comparing the SQL answer with LINQ-to-objects
  over the same seeded rows. The probe's first run was wrong. It reused one database file and every call appended
  the seed again, so every count was inflated. That was caught and fixed before any verdict was written.
- **Two corrections found by the live run, not by review.** The value-position bool test was first resolved
  table-qualified, which PostgreSQL refuses in an UPDATE's SET. And NEST drops a term query on `""` as
  "conditionless", which left `IsNullOrEmpty` half-built. Both were fixed before the red check.
- **One wrong premise corrected in code**: `SqlLiteral` stated that quote-doubling is complete containment "on the
  other three providers". Measured on MySQL 8.4, the default `sql_mode` honours backslash escapes.

**Red-verified, as numbers.** SQLite `FilterTranslationTriageTests` 20 of 23 red (pins: an equality with the value on
the left, `Contains("0%")`, the escape character alone). `ValueExpressionTriageTests` 2 of 3 (pin: a closure chain).
Live `FilterTranslationTriageLiveTests`: PostgreSQL 3 of 5 (pins: the UPDATE CASE, which PostgreSQL accepted
before, and the mixed-case table read, which is SH-M137's refutation), SQL Server 3 of 4 (pin: bool in a WHERE CASE,
valid T-SQL before), MySQL 3 of 5. ElasticSearch `FilterTranslationTriageTests` 9 of 10 (pin: SH-M151's two
closures, which the normalizer folds before the cache). 7 SQL renderings that pinned the unescaped LIKE were updated.
Regression: 30 SQL-dependent suites, 2,587 tests, green live; ElasticSearch 255/255 live.

**Spec regen pending**, batched with the database fixes ([[project-spec-regen-batched]]): `filter-expression-translation`.

## Out of scope

- The other 392 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-189]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A: fully covered by automated tests. This is a library contract with no UI surface, and every fix is asserted
by the rows (or documents) a real database returns.

## Implementation plan

_Populated by `/tasks plan TASK-153` — leave empty until then._
