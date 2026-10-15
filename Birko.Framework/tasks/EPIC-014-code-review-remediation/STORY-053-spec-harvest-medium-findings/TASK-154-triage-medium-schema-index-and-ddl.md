---
id: TASK-154
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
findings: [SH-M221, SH-M222, SH-M223, SH-M224, SH-M225, SH-M226, SH-M227, SH-M228, SH-M229, SH-M230, SH-M231, SH-M232, SH-M233, SH-M234, SH-M235, SH-M236, SH-M237, SH-M238, SH-M239, SH-M240, SH-M241, SH-M242, SH-M243, SH-M244, SH-M245, SH-M432, SH-M433]
pr: null
github-issue: null
jira-key: null
---

# Triage the 27 medium spec-harvest findings in `schema-index-and-ddl`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **25** findings
that fall in the `schema-index-and-ddl` area.

**Findings:** `SH-M221`–`SH-M245` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: schema-index-and-ddl`, lines 1794–1945.

**The contract under review** is specced in [`docs/specs/schema-index-and-ddl.md`](../../../docs/specs/schema-index-and-ddl.md), harvested from
16 source globs — `../Birko.Data.Patterns/Schema/*.cs`, `../Birko.Data.Patterns/IndexManagement/*.cs`, `../Birko.Data.SQL/Attributes/*.cs`, and 13 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/schema-index-and-ddl.md` lying until `/specs regen schema-index-and-ddl` runs, and **that spec diff is the fix's evidence**.

### Added by the 2026-10-12 spec regen (harvester claims, not reproduced)

- **SH-M432** — RavenDB index manager (`Birko.Data.RavenDB/IndexManagement/RavenDBIndexManager.cs`): `Unique`, `Sparse` and `ExpireAfter` on a definition are silently ignored, the auto-generated map interpolates scope and field names into map text, and listing reports no fields
- **SH-M433** — Cosmos DB index manager (`Birko.Data.CosmosDB/IndexManagement/CosmosDBIndexManager.cs`): the definition name and flags are not stored, so `ExistsAsync` by the declared name is false, and `DropAsync` never removes composite indexes

**Verdicts (2026-10-15).** **SH-M432: CONFIRMED, fixed.** `RavenDBIndexManager.CreateAsync` dropped `Unique` / `Sparse` /
`ExpireAfter` and interpolated the scope and field names into map code the server compiles. The flags are refused, and a
generated map takes plain identifiers only (a hand-written map still goes in the `Map` property). Listing reports no
fields because RavenDB's index statistics carry none; recovering them means parsing map text, so that half is waived.
`tests/Birko.Data.RavenDB.Tests/IndexManagement/RavenDBIndexManagerTriageTests.cs`, 5 of 5 red before. **SH-M433:
CONFIRMED by reading, spawned as [[TASK-628]].** A Cosmos indexing policy has no index names, so finding an index by its
declared name needs a decision on what a name means there.

## Acceptance criteria

- [x] All 27 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [ ] Any behavioural fix is followed by `/specs regen schema-index-and-ddl`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Resolution (2026-10-15)

**27 findings triaged: 27 confirmed, 0 narrower, 0 refuted.** Fixed here: 19. Verified fixed by other tasks: 3
(SH-M229 by TASK-245, SH-M230 by TASK-249, SH-M231 by TASK-622). Owned by an open task: SH-M234 by [[TASK-560]].
Spawned: SH-M245 as [[TASK-627]] (its proposed remedy is wrong: an abstract `Build()` cannot catch an omitted call)
and SH-M433 as [[TASK-628]] (a design decision). Verdicts are in `SPEC-HARVEST-FINDINGS-2026-07-30.md`, and for
SH-M432/M433 above.

- **`LoadTable` throws now**, with `TryLoadTable` for the callers whose null is a real answer. All 32 null-checking
  call sites were read first. Most were silently answering "nothing": a `Select` returned 0 rows, MySQL's bulk insert
  inserted none, an aggregate yielded nothing. The ones kept are view types in rule-field resolution, drift, the cache
  prefix, `GetPrimaryFields`' Guid fallback and the view translator.
- **Behaviour changes, recorded:** a get-only property is no longer a column (SH-M240); an inherited `[IndexedField]`,
  an `[IndexedField]` on an unmapped property, and a `long`-backed enum are refused at table load; ElasticSearch index
  creation refuses `Unique` / `Sparse` / `ExpireAfter` and unknown properties. Consumers were checked first: none
  declares an `[IndexedField]` on a base class.
- **Two live corrections.** NEST marks a 404 HEAD invalid, so "exists" is decided by status code. PostgreSQL folds the
  bare column names, so the live test compares them case-insensitively (the product was right).

**Red-verified, as numbers.** SQLite `SchemaDdlTriageTests` 9 of 10 (pin: tie order, which happened to be stable).
Live `IndexManagerTriageLiveTests`: PostgreSQL 2 of 2, MySQL 2 of 2. `ModelMapRenameIndexTests` 1 of 1. ElasticSearch
`IndexManagementTriageTests` 11 of 13 (pins: a missing index answers no; a text shard count). RavenDB 5 of 5. The
SQLite and ElasticSearch files use the new `TryLoadTable` / `CleanupError`, so the red run stripped those lines.

**Spec regen pending**, batched with the database fixes: `schema-index-and-ddl`.

## Out of scope

- The other 396 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-192]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A: fully covered by automated tests. This is a library contract with no UI surface, and every fix is asserted
against a real database, a live ElasticSearch, or a recorded transport.

## Implementation plan

_Populated by `/tasks plan TASK-154` — leave empty until then._
