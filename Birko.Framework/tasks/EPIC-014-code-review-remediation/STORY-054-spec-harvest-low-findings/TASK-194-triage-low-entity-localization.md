---
id: TASK-194
parent: STORY-054
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-09
depends-on: []
blocks: [TASK-679, TASK-680]  # EPIC-021 (Symbio STORY-081): triage before building on Birko.Data.Localization
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-L078, SH-L079, SH-L080, SH-L081, SH-L082, SH-L083, SH-L084, SH-L085, SH-L086, SH-L087]
pr: null
github-issue: null
jira-key: null
---

# Triage the 10 low spec-harvest findings in `entity-localization`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-054]] — which held all
387 low findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **10** findings
that fall in the `entity-localization` area.

**Findings:** `SH-L078`–`SH-L087` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Low severity →
`### area: entity-localization`, lines 3494–3555.

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

- [x] All 10 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
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
- [x] [[STORY-054]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 377 low findings — they belong to the other 21 per-area tasks under [[STORY-054]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Medium-severity findings in this area — [[TASK-168]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A — fully covered by automated tests. `Birko.Data.Localization` is a library contract with no UI surface, and no
consumer calls the wrappers yet; every behaviour changed here is asserted by `LocalizationLowFindingsTests` and the
inverted tests.

## Implementation plan

Drafted 2026-10-19 after reading all 10 findings against the code as TASK-168 left it (`1d2dfb16`). Three
decisions taken by the user the same day: L079 refuses every bad name, L081 throws like `OrderByHelper`, L080 uses
the four-part key.

1. **L079 + L082** (one fix): each wrapper reads `_innerStore.CreateInstance().GetLocalizableFields()` **once, in
   its constructor**, and validates it: every name must be a public instance `string` property with a getter and a
   setter, otherwise `InvalidOperationException` naming the type and field — at wiring, on every culture. The cached
   list replaces the per-call `GetLocalizableFieldsFromInstance()` allocations (L082). An entity's own
   `GetLocalizableFields()` (a subclass may add fields) is validated the same way where it is used to write or apply.
   Consequence: TASK-498's `RefuseIncrementOnLocalizableField` can no longer fire (a localizable field is a string,
   `Increment` takes numbers) → removed per rule 53, and its two tests are inverted into "a model declaring
   `decimal Price` localizable is refused at construction" (rule 56); `PricedModel` keeps `Label` only.
2. **L080**: reading, upserting and deleting translation rows filter on `EntityType = EntityTypeOf<T>()` too, through
   `LocalizedTranslationRows` (new `Delete`, and `Read` / `Save` gain the type). TASK-168's "a row found under
   another type name is rewritten" goes; update SH-M072's verdict and the spec.
3. **L081**: `ApplyInMemoryOrderBy` throws `ArgumentException` for an unknown property, before its trivial-input
   early return, as `OrderByHelper` does; the M071 test is inverted to expect the refusal. Null placement and the
   culture collation stay deliberate differences (backends disagree on NULLs among themselves) — recorded, not fixed.
4. **L078**: `Destroy` / `DestroyAsync` destroy the inner store, then delete this entity type's translation rows
   (`Delete(x => x.EntityType == …)` on the translation store — bounded, never the whole shared table). `Init` also
   initialises the translation store.
5. **L083 / L084**: the binary switch becomes `Func<string?, bool>?` with `_ => null`; `ValuePredicate` becomes
   `Func<string?, bool>`.
6. **L085**: the README section was rewritten by TASK-168; add that `Delete(filter)` reads then deletes so
   translations cascade.
7. **L086**: `ApplyInMemoryPaging` throws `ArgumentOutOfRangeException` for a negative offset or limit.
8. **L087**: handed to TASK-681 — its filter rework is where the GUID list can become a query instead of an `IN`
   list; nothing in the framework chunks `IN` lists today.

Tests in `LocalizationLowFindingsTests`, each red-verified by reverting its fix; then `/specs regen
entity-localization`, verdicts, STORY-054 progress.

## Progress log

- 2026-10-19 — picked; all 10 findings read against `1d2dfb16` (TASK-168's code). Three decisions put to the user and
  taken as recommended: refuse every bad field name (L079), throw for an unknown order-by property (L081), match rows on
  the four-part key (L080).
- 2026-10-19 — fixed L078, L079+L082, L080, L081, L083, L084, L085 (docs), L086; L087 handed to TASK-681. TASK-498's
  increment-on-localizable refusal removed as unreachable (rule 53), its 2 tests and SH-M071's test inverted (rule 56).
- 2026-10-19 — `LocalizationLowFindingsTests` 18 cases: **17 red under their own revert, 1 contract pin** (L084, a
  type-level change the compiler checks). The inverted tests (2 theories = 4 cases, and M071) are red under the L079 and
  L081 reverts. 173/173 green, no warnings. Spec regenerated; verdicts, STORY-054 at 111/391.
- 2026-10-19 — close-gate reviews. **Conventions** (no blockers): ⚠ rule 53 fixed — `Capture` / `Restore` kept their own
  skip-a-bad-property branches, now unreachable; one resolver (`LocalizableProperty`) serves the check and both, and it
  resolves a property hidden with `new` to the hiding one instead of throwing `AmbiguousMatchException` (new test, red
  with plain `GetProperty`). ⚠ rules 2/5 fixed — `DeleteType` refuses a null or empty type, which the filter would read
  as "every type" (new test, red without the guard). **Correctness** (no blockers): fixed — an entity's own bad
  declaration was refused only after the inner store had written it; every create/update now validates first (2 tests,
  3 cases, red when validation is a no-op). Fixed — a wrongly-cased order-by name passed on English (pushed down,
  `Expression.Property` ignores case) and threw on Slovak; resolution is case-insensitive like `OrderByHelper` (new test,
  red without `IgnoreCase`). Kept, following the user's decisions: an `internal set` property is refused; construction
  calls `CreateInstance()` (every in-tree store's is `new()`); `Update(IEnumerable)` / `Update(filter, Action)` use the
  store-level declaration while `Update(T)` uses the entity's (pre-existing; both validated). Rows written under a
  subclass name before TASK-168 are not migrated (no consumer has any). 178/178 green, no warnings; Sandbox builds.
