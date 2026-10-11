---
id: TASK-163
parent: STORY-053
feature: FEATURE-014
# status: in-progress | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M302, SH-M303, SH-M304, SH-M305, SH-M306, SH-M307, SH-M308, SH-M309, SH-M310, SH-M311, SH-M312, SH-M313, SH-M314, SH-M315, SH-M316]
pr: null
github-issue: null
jira-key: null
---

# Triage the 15 medium spec-harvest findings in `store-crud-contract`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **15** findings
that fall in the `store-crud-contract` area.

**Findings:** `SH-M302`–`SH-M316` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: store-crud-contract`, lines 2290–2381.

**The contract under review** is specced in [`docs/specs/store-crud-contract.md`](../../../docs/specs/store-crud-contract.md), harvested from
9 source globs — `../Birko.Data.Stores/IStore.cs`, `../Birko.Data.Stores/IAsyncStore.cs`, `../Birko.Data.Stores/AbstractStore.cs`, and 6 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/store-crud-contract.md` lying until `/specs regen store-crud-contract` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 15 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen store-crud-contract`, with the spec diff reviewed as the change's
      evidence — **done by hand, not by regen**, as for the first two: every requirement and scenario the fixes
      falsified was rewritten (see 2026-10-11 below)
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## ⚠ Two of these findings were independently re-reported (added 2026-09-08 by [[TASK-195]])

The recovered `store-lazy-initialization` sweep hit the same defects, because that area's source globs
include the same `AbstractStore.cs` / `AbstractAsyncStore.cs` files this area globs:

| This task already owns | Also recovered as | Note |
|---|---|---|
| `SH-M307` (Destroy does not reset the initialization latch) | `SLI-1` | Same file, same line 48, same mechanism |
| `SH-M316` (InitCore re-entrancy fails two different ways) | `SLI-2` **and** `SLI-3` | `SH-M316` deliberately spans both halves, so two recovered ids collapse into it — `SLI-2` is the sync StackOverflow, `SLI-3` the async deadlock |

**Nothing was added to this task's `findings:` list** — the ids were already there, and the recovered
duplicates are cross-referenced in the findings doc rather than re-filed. What is worth carrying into the
fix: the recovered text describes the **async** half independently and is more specific about it than
`SH-M316`'s summary, so read `SLI-2`/`SLI-3` in
[`STORY-055/RECOVERED-FINDINGS.md`](../STORY-055-spec-harvest-unrated-areas/RECOVERED-FINDINGS.md) before
fixing — in particular that `SemaphoreSlim` has **no owner affinity**, which is why the async base hangs
where the sync base (a re-entrant `Monitor`) recurses to a `StackOverflowException` instead. Both were
re-verified as still present on 2026-09-08.

⚠ And note the framework now contains the remedy both halves ask for, in a different class: [[TASK-270]]
built an `AsyncLocal<bool>` re-entrancy scope for `DataBase.IsInitializing`, per call flow and per
instance, restoring on exception. Reuse that shape rather than inventing a second one.

## Progress — 15 of 15 (2026-10-11)

### 2026-10-11 — the other 13: 7 fixed, 6 spawned

Verdicts with `file:line` and mechanism are in `SPEC-HARVEST-FINDINGS-2026-07-30.md`. 0 refuted; `SH-M305`
confirmed-narrower (its RavenDB example is harmless — `CreateDocumentStore` never uses the credentials `GetId()`
drops — but SQL/Mongo passwords and the InfluxDB token are real).

**Fixed** (one commit, `tests/Birko.Data.InMemory.Tests/StoreCrudContractTests.cs`, 17 tests):

| Finding | Fix | Red with the fix reverted |
|---|---|---|
| `SH-M302` wrapper chain | `IsStoreOfType` (4 overloads) → one `IsOrWraps`: the store or its innermost store | 1 (two wrappers); one-wrapper test is a pin |
| `SH-M303` unlocked read | read inside the lock | **none** — a torn read is not reproducible; fixed by inspection |
| `SH-M304` async excluded | constraint removed, run-time `IBaseStore`/`IAsyncBaseStore` check | old constraint: test project fails `CS0311`; check removed: 1 |
| `SH-M305` settings dropped on hit | entry remembers its settings; a hit with different value properties throws | 1 |
| `SH-M308` resurrecting update | `ReplaceExisting`: `TryGetValue` + `TryUpdate` loop | 4 (sync/async × single/bulk) |
| `SH-M310` create upserts | `RequireNewKeys` (whole batch, before any write) + `TryAdd` → `StoreException` | 4 |
| `SH-M312` Delete(filter) bypass | both public overrides deleted; the base runs both guards and reaches `DeleteCore` | 2 |

Blast radius: the 16 suites that use the in-memory store or `StoreLocator` — 853 tests before the new ones — all
green (none relied on create-as-upsert); solution build 0 errors; consumer Affiliate (the `StoreLocator` user,
fresh `new BatchSettings{…}` per call) builds and passes 82/82 (3 skipped).

**Spawned** — each is a cross-provider contract decision, not an in-memory edit, so fixing the test double alone
would make it diverge from the backends it stands in for:

- [[TASK-555]] — `SH-M306`, `SH-M309`: `Update` of a missing entity is a silent no-op on **every** backend, and
  `Save` returns its Guid.
- [[TASK-556]] — `SH-M313`, `SH-M314`: identity assignment follows **three** policies across ~55 sites (keep
  `Guid.Empty`; replace it; always overwrite — SQL and RavenDB bulk, by CR-L190's recorded decision, reopened there).
- [[TASK-557]] — `SH-M311`, `SH-M315`: the delegate's return is discarded at all 105 call sites; the in-memory
  store keeps the caller's live object.

Docs: spec `store-crud-contract.md` (create, update, bulk writes, filter delete, StoreLocator ×2, IsStoreOfType,
StoreException), `Birko.Data.InMemory/CLAUDE.md` ("behaves like a real backend, on purpose"),
`Birko.Data.Stores/CLAUDE.md`, CHANGELOG (behaviour changes + migration).

### 2026-10-10 — the first two

Done first because consumer **Affiliate** needs the Destroy latch; the other 13 findings are untouched.

- **SH-M307 (Destroy latch) — confirmed, fixed.** `Destroy()` / `DestroyAsync(ct)` are now non-virtual wrappers
  in `AbstractStore` / `AbstractAsyncStore` over a new abstract `DestroyCore()` / `DestroyCoreAsync(ct)`; the
  wrapper clears `_initialized` in a `finally` (a destroy that throws leaves storage in an unknown state, so it is
  re-provisioned rather than trusted). Rule 12: the reset could not live in 44 overrides that each had to remember
  it. All 44 production overrides and 19 test doubles renamed, compiler-driven (CS0506 at each site, two rounds,
  0 errors). ⚠ **Breaking for anyone deriving from a store** — CHANGELOG carries the migration.
- **Found on the way, and guarded:** a renamed override still calling `base.DestroyAsync(ct)` now lands on the
  wrapper, which dispatches back to the override — measured as a **stack overflow that killed the test host** in
  two test doubles (`AsyncBulkRepositoryDestroyTests`, `BulkViewModelRepositoryDestroyTests`, now
  `base.DestroyCoreAsync`). A consumer migrating by hand would hit the same, so a re-entrant `Destroy*` on the
  same flow now throws `InvalidOperationException` naming `base.DestroyCore` instead of recursing.
- **SH-M316 (InitCore re-entrancy) — confirmed both halves, fixed.** A per-instance `AsyncLocal<bool>` (the
  TASK-270 shape) marks the flow running this store's init; `EnsureInitialized*` returns at once on it. A seeding
  `Create` inside `InitCore` runs once in both bases; other flows still wait.
- **Proof** — `tests/Birko.Data.Tests/Stores/StoreInitializationLatchTests.cs` (9). Mutation: latch reset removed
  → 4 red; re-entrancy pass removed → 2 red (sync: a second `InitCore`; async: the 5 s timeout, where it used to
  hang). Contract pin, passes either way: another flow still waits for init. The recursion guard has no clean red
  — without it the host dies, which is the measurement above. Suites: Birko.Data 229, Repositories 18, ViewModel
  37, Sync 64, Validation 130, InMemory 74, JSON 37, XML 32, SQLite 440, Patterns 68, InfluxDB.ViewModel 5 — all
  green; solution build 0 errors. Live Mongo / Raven / Cosmos / Elastic / Influx suites not run (rename only in
  those files).
- Verdicts written into `SPEC-HARVEST-FINDINGS-2026-07-30.md`; spec `store-crud-contract.md` updated by hand
  (requirement and both scenarios), not regenerated; `docs/store-implementation.md` and the `new-store-backend`
  skill corrected (it also said to override `Init()` and to use `Destroy` for closing connections).

## Out of scope

- Deferred to [[TASK-555]] — `Update` of a missing entity / `Save`'s Guid (`SH-M306`, `SH-M309`).
- Deferred to [[TASK-556]] — one identity-assignment policy across backends (`SH-M313`, `SH-M314`).
- Deferred to [[TASK-557]] — the delegate's return value and in-memory snapshots (`SH-M311`, `SH-M315`).

- The other 406 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-182]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A — fully covered by automated tests: a library contract with no UI surface, and every fix carries a
red-verified regression test (the one exception, `SH-M303`'s torn read, has no reproducible red at all).

## Implementation plan

_Populated by `/tasks plan TASK-163` — leave empty until then._
