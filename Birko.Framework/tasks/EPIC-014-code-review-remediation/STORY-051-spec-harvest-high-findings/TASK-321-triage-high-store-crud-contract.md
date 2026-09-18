---
id: TASK-321
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
findings: [SH-H046]
pr: "Stores 8058810 · Repositories 005f140 · tests 94f677a + 6b518be"
github-issue: null
jira-key: null
---

# Triage the 1 remaining high spec-harvest finding in `store-crud-contract`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **1** open finding in the `store-crud-contract` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H046` | IStore.Destroy() is documented as "releases all resources" but implementations hard-delete all stored data | `Birko.Data.Stores/IStore.cs:32` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: store-crud-contract`, lines 344-350.

**The contract under review** is specced in [`docs/specs/store-crud-contract.md`](../../../docs/specs/store-crud-contract.md),
harvested from 12 source files — `../Birko.Data.Core/Exceptions/StoreException.cs`, `../Birko.Data.InMemory/Stores/AbstractAsyncInMemoryStore.cs`, `../Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Contract-wide.** `IStore` is the framework's central store interface, so the *documentation* defect is visible to every consumer; whether any of them calls `Destroy()` is the thing to measure when triaging.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** a documentation/behaviour mismatch on a **destructive** method. Not P0 because nothing mistranslates or leaks; the risk is a caller believing the doc comment.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/store-crud-contract.md` lying until `/specs regen store-crud-contract` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [x] The finding is marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
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
- [x] Any behavioural fix is followed by `/specs regen store-crud-contract`, with the spec diff reviewed as the
      change's evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [x] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 38 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-163]] owns those.
- Low-severity findings in this area — [[TASK-182]] owns them. Where a fix closes findings across
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

_Populated by `/tasks plan TASK-321` — leave empty until then._

## Progress log

- step 2 - picked; ranked above TASK-313 (entity-localization), TASK-316 (repository-contract) and
  TASK-319 (schema-index-and-ddl) on **key 2**, at comparable key-1 severity. All three of those, and
  the four other remaining high tasks, measure **0 consumer .cs references**; this one is the only
  **contract-wide** item in the tier - `IStore` is the framework's central store interface, so the
  documentation every consumer reads is the defect surface. Key 3 also favours it: the harvest records
  that **no store implements IDisposable**, so a developer looking for cleanup finds only
  `Destroy()`, reads "releases all resources", and on RavenDB drops the entire database
  (`hardDelete: true`). § Conventions already carries the applicable rule family - *a short name is a
  footgun in proportion to how destructive the operation is*.
