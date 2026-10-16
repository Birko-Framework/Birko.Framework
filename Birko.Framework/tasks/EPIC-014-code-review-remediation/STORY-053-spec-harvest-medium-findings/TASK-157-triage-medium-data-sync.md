---
id: TASK-157
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
findings: [SH-M049, SH-M050, SH-M051, SH-M052, SH-M053, SH-M054, SH-M055, SH-M056, SH-M057, SH-M058, SH-M059, SH-M060, SH-M061, SH-M062, SH-M063, SH-M064, SH-M065, SH-M066, SH-M067, SH-M068, SH-M069, SH-M436]
pr: null
github-issue: null
jira-key: null
---

# Triage the 22 medium spec-harvest findings in `data-sync`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **21** findings
that fall in the `data-sync` area.

**Findings:** `SH-M049`–`SH-M069` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: data-sync`, lines 746–873.

**The contract under review** is specced in [`docs/specs/data-sync.md`](../../../docs/specs/data-sync.md), harvested from
9 source globs — `../Birko.Data.Sync/**/*.cs`, `../Birko.Data.Sync.Sql/**/*.cs`, `../Birko.Data.Sync.ElasticSearch/**/*.cs`, and 6 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/data-sync.md` lying until `/specs regen data-sync` runs, and **that spec diff is the fix's evidence**.

### Added by the 2026-10-12 spec regen (harvester claims, not reproduced)

- **SH-M436** — `CosmosSyncKnowledgeItem` (`Birko.Data.Sync.CosmosDB/Models/CosmosSyncKnowledgeItem.cs:91-104`): a pass-through item gets its Guid from `EnsureGuid()` (TASK-556), but a foreign item is still copied with `item.Guid ?? NewGuid()`, so a foreign `Guid.Empty` is kept as a key

## Acceptance criteria

- [x] All 22 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen data-sync`, with the spec diff reviewed as the change's — done 2026-10-16 (batched regen of 17 areas, diffs reviewed and confirmed)
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 400 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-177]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Resolution (2026-10-15)

**22 findings triaged: 21 confirmed (one broader), 1 narrower, 0 refuted.** Fixed here: SH-M049–M059, M061, M067,
M068, M069, and the engine side of M064. Handed to [[TASK-630]] (rebuilding the tenant provider on this engine, next in
the queue): SH-M060, M062, M063, M065, M066, M436 and the Raven/Cosmos side of M064, because those stores implement
neither knowledge-store interface and their only consumer is the provider being rebuilt. TASK-630's own SH-M350 is fixed
by this task's queue change. Verdicts are in `SPEC-HARVEST-FINDINGS-2026-07-30.md`; SH-M436 has no entry there.

**Three defects the harvest did not file, measured here:**

1. **A deletion was never propagated; it was resurrected.** The engine read deletion from the stored
   `IsLocalDeleted` / `IsRemoteDeleted`, which every backend sets to "this side's hash was empty when the row was
   written". After a synced round both are false, so a deletion on one side was re-created by the next bidirectional
   run (measured: `created=1, deleted=0`).
2. **A local-only item present at the first sync was deleted by the second** under `RemoteWins`: the initial sync
   skipped it but recorded a one-sided row, which read as "deleted remotely" (measured: `deleted=1, local=0`).
3. **The generic engine never found its knowledge on ElasticSearch.** Its filter `k => k.Scope == s` is written against
   `TKnowledge`, so `k.Scope` bound to `ISyncKnowledgeItem.Scope`, lost the model's `[Keyword(Name = "scope")]`, and
   matched nothing: every run inserted its knowledge afresh (measured live, two rows per entity after two runs). Fixed
   in the shared `ExpressionNormalizer` (SQL, ElasticSearch and RavenDB filters all pass through it), which now rebinds
   an interface member to the concrete property, the same hole SH-M317 closed for SQL `SET` in TASK-159.

**The engine's decision is rebuilt on a known pair** (a row recorded while both sides held the item): a deletion is a
known pair with one side missing, unchanged is Skip, one side changed wins, both changed is the policy, and knowledge
is written only for a reconciled state (both present or both deleted). A scope marker row (`Guid.Empty`) ends the
initial sync for a run that reconciled nothing. Ten existing tests seeded the old tombstone shape (flag true, no
versions), which only the bug wrote; they are re-seeded as a recorded pair. Seven counted knowledge rows and now
exclude the marker. `docs/sync.md` described an API that does not exist and is rewritten around the real one.

**Behaviour changes on purpose:** `Merge` fails the item; `Preview` throws on a store error; a non-positive `BatchSize`
throws; save filters guard deletes; a filtered `SetLastSyncTime` returns null for an empty scope; an aggregate
definition with a duplicate or keyless navigation is refused. **Migration:** a MongoDB knowledge collection written
before TASK-219 (ObjectId `_id`) cannot be read; drop `MongoSyncKnowledgeItem` and the next run rebuilds it.

**Red-verified, as numbers.** `SyncEngineTriageTests` 19 of 20 (pin: a failing conflict resolution, which the old code
reached through the update arm; the `BatchSize = -1` case hung 7 min 21 s). `SqlSyncKnowledgeStoreStampTests` 1 of 2
(pin: every row stamped; the open-reader interleaving did not fail on SQLite). `AggregateDefinitionValidationTests` 2 of 3.
`MongoSyncKnowledgeLiveTests` 1 of 1 (live). `ElasticSyncKnowledgeLiveTests` 1 of 1 (live). `ExpressionNormalizerTests`
rebinding 1 of 2 (pin: an explicit implementation keeps the interface member). `JsonSyncInitialScopeTests` 0 of 1: a pin
for the marker, because on the old code the one-sided rows ended the initial state.

**Regression:** the normaliser change reaches every SQL, ElasticSearch and RavenDB filter, so every suite importing
`Birko.Data.Core` ran: 121 suites, 6,604 tests, green, live against PostgreSQL, MySQL, SQL Server, TimescaleDB,
ElasticSearch, MongoDB and RavenDB (Redis, CosmosDB and InfluxDB skipped their live tests: no container). No consumer
references `Birko.Data.Sync*` or `Birko.Data.Aggregates`.

**Spec regen pending**, batched: `data-sync`.

## Human test plan

N/A: fully covered by automated tests; a library contract with no UI surface.

## Implementation plan

_Populated by `/tasks plan TASK-157` — leave empty until then._
