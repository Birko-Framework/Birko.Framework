---
id: TASK-313
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
picked-by: fix-next
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H015, SH-H016, SH-H017, SH-H018]
pr: e4e208b3becaa64636ab7811b8a3886837d14122
github-issue: null
jira-key: null
---

# Triage the 4 remaining high spec-harvest findings in `entity-localization`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **4** open findings in the `entity-localization` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H015` | Create/Update on a non-default culture writes the localized text into the default-culture column | `Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:83` |
| `SH-H016` | ApplyTranslations mutates the entity in place, corrupting stores that return live instances | `Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:218` |
| `SH-H017` | Filter-based Update on a non-default culture overwrites the default-culture base column | `Birko.Data.Localization/Decorators/LocalizedBulkStoreWrapper.cs:174` |
| `SH-H018` | Filter-based Update/Delete/PropertyUpdate never rewrite the filter, so a localized predicate hits the base column | `Birko.Data.Localization/Decorators/AsyncLocalizedBulkStoreWrapper.cs:181` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: entity-localization`, lines 130-154.

**The contract under review** is specced in [`docs/specs/entity-localization.md`](../../../docs/specs/entity-localization.md),
harvested from 12 source files — `../Birko.Data.Localization/Decorators/AsyncLocalizedBulkStoreWrapper.cs`, `../Birko.Data.Localization/Decorators/AsyncLocalizedStoreWrapper.cs`, `../Birko.Data.Localization/Decorators/LocalizedBulkStoreWrapper.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Latent.** `LocalizedStoreWrapper`: **0** consumer `.cs` files. `Birko.Data.Localization` is imported by Symbio's aggregator, so it compiles there but nothing constructs the decorator.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** data corruption is real but confined to consumers who wrap a store in the localization decorator; no cross-tenant or auth dimension.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/entity-localization.md` lying until `/specs regen entity-localization` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [x] All 4 findings are marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
      the code, with the verdict and its evidence (`file:line` + the mechanism, not just the rule)
      written back into `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific
      code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers,
      and name any test that passes either way as a contract pin rather than as evidence
- [x] ⚠ For a claim of *silent* loss, corruption or leakage, the assertion is the **observed
      state** — rows counted, the value read back, the tenant that could see it — never that no
      exception was thrown. § Conventions records several defects that a "did not throw" assertion
      hid, including one in this epic that hid a live failure for weeks
- [x] Any behavioural fix is followed by `/specs regen entity-localization`, with the spec diff reviewed as the
      change's evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [x] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 35 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-168]] owns those.
- Low-severity findings in this area — [[TASK-194]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

**N/A — fully covered by automated tests.** `Birko.Data.Localization` is a library contract with no UI
and no service boundary; every claim here is about the state of a store after an operation, which the 33
new tests assert directly by reading the inner store back. There is nothing a human could observe that the
suite does not.

## Implementation plan

_Populated by `/tasks plan TASK-313` — leave empty until then._

## Progress log

- step 2 — picked; ranked above TASK-314 (`migrations`) because its worst member (`SH-H032`, an empty
  operator object degrading to match-all on delete/update) is an *unbounded destructive write*, one rung
  below the *silent corruption* claimed here (`SH-H015`/`SH-H017` overwrite the default-culture source
  text; `SH-H018` sends a destructive filter at the wrong column). Self-containment confirmed it:
  one project, four findings in one root-cause family, testable offline over InMemory — where TASK-314
  spans four backend projects and three services needing live servers. Ranking keys 6-8 never reached.

## Outcome

**What was wrong.** The localization decorators treat an entity's own column and its translation as the
same slot, and they resolve a filter for reads but not for writes. Under a non-default culture that meant
four things, all silent: an `Update` overwrote the stored **default-culture** text with the translated text
(`SH-H015`); a *read* wrote the translation into the instance the inner store had handed back, so on a
store that returns live instances one Slovak page view destroyed the English original permanently
(`SH-H016`); a filter-based `Update` did the same through the caller's action (`SH-H017`); and
`Update(filter, ...)` / `Delete(filter)` never resolved their predicate, so `Delete(x => x.Name ==
"Stolicka")` matched the untranslated column and removed **a different set of rows than the identical
`Read(filter)` returned** (`SH-H018`).

**Verdicts.** 3 CONFIRMED, 1 CONFIRMED-NARROWER (`SH-H015`: the corruption holds for `Update`; `Create` has
no stored value to destroy, so it is a fallback rather than a defect and is deliberately unchanged). 0
refuted. All four verdicts, with their evidence and their fixes, are written into
`SPEC-HARVEST-FINDINGS-2026-07-30.md`.

**The fix.** The rule is now stated once — *a localizable field's base column holds the default-culture
value, every other culture lives in a translation row* — in `Decorators/LocalizedEntityFields.cs`, and
all four wrappers call it. Reads return a detached copy (`Localize`, replacing `ApplyTranslations`); entity
updates hand the inner store a detached copy carrying the base values read back from storage; filter-based
updates capture and restore around the caller's action; and every filter-based write resolves its filter
exactly as a read does. Default-culture behaviour is byte-identical throughout.

**Step 6 — four disjoint mutations, 112 tests (79 pre-existing + 33 new).**

| Mutation | Reverts | Red | Fix-dependent tests |
|---|---|---|---|
| A | entity `Update` hands over the caller's entity again | **7 of 112** | the 7 `SH_H015_*` base-column tests — sync + async, single + bulk + collection |
| B | `Localize` applies to the store's own instance again | **9 of 112** | the 8 `SH_H016_*` tests **plus** `SH_H015_A_read_modify_write_cycle_...` |
| C | filter-update drops the capture/restore | **5 of 112** | the 5 `SH_H017_*` tests, sync + async |
| D | filter-based writes stop resolving the filter | **6 of 112** | the 6 `SH_H018_*` crossed-row tests, sync + async |

Mutation B redding an `SH_H015` test is not leakage: `SH-H016` genuinely **defeats** `SH-H015`'s fix on a
live-instance store, because preserving the base column works by reading the stored value back and a
corrupting read leaves nothing correct to read. That coupling is recorded on the finding.

⚠ **All 79 pre-existing tests passed against the unfixed code and still pass now** — nothing in this
area could see any of the four defects. That absence is the reason they survived a harvest-grade suite.

**Contract pins — named as pins, not as evidence** (they pass either way):
`Pin_default_culture_writes_are_untouched`, `Pin_default_culture_reads_return_the_inner_store_instance`
(x2, sync + async), `Pin_a_filter_naming_no_localizable_field_is_unaffected`, and
`SH_H015_The_caller_entity_is_returned_unchanged` (x2). The last pins a *design* choice rather than the
defect — it passes against the original code too, and would only red against a fix that preserved the
base column by mutating the caller's object.

**Judgement calls.**

- **The copy is `Object.MemberwiseClone` via reflection, not a property-by-property reflection copy.** The
  latter silently drops any property with no public setter, handing a caller a partially-populated entity
  — a quieter defect than the one being removed (§ SH-H037). The framework offers no alternative:
  `AbstractModel.CopyTo(null)` returns `this`, and an un-overridden `CopyTo(new T())` copies only `Guid`.
- **Fixed at the decorator, not at `Birko.Data.InMemory`.** The stricter-looking option — declare that
  `IStore<T>.Read` returns a detached instance and change every backend — is a framework-wide contract
  change, and it would not help a caching decorator, which the finding names as the same shape. The
  decorator is the layer writing to an object it did not create.
- **⚠ The first version of the fix was wrong, and only a test caught it.** It preserved the base column
  by swapping the values on the caller's entity and restoring them in a `finally`. A store may keep the
  reference it is given — the test double and `Birko.Data.InMemory` both do — so the restore wrote
  the translated text straight back into the store. It failed 4 tests. Handing over a copy is what actually
  works, and the reason is recorded in the code so nobody simplifies it back.
- **`Create` deliberately unchanged**, per the narrowed verdict above.
- **A detached read is unconditional on a non-default culture**, not conditional on a translation row
  existing. Copying only on a hit would vary per entity inside one result set, which is exactly the kind of
  difference a test passes by luck.
- **Consumer reach re-measured 2026-09-17: 0 `.cs` files across all 16 consumer repos**, so no behaviour
  change reaches a consumer today. Per this task's own warning that is a reason not to overstate urgency,
  not a reason to discount the fix.

**Flagged, not fixed.** Nothing was deferred from these four findings. Two pre-existing defects were met in
passing and left alone: the zero-argument `ReadAsync()` does not compile (CS0121 — already tracked as
[[TASK-138]]), worked around by spelling the overload out at each call site; and a `null` localizable value
is skipped by `SaveTranslations`, so it neither writes nor clears a translation row — pre-existing,
already specced as-is, and outside this task's findings.
- step 3 — verified: SH-H016, SH-H017, SH-H018 held as written; SH-H015 rescoped to CONFIRMED-NARROWER
  (Update corrupts, Create has no stored value to destroy). Acceptance unchanged; verdicts written to
  SPEC-HARVEST-FINDINGS-2026-07-30.md.
- step 4 — layer: local. The decorator writes to an object it did not create and resolves a filter on
  reads only; both are in Birko.Data.Localization. Fixing InMemory instead would be a framework-wide
  store-contract change and would still miss a caching decorator.
- step 5 — fix in Birko.Data.Localization/Decorators/{LocalizedEntityFields (new), LocalizedStoreWrapper,
  LocalizedBulkStoreWrapper, AsyncLocalizedStoreWrapper, AsyncLocalizedBulkStoreWrapper}.cs + projitems;
  tests in Birko.Data.Localization.Tests/{LocalizedBaseColumnIntegrityTests,
  AsyncLocalizedBaseColumnIntegrityTests}.cs; suite 112/112 green (79 baseline + 33 new).
- step 6 — four disjoint mutations: A 7/112, B 9/112, C 5/112, D 6/112. Fix-dependent and contract-pin
  test names are in the Outcome table. First version of the fix (capture/restore on the caller entity)
  failed 4 tests and was replaced with a detached copy.
- step 7 — respecced entity-localization; requirements changed: Purpose, Applying translations on read,
  Bulk reads translate every returned entity, Translation persistence on create and update, Filter-based
  bulk delete (title + body), Filter-based bulk update with an action, Native PropertyUpdate fallback.
  Diff +94/-39; no unintended change in it. source-commits stamped PENDING-TASK-313 until the production
  commit lands.
- step 8 — closed done; production e4e208b, tests f382893, aggregator 025c8d1. Gate: verify-conventions
  (generic + verify-birko-conventions, ran — 0 nullable warnings, checks 2/3/4/10 clean; checks 6/7/7b/8
  N/A, no new project) · verify-intent (all 7 criteria met; criterion 7 was genuinely unmet until the
  STORY-051 rollup was written) · code-review inline · security-review inline (the diff touches data
  access — filter resolution on destructive paths; it narrows rather than widens, and the empty-match-set
  case is pinned). Register-on-introduce and check #9 both fired and were satisfied in the same change.
  Out-of-scope sweep: 5 bullets, all boundaries naming an owner; 0 spawned.