- step 3 - **CONFIRMED-NARROWER**, and the narrowing sharpens the fix rather than shrinking it.
  Confirmed verbatim: `IStore.cs:29-32` reads *"Destroys the store and releases all resources"* and
  `IAsyncStore.cs:21-25` is identical, while the implementations permanently delete data -
  `RavenDBStore.Destroy:142` sends `DeleteDatabasesOperation(dbName, hardDelete: true)` (**the whole
  database**, not this entity's documents), `CosmosDBStore:143` deletes the container,
  `MongoDBStore:92` drops the collection, `JsonStore:111` `File.Delete`s the file, InMemory clears.
  **17 files** carry a `Destroy`/`DestroyAsync` override.
- step 3b - ⚠ **The harvest's supporting claim is FALSE, and it was the load-bearing one.** It states
  *"No store implements IDisposable, so a consumer looking for cleanup finds only Destroy()"*.
  Measured: `RavenDBStore` declares `IDisposable` (`:27`) with a `Dispose()` that disposes the
  document store when it owns it, and `InfluxDBStore` declares `System.IDisposable` in its class
  header; `AsyncRavenDBStore`, `AsyncInfluxDBStore`, `DataBaseStore`, `AsyncDataBaseStore` and
  `CachedAsyncDataBaseBulkStore` all reference it too. So disposal **exists** on the backends that
  hold resources.
  That makes the defect worse-framed-better: `Destroy()`'s doc does not merely mislead, it **describes
  what an existing, correct member already does**, so a consumer reading it has no reason to look for
  `Dispose()`. The *contract* is still the problem - `IStore`, `IAsyncStore` and all four abstract
  bases declare no disposal member - so the fix belongs on the contract, not on the implementations.
- step 3c - **the callers were measured, per the task's own triage note, and they are all correct.**
  Every framework call site is a deliberate destructive operation: the seven
  `Birko.BackgroundJobs.*JobQueueSchema.DropAsync` methods are each documented
  *"WARNING: This deletes all job data"*, the repository overrides forward to their store, and the
  EventSourcing / Localization wrappers delegate. **0** consumer `.cs` files call it. So nothing
  relies on the doc's false reading today - this is a trap laid for the next reader, not an active
  incident.
- step 4 - layer: **local**, and specifically on `Birko.Data.Stores` (the contract), not on the 17
  implementations - they behave correctly, they are simply described wrongly.
- step 5 - fix in `Birko.Data.Stores/IStore.cs` + `IAsyncStore.cs`. The summary now leads with
  **"PERMANENTLY DELETES every row this store can see. This is not disposal."**, the remarks give the
  **per-backend blast radius** (RavenDB drops the whole database; Cosmos the container; Mongo the
  collection; SQL the table; JSON/XML the file; InMemory the dictionary) and name the three doors a
  caller actually has - `IDisposable` for resources, `IBulkDeleteStore<T>.Delete(filter)` for
  selective deletion, `DeleteAll()` to empty a store that stays usable. **No implementation changed**:
  all 17 behave correctly, they were described wrongly. Tests in
  `Birko.Data.InMemory.Tests/DestroyIsNotDisposalTests.cs` (5), following
  `PortableBulkFilterGuardTests`' precedent - there is no `Birko.Data.Stores.Tests` and InMemory is
  the canonical double for the shared bases. Suite **74/74**, 0 warnings under `-warnaserror`.
- step 5b - ⚠ **the doors were verified to exist before being named**, per § TASK-263's *escape hatch
  that did not open*: `Delete(Expression<Func<T,bool>>)` is on `IBulkDeleteStore<T>`
  (`IBulkStore.cs:124`) and `DeleteAsync(filter)` on `IAsyncBulkStore` (`:134`), but **`DeleteAll()` /
  `DeleteAllAsync()` are base-class members on `AbstractBulkStore` / `AbstractAsyncBulkStore` and are
  NOT on any interface**. The doc says so explicitly rather than implying the interface carries them,
  and a test asserts both facts against the types.
- step 6 - three mutations, all reverted:
  **(A) the original misleading summary restored on both interfaces** -> **2 of 76** red, the doc
  theory.
  **(B) the "doors" paragraph deleted** -> **1 of 76**, `The_interface_doc_names_the_doors...` -
  § SH-H037's *a guard that only says no gets reached around*, as a test.
  **(C) InMemory's `Destroy` made a no-op** -> **2 of 76**, and *neither is in this file*: it reds the
  pre-existing `InMemoryStoreTests.Destroy_ShouldClearAllData` and its async twin. That is the honest
  attribution, and it is why my two behavioural tests were **deleted as duplicates** - the behaviour
  was already pinned, and what this file adds is the contract and documentation halves that no
  behaviour test can express. Final count 74.
- step 6b - ⚠ **Third instance this session of a source scan matching its own explanation.** A flat
  `NotContain("releases all resources")` failed, because the new remarks *quote* the old wording to
  record what the member used to claim. Narrowed to catch an **unquoted** occurrence only (a line
  carrying the phrase outside an `<i>...</i>` quotation). TASK-449 hit this with `System.Random`,
  TASK-450 with the bracket-quoted path, this with the doc phrase. § TASK-276 records the trap from
  the other side; the general rule is that a scan over files which *document* the defect must read
  code or structure, never prose.
- step 6c - ⚠ **WIDENED at step 7, by the spec regen rather than by reading the code.** Grepping the
  spec tree for the old phrase found a **second area documenting the same defect**:
  `docs/specs/repository-contract.md:251`. Traced to source - `IBaseRepository.Destroy()` and
  `IAsyncBaseRepository.DestroyAsync(ct)` carry the **identical sentence**
  (*"destroys the repository and releases all resources"*), and all four repository families forward
  straight to the store's `Destroy`. So a SQL-backed repository's `Destroy()` reaches
  `DataBaseStore.Destroy()` -> `Connector.DropTable(...)` and drops the entity's table. Not a milder
  operation one layer up: the **same destruction, one call away**, on the more consumer-facing surface.
  Fixed here rather than spawned - § TASK-215's *guard the whole verb family or none of it*: shipping a
  warning on the store contract beside a reassurance on the repository contract, for one operation, is
  precisely the half-fix that rule names. Doc pin added in
  `Birko.Data.Repositories.Tests/DestroyIsNotDisposalDocTests.cs` (2); behaviour already covered by
  `AsyncBulkRepositoryDestroyTests`, so not duplicated.
  **Mutation (D):** reverting **only the async** repository summary reds 1 of 18 - the asymmetry the
  widening exists to stop.
- step 6d - ⚠ **And the two scans disagreed with each other until they were unified.** The store-side
  scan was line-based and passed by luck (the store interfaces keep each quotation on one line); the
  same rule failed on the repository contract, whose async remark **wraps its quotation across two
  lines**, so the continuation carried the phrase with no opening tag. Both now use one
  `WithoutQuotations` helper that strips `<i>...</i>` spans across newlines, and each file says so.
  Two files checking one thing two different ways is the shape this codebase keeps unpicking - it does
  not stop being that because they are tests.
- step 7 - respecced **two** areas. `store-crud-contract.md`: the requirement was retitled
  *Destroy tears down stored data without resetting the initialization latch* ->
  *...permanently deletes stored data and is documented as such*, the "declared and implemented meaning
  disagree" paragraph replaced by the new contract, the *Caller reads Destroy as resource cleanup*
  scenario (which described the defect as behaviour) replaced by three - the summary, the named
  alternatives, and *No implementation changed*. It also now records the corrected `IDisposable`
  fact: absent from the contract, present on `RavenDBStore`/`InfluxDBStore`/the SQL stores.
  `repository-contract.md`: the same treatment for the repository half.
- step 7b - root `CLAUDE.md` gains its `### Recent Updates` entry (convention gate check 9).
- step 8 - closed done; out-of-scope sweep: 5 boundary, 0 spawned, 0 declined.

## Human test plan

**N/A - fully covered by automated tests.** The deliverable is interface documentation plus the tests
that stop it drifting back. There is nothing to run by hand: the behaviour was already pinned
(`InMemoryStoreTests.Destroy_ShouldClearAllData`, `AsyncBulkRepositoryDestroyTests`) and the new tests
assert the contract shape and the doc text.

⚠ What a human *should* do, and it is not a test step: the four backends whose blast radius is widest
(RavenDB drops the **entire database**, Cosmos the container, Mongo the collection, SQL the table) are
only described here, never exercised - no consumer calls `Destroy`, and doing so against a live
account would destroy the account's data, which is the whole point. The per-backend radius is read
from source and stated as such.

## Outcome

**What was wrong.** `IStore.Destroy()` and `IAsyncStore.DestroyAsync()` were documented as *"destroys
the store and releases all resources"* - disposal wording - while all 17 implementations permanently
delete data, and `RavenDBStore` drops the **entire database** (`hardDelete: true`). The same sentence
appeared on `IBaseRepository.Destroy()` / `DestroyAsync()`, which forward to the store.

**The harvest's supporting claim was false, and correcting it sharpened the fix.** It said *"no store
implements IDisposable, so a consumer looking for cleanup finds only Destroy()"*. Measured:
`RavenDBStore` and `InfluxDBStore` declare `IDisposable`, and the SQL stores reference it - releasing
exactly the resources the old doc claimed. So the doc did not merely mislead; it **described what an
existing, correct member already does**, giving a consumer no reason to look past it. The *contract*
is where disposal is genuinely absent, which is why the fix belongs there and not on any
implementation.

**What was done.** Documentation only, on two contracts: `Birko.Data.Stores/IStore.cs`,
`IAsyncStore.cs` and `Birko.Data.Repositories/IBaseRepository.cs`. Each summary now leads with
*"PERMANENTLY DELETES every row ... This is not disposal"*, the remarks give the per-backend blast
radius, and all three name the doors a caller has. **No implementation changed** - all 17 behave
correctly and were described wrongly.

**Step-6 split.** (A) misleading summary restored on both store interfaces -> 2 of 76. (B) the
"doors" paragraph deleted -> 1 of 76. (C) InMemory's `Destroy` made a no-op -> 2 of 76, **neither in
my file** - it reds the pre-existing behaviour tests, which is the honest attribution and why my two
behavioural tests were deleted as duplicates. (D) only the async repository summary reverted -> 1 of
18.

**Judgement calls.**

- **Documentation, not a rename.** § Conventions' rule that a destructive all-rows operation is named
  `*All` is about a short name sitting one keystroke from a safe one (`Delete()` vs `Delete(items)`).
  `Destroy` is already an alarming word; what disarmed it was the sentence underneath. Renaming would
  break 17 implementations, 7 `JobQueueSchema.DropAsync` helpers and every wrapper, to fix a problem
  the wording caused. Recorded rather than done.
- **No `IDisposable` added to the contract.** That is the feature the harvest's framing implies, and it
  is a design decision about a central interface - not something to slip into a documentation fix. The
  doc instead redirects to where disposal actually lives, and a test asserts the contract still has
  none so the redirect cannot go stale silently.
- **The doors were verified before being named** (§ TASK-263). `Delete(filter)` is on
  `IBulkDeleteStore<T>`; `DeleteAll()` is a **base-class** member, not an interface one, and the doc
  says so rather than implying otherwise.
- **Behaviour tests deleted as duplicates.** `Destroy` already had behavioural coverage on both the
  store and repository sides. Keeping my own versions would have been noise, and mutation C shows the
  existing ones carry that weight.
