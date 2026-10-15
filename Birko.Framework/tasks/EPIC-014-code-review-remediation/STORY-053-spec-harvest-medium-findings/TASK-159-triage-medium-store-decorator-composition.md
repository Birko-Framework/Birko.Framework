---
id: TASK-159
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
findings: [SH-M317, SH-M318, SH-M319, SH-M320, SH-M321, SH-M322, SH-M323, SH-M324, SH-M325, SH-M326, SH-M327, SH-M328, SH-M329, SH-M330, SH-M331, SH-M332, SH-M333, SH-M334, SH-M335, SH-M336, SH-M435]
pr: null
github-issue: null
jira-key: null
---

# Triage the 21 medium spec-harvest findings in `store-decorator-composition`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **20** findings
that fall in the `store-decorator-composition` area.

**Findings:** `SH-M317`–`SH-M336` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: store-decorator-composition`, lines 2382–2503.

**The contract under review** is specced in [`docs/specs/store-decorator-composition.md`](../../../docs/specs/store-decorator-composition.md), harvested from
5 source globs — `../Birko.Data.Patterns/Decorators/*.cs`, `../Birko.Data.Patterns/Concurrency/*.cs`, `../Birko.Data.Patterns/Models/*.cs`, and 2 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/store-decorator-composition.md` lying until `/specs regen store-decorator-composition` runs, and **that spec diff is the fix's evidence**.

### Added by the 2026-10-12 spec regen (harvester claims, not reproduced)

- **SH-M435** — Save routing (`Birko.Data.Stores/StoreSave.cs:25-82`, `Birko.Data.Patterns/Concurrency/VersionedStoreWrapper.cs:42-58`): on a versioned wrapper a missing row throws `ConcurrentUpdateException`, not `EntityNotFoundException`, so StoreSave's create fallback for an unstored Guid never runs — contrary to TASK-555's comment

## Acceptance criteria

- [x] All 21 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [ ] Any behavioural fix is followed by `/specs regen store-decorator-composition`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 401 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-187]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Resolution (2026-10-15)

**21 findings triaged: 20 confirmed, 1 narrower, 0 refuted.** 15 fixed here (SH-M317, M319, M320, M322, M323, M325, M326,
M329–M335, M435) plus M324 (narrower); SH-M318 found already fixed (TASK-555); SH-M321 waived; SH-M327/M328 spawned as
[[TASK-632]], SH-M336 as [[TASK-631]]. Verdicts are in `SPEC-HARVEST-FINDINGS-2026-07-30.md`; SH-M435, added by the
2026-10-12 regen, has no entry there, so its verdict is here.

- **SH-M435, CONFIRMED, fixed.** A versioned `Update` of a missing row threw `ConcurrentUpdateException`, so `StoreSave`'s
  create fallback never ran and `Save` of an unstored Guid threw. It is `EntityNotFoundException` now (still no silent
  pass, CR-M125). The two tests pinning the conflict are inverted; `Save` of an unstored Guid creates at Version 1.
- **Behaviour changes on purpose:** a soft-deleted row is not found by an update (it threw nothing and resurrected the
  row); an already-deleted row keeps its `DeletedAt`; a filtered soft delete or a Default filtered update refuses a null
  or reducing-to-all filter; `StoreWrapperBuilder.Build` refuses an `IVersioned` model; Default's filtered update is one
  collection write on both paths; `PropertyUpdate<T>.Clone()` is new.
- **Waived, SH-M321:** `PrevUpdatedAt` in a native `PropertyUpdate` needs a column-to-column assignment, which does not
  exist, and an RMW fallback would break `Increment` atomicity (TASK-498).
- **Narrower, SH-M324:** no shipped store holds a row without a Guid, so the hole is unreachable through one. Fixed for
  clarity, untested.

**Red-verified, as numbers.** `DecoratorCompositionTriageTests` 15 of 23 red on the old code (pins: the lazy batch
enumerated once, InMemory enumerates once; an update keeps its own slug; a property update not touching `IsDefault`; the
two filtered-action one-default tests and the outside-filter one, InMemory survived the nested write; the Default
reducing-filter refusal, the inner store's guard caught it before; the filtered soft delete keeping a deleted row's
instant). `VersionedStoreWrapperTests` 4 of 4 new or inverted. `StoreWrapperBuilderTests.Build_refuses_an_IVersioned_model`
1 of 1. `InterfaceMemberColumnRemapTests` (SQLite) 1 of 1, as `no such column: UpdatedBy`.

**Regression:** the 87 suites importing `Birko.Data.Patterns`, `Birko.Data.Composition` or `Birko.Data.SQL` (4,655
tests) are green: 84 live against PostgreSQL, MySQL, SQL Server, TimescaleDB, ElasticSearch, MongoDB and RavenDB; the
Redis, CosmosDB and InfluxDB suites without their live tests (no container). Symbio's unit suite (it builds stores
through `StoreWrapperBuilder`): 3,958 green; 2 failures read Symbio's own locale files, unrelated.

**Spec regen pending**, batched: `store-decorator-composition`.

## Human test plan

N/A: fully covered by automated tests; a library contract with no UI surface.

## Implementation plan

_Populated by `/tasks plan TASK-159` — leave empty until then._
