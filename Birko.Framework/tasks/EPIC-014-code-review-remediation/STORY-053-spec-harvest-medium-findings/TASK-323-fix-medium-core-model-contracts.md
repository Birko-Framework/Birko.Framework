---
id: TASK-323
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
picked-by: fix-next
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M422, SH-M423, SH-M424]
pr: null
github-issue: null
jira-key: null
---

# Fix the 3 verified medium findings in `core-model-contracts`

## Context

Filed by [[TASK-195]] on 2026-09-08, which rated, ID'd and folded in the 16 findings recovered from the
harvest's lost first-schema pass. `core-model-contracts` is one of the **three areas that had no per-area task at all**,
because the 44 tasks under [[STORY-053]]/[[STORY-054]] cover only the 22 areas that had rated findings —
and these three are precisely the ones that did not. Folding ids in without creating this task would have
left them exactly as they were: filed, and scheduled by nothing.

⚠ **These are not triage tasks, and that is the one way they differ from their 44 siblings.** Every
finding below was **hand-verified against the code on 2026-09-08** as part of the rating pass, so the
verdict is already in the findings doc. There is no confirm-or-refute step: the work is the fix.

| Finding | Verdict | Claim |
|---|---|---|
| `SH-M422` | CONFIRMED-NARROWER | `AbstractLogModel.CopyTo` is an overload, not an override, so timestamps are dropped via a base-typed reference |
| `SH-M423` | CONFIRMED-NARROWER | `AbstractLogModel.LoadFrom` is an overload, not an override, so `ILoadable<IGuidEntity>` dispatch discards the timestamps the argument carries |
| `SH-M424` | CONFIRMED | `CopyTo` with a null or omitted target returns `this`, so the "clone" aliases the source |

