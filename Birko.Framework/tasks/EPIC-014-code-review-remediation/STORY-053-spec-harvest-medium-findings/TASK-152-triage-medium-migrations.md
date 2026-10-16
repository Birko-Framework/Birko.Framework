---
id: TASK-152
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
findings: [SH-M172, SH-M173, SH-M174, SH-M175, SH-M176, SH-M177, SH-M178, SH-M179, SH-M180, SH-M181, SH-M182, SH-M183, SH-M184, SH-M185, SH-M186, SH-M187, SH-M188, SH-M189, SH-M190, SH-M191, SH-M192, SH-M193, SH-M194, SH-M195, SH-M196, SH-M197, SH-M198, SH-M199, SH-M200, SH-M201, SH-M202, SH-M203, SH-M204]
pr: null
github-issue: null
jira-key: null
---

# Triage the 33 medium spec-harvest findings in `migrations`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **33** findings
that fall in the `migrations` area.

**Findings:** `SH-M172`–`SH-M204` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: migrations`, lines 1496–1695.

**The contract under review** is specced in [`docs/specs/migrations.md`](../../../docs/specs/migrations.md), harvested from
8 source globs — `../Birko.Data.Migrations/**/*.cs`, `../Birko.Data.Migrations.SQL/**/*.cs`, `../Birko.Data.Migrations.MongoDB/**/*.cs`, and 5 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/migrations.md` lying until `/specs regen migrations` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 33 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen migrations`, with the spec diff reviewed as the change's — done 2026-10-16 (batched regen of 17 areas, diffs reviewed and confirmed)
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 388 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-178]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Resolution (2026-10-15)

**33 findings triaged: all confirmed (2 wider, 2 narrower); 25 fixed here, 8 found fixed earlier.** Fixed earlier, verified
by reading: SH-M173 (TASK-246), M177, M178, M179, M180 (TASK-247), M185 (TASK-450), M190 (TASK-618), M204 (TASK-255).

- **Runner** (`AbstractMigrationRunner`): selection by the applied set, so a back-dated migration runs and a rollback
  undoes only what was applied (M182, M183); `RunFailed` blames the right migration, or none after the loop (M172).
- **One JSON filter reader** (`MigrationFilterTerms`, shared by SQL, ElasticSearch, RavenDB, Cosmos DB): unknown operators,
  array values and null orderings refused (M175); null and `$ne` mean what MongoDB means on every backend (M176, wider:
  a bare `null` and three backends' own null handling were wrong too). `MigrationFilter.RefuseTransform` (M196).
- **SQL:** `CollectionExists` uses the new per-provider `AbstractConnectorBase.TableExistsSql`, scoped as each DDL resolves
  a bare name (M181).
- **Cosmos DB:** the collection is created by `Build()` with the declared key, a non-id primary or unique field refused
  (M174); count reads every page (M184); the state document is ETag-checked (M186); `CreateIndex(...).Build()` goes
  through `CosmosDBIndexManager` (M200).
- **RavenDB:** optimistic concurrency on the state document (M187); `CreateIndex(...).Build()` through
  `RavenDBIndexManager` (M200). **Unfiled, found live:** `CountDocuments` passed the collection as the INDEX name and threw
  on every call; fixed.
- **ElasticSearch:** count checked (M197); by-query conflicts and failures refused (M198, by reading); rename checked,
  parameterised and scoped to documents with the field, and update field names parameterised too (M199); new migrations
  index maps Version as long, and a partial read is refused (M201); `Initialize` checks its probe and create (M202).
- **InfluxDB:** removal finds the version's own points (M188); version tag (M189); update, filtered count, missing bucket,
  field-less document refused (M191, M192, M193, M195); copy reconstructs fields and tags (M194). **Also:** applied
  versions were read from `-10y`, so an old fixed CreatedAt read as never applied; now from the epoch.
- **MongoDB:** the migration index builder takes its keys from `MongoDBIndexManager.KeysFor` (M203).

**Red-verified, as numbers.** Every production file reverted to HEAD with the tests kept (the `RunFailed` test excluded:
new API), 40 new or inverted tests run live: **32 failed, 8 passed.** The 8 are pins: two selection tests whose applied set
equals the old range, five operator mappings (`$eq` fell through to `=` already) and SQLite's scope test (no other schema
to put a table in). Inverted rather than deleted (rule 56): the TASK-274 refusal tests on RavenDB and Cosmos DB, the
`$ne` SQL and Cosmos pins, the runner's range-selection pins and the Painless source pin.

**Measured live** on SQLite, PostgreSQL, MySQL, SQL Server, MongoDB, RavenDB, ElasticSearch 7.17, InfluxDB 2 and the
Cosmos emulator. Not reproduced: SH-M198's version conflict (needs a writer racing the request).

**Regression:** the 44 suites importing a migration project, a SQL project or Birko.Data.MongoDB (3,034 tests), live on every provider above, green on the first run.

**Spec regen pending**, batched: `migrations`.

## Human test plan

N/A: fully covered by automated tests, asserted live against every provider; a library contract with no UI surface.

## Implementation plan

_Populated by `/tasks plan TASK-152` — leave empty until then._
