---
id: TASK-308
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P0
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H021, SH-H022, SH-H024, SH-H025, SH-H026, SH-H027, SH-H028]
pr: null
github-issue: null
jira-key: null
---

# Triage the 7 remaining high spec-harvest findings in `filter-expression-translation`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **7** open findings in the `filter-expression-translation` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H021` | An untranslatable AND/OR operand is read as constant TRUE, so an OR predicate loses its WHERE clause entirely | `Birko.Data.SQL/SQL/DataBase.cs:916` |
| `SH-H022` | ReturnSingleSubCondition overwrites the parent's IsNot, so `!(a && trueConst)` renders as `a` — the opposite rows | `Birko.Data.SQL/SQL/DataBase.cs:948` |
| `SH-H024` | An unrecognised parameter-bound method call in an UPDATE SET value is reflectively invoked with null args and its result bound as a constant | `Birko.Data.SQL/SQL/DataBase.cs:239` |
| `SH-H025` | A value that will not convert to double yields an unbounded range query (ElasticSearch) | `Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:246` |
| `SH-H026` | An unhandled expression node silently removes the whole WHERE clause (SQL) | `Birko.Data.SQL/SQL/DataBase.cs:818` |
| `SH-H027` | CombineBool silently drops an untranslatable AND/OR operand (ElasticSearch) | `Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:209` |
| `SH-H028` | ElasticSearch String.Contains passes the raw value into a QueryStringQuery — Lucene query-syntax injection | `Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:555` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: filter-expression-translation`, lines 172-222.

**The contract under review** is specced in [`docs/specs/filter-expression-translation.md`](../../../docs/specs/filter-expression-translation.md),
harvested from 28 source files — `../Birko.Data.Core/Expressions/ExpressionNormalizer.cs`, `../Birko.Data.Core/Expressions/ExpressionParameterReplacer.cs`, `../Birko.Data.Core/Expressions/PredicateScope.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **LIVE.** `Birko.Data.SQL` is imported by **6** consumer aggregators (excluding the Sandbox smoke harness, which imports everything), and `DataBase.cs` is on the path of every SQL read those consumers make.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P0:** every SQL read builds its WHERE through `DataBase.cs`, so a predicate that silently becomes match-all or loses its clause is the same class as [[TASK-116]] (degraded leaf → match-ALL) and [[TASK-110]] (identifier injection), both P0.

⚠ **Do not re-derive what [[TASK-109]], [[TASK-116]] and [[TASK-137]] already settled.** Those three
established the framework's answer to a degraded predicate: a leaf that cannot be translated is *tracked*
rather than silently replaced by a constant, and a filter that reduces to every row is **refused**
(`PredicateScope`, `WholeTableWriteException`). `SH-H021`/`SH-H026`/`SH-H027` are the **read**-path
siblings of that family, which those tasks explicitly left open — TASK-109 says so in as many words.
Check what those fixes already cover before assuming a finding still holds; a partial overlap is the
likeliest outcome, which makes **confirmed-narrower** the verdict to expect here.

`SH-H028` (Lucene query-syntax injection through `String.Contains`) is a different species from its six
neighbours — an injection sink, not a mistranslation. It is the one finding in this area whose fix
belongs to the identifier/containment family in § Conventions rather than to the predicate family.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/filter-expression-translation.md` lying until `/specs regen filter-expression-translation` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [ ] All 7 findings are marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
      the code, with the verdict and its evidence (`file:line` + the mechanism, not just the rule)
      written back into `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific
      code it traced
- [ ] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [ ] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers,
      and name any test that passes either way as a contract pin rather than as evidence
- [ ] ⚠ For a claim of *silent* loss, corruption or leakage, the assertion is the **observed
      state** — rows counted, the value read back, the tenant that could see it — never that no
      exception was thrown. § Conventions records several defects that a "did not throw" assertion
      hid, including one in this epic that hid a live failure for weeks
- [ ] Any behavioural fix is followed by `/specs regen filter-expression-translation`, with the spec diff reviewed as the
      change's evidence
- [ ] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [ ] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 32 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-153]] owns those.
- Low-severity findings in this area — [[TASK-189]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

Cannot be written yet: which steps a human adds depends on which findings survive triage. **Resolve this
section before `/tasks close`** — an absent plan is not an `N/A` one, and defaulting it parks the
task on a step that may not exist (SKILL.md § Lifecycle). Expected outcome for this area is
`N/A — fully covered by automated tests`, since it is a library contract with no UI surface; write
that explicitly with its reason rather than leaving the section as-is.

## Implementation plan

_Populated by `/tasks plan TASK-308` — leave empty until then._