Detail — including each verdict and what was measured — in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: core-model-contracts`. The recovered originals are in
[`STORY-055/RECOVERED-FINDINGS.md`](../STORY-055-spec-harvest-unrated-areas/RECOVERED-FINDINGS.md).

**The contract under review** is specced in [`docs/specs/core-model-contracts.md`](../../../docs/specs/core-model-contracts.md). Its
sources point into **sibling repos**, so a fix here normally lands as three commits in three repos
(production, regression suite, this file) per CLAUDE.md § Integration model.

**Why P2:** all three are latent: measured 2026-09-08, **0** `.CopyTo(` call sites in the framework's non-test code and no `ICopyable<AbstractModel>`-typed variable anywhere, and every `LoadFrom` call site is on a concrete model type so the overload resolves statically to the most-derived one. Real API defects on a base every model inherits, with no shipped path taking the broken dispatch today.

⚠ **These three are one decision, not three edits.** `SH-M422` and `SH-M423` are the same mistake (a derived method that looks like an override and is not) and `SH-M424` is the reason the base cannot simply be made overridable — `AbstractModel` is abstract, so a `CopyTo` with no target has nothing to allocate. Fixing the two dispatch findings without settling the null-target contract leaves `ICopyable`'s XML doc still false. Decide the contract first: **throw** on a null target, or **drop** the optional parameter and the doc claim together.

⚠ **And a signature change here is not free.** `CopyTo`/`LoadFrom` are public surface on the base of every Birko model. Measure the consumer blast radius before changing an arity or a parameter type — § TASK-255 records that a same-typed inserted parameter let two call sites **silently rebind** while the rest failed loudly, and § TASK-260 records that changing the *type* is the change the compiler cannot miss.

**Ordering constraint — the spec documents these defects as shipped behaviour.** The harvest specced what
the code *does*, defects included. So a behavioural fix leaves `docs/specs/core-model-contracts.md` lying until
`/specs regen core-model-contracts` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 3 findings are fixed with a regression test, or explicitly waived with a recorded reason. Findings sharing a
      root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers,
      and name any test that passes either way as a contract pin rather than as evidence
- [x] ⚠ The assertion is the **observed state**, never that no exception was thrown. § Conventions records
      several defects hidden by exactly that assertion, including one in this epic that hid a live MSSql
      failure for weeks
- [x] Any public-surface change (a signature, a namespace, a nullable annotation) is preceded by a
      **measured** consumer blast radius across all 16 consumer repos, and the count is written into the
      task — not asserted
- [x] `/specs regen core-model-contracts` run after any behavioural fix, with the spec diff reviewed as the evidence
- [x] Anything too large for this task is spawned via `/tasks spawn` — never left as a ticked box with the
      work undone, and never as an `## Out of scope` sentence describing work
- [x] [[STORY-053]]'s task table and `finding-count` reflect this area's closed count

## Out of scope

- **Re-verifying the findings.** Done on 2026-09-08; the verdicts are in the findings doc. If the code has
  moved since and a verdict no longer holds, that is a regression — say so and re-measure rather than
  silently fixing something else.
- The other severity tier of this same area — its low finding is [[TASK-326]].
- **The five recovered findings that were duplicates.** `SLI-1`→`SH-M307`, `SLI-2`/`SLI-3`→`SH-M316`
  ([[TASK-163]]), `SLI-4`→`SH-L297`, `SLI-6`→`SH-L298` ([[TASK-182]]). They are cross-referenced in the
  findings doc, not re-filed here.
- **Test gaps.** Test coverage was out of scope for the harvest sweep, so a missing test is not a finding
  here — only a test a fix needs.

## Human test plan

N/A — library code with no UI surface; fully covered by automated tests (`LogModelDispatchTests`, `AbstractModelTests`).

## Implementation plan

_Populated by `/tasks plan TASK-323` — leave empty until then._

## Outcome

**All 3 fixed.** A log model (anything deriving `AbstractLogModel`) now copies and loads its audit timestamps
whether it is held as itself or as an `AbstractModel` / `ICopyable` / `ILoadable` reference — before, the
base-typed call silently dropped them. And asking an abstract base for a copy without giving it a target now throws,
instead of returning the original object as its own "copy".

- **Step-6 split (11 tests across `LogModelDispatchTests` + `AbstractModelTests`):** full revert → **6/11** red:
  `CopyTo_through_an_AbstractModel_reference_keeps_the_timestamps`,
  `CopyTo_through_ICopyable_of_AbstractModel_keeps_the_timestamps`,
  `LoadFrom_through_ILoadable_of_IGuidEntity_keeps_the_timestamps`, `A_null_target_is_refused_through_a_base_reference_too`,
  and the two inverted CR-M082 tests (`CopyTo_without_target_throws_instead_of_returning_self`,
  `LogModel_CopyTo_without_target_throws_instead_of_returning_self`). Null-throw reverted alone → 3/11; overrides
  reverted alone → 4/11. **Contract pins:** `An_overriding_model_that_calls_base_with_its_own_type_copies_and_does_not_recurse`,
  `LoadFrom_a_plain_guid_entity_leaves_the_timestamps_alone`, and the 3 pre-existing copy/load-with-target tests.
  Solution 0 errors; 22 suites green (Data.Core 108, every Birko.Models* suite, Localization ×3, MongoDB, InMemory, SQLite).
- **The contract decision the task asked for — throw, keep the default.** Measured blast radius 2026-10-11 over the
  16 consumer repos + framework: 10 no-arg `.CopyTo()` calls (all FisData.Stock.Core, binding to its own allocating
  `ViewModel` overloads), 10 DraCode entity overrides and 19 framework derived models that all allocate before the
  base call, 5 FisData.Stock.API calls passing a target — **0** reach the base with null. Rejected: dropping the
  `= null` default, a compile break across every no-arg caller bought for nothing. Rejected: allocating via
  `Activator.CreateInstance(GetType())` in the base. It would make the doc literally true, but the 17
  `Birko.Models*` types copy their own fields in **overloads** (`Address.CopyTo(Address)` etc.), so a base-allocated
  "copy" of one held as `AbstractModel` would silently carry only `Guid` + timestamps — a plausible wrong copy is worse
  than an exception.
- **Judgement call — recursion.** The typed overloads (`CopyTo(AbstractLogModel?)`, `LoadFrom(ILogEntity)`) do not
  call the new overrides: a derived override that calls `base.CopyTo(target)` with its own type binds to the typed
  overload, and a virtual call from there would recurse into the override (DraCode's 10 entities have exactly that
  shape). Pinned by `An_overriding_model_that_calls_base_with_its_own_type_copies_and_does_not_recurse`.
- **Wider, fixed in the same edit:** `MongoDBLogModel.CopyTo(AbstractLogModel clone = null!)` returned the null target
  as its "copy"; it now inherits the refusal (and lost its dead null guard).
- **Flagged, not fixed:** the same overload-not-override shape exists on the 17 `Birko.Models*` types
  (`Address.CopyTo(Address)`, `Customer.CopyTo(Customer)`, …): through an `AbstractModel` reference their own fields
  are not copied. Not in this task's findings, 0 measured base-typed call sites, and fixing it is a per-model edit
  across the catalogue — recorded here rather than spawned, as no defect has been observed on a shipped path.
- **Merge gate:** inline review. No signature change (verified: parameter types and defaults unchanged); new public
  members are `override`s of existing virtuals. No nullable warnings.

## Progress log

- step 2 — picked; the last verified, self-contained task left in the pool after TASK-324 — TASK-555/556/557 are excluded as cross-provider decision tasks, TASK-558..561 are unverified spawns; ranked above TASK-561 (Redis default-queue, P2, config-dependent) on key 5 (verified over unverified)
- step 3 — verified: all three hold as recorded (`Birko.Data.Core/Models/AbstractModel.cs:11-18`, `AbstractLogModel.cs:13-35`). **Wider by one site, same root:** `Birko.Data.MongoDB/Models/MongoDBLogModel.CopyTo(AbstractLogModel clone = null!)` calls the base with null and returns the null target — a "copy" of `null`. Contract decision, measured first: **throw `ArgumentNullException` on a null target in the abstract bases, keep the `= null` default.** Blast radius 2026-10-11 across `C:\Source\Birko\Consumers` (16 repos) + framework: every no-arg `.CopyTo()` (10, all FisData.Stock.Core) binds to the consumer's own `ViewModel` overloads, which allocate; every framework derived model (17 in Birko.Models*, EntityTranslationModel, TranslationModel) and every DraCode entity override (10) allocates its target before calling the base; FisData.Stock.API's 5 calls pass a target. So 0 call sites reach the base with null, and dropping the default would be a compile-breaking signature change bought for nothing — the throw makes `ICopyable`'s doc honest without it
- step 4 — layer: local (Birko.Data.Core, Birko.Data.MongoDB)
- step 5 — fix in `Birko.Data.Core/Models/AbstractModel.cs` (null target throws), `AbstractLogModel.cs` (`CopyTo`/`LoadFrom` overrides + non-virtual typed overloads), `ICopyable.cs` (doc), `Birko.Data.MongoDB/Models/MongoDBLogModel.cs` (null target no longer returned); tests in `tests/Birko.Data.Core.Tests/LogModelDispatchTests.cs` (6) and 2 inverted in `AbstractModelTests.cs`; Data.Core 108/108 + 21 neighbouring suites green
- step 6 — full revert 6/11 red; null-throw alone 3/11; overrides alone 4/11; pins named in Outcome
- step 7 — respecced core-model-contracts; requirements replaced: *An omitted copy target is refused by the abstract bases*, *Copy dispatch does not depend on the static reference type*, *Load dispatch does not depend on the static reference type*; intro and the no-argument scenario amended; recursion scenario added
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
