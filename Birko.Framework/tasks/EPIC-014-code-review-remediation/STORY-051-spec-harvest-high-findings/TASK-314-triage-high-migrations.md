---
id: TASK-314
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P1
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H029, SH-H030, SH-H031, SH-H032, SH-H033]
pr: 8b18f2f
picked-by: fix-next
github-issue: null
jira-key: null
---

# Triage the 5 remaining high spec-harvest findings in `migrations`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **5** open findings in the `migrations` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H029` | ElasticSearch GetAppliedVersions returns an empty set on an INVALID search response | `Birko.Data.Migrations.ElasticSearch/ElasticSearchMigrationStore.cs:88` |
| `SH-H030` | InfluxDB GetAppliedVersions swallows every InfluxException, re-running everything | `Birko.Data.Migrations.InfluxDB/InfluxMigrationStore.cs:113` |
| `SH-H031` | MongoDB version rows are written without the session, surviving AbortTransaction | `Birko.Data.Migrations.MongoDB/MongoMigrationRunner.cs:78` |
| `SH-H032` | An empty operator object degrades the filter to match-all on delete/update | `Birko.Data.Migrations.SQL/Context/SqlDataMigrator.cs:152` |
| `SH-H033` | InfluxDB migration bucket has a 365-day expiry, so applied versions expire | `Birko.Data.Migrations.InfluxDB/InfluxMigrationStore.cs:47` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: migrations`, lines 224-254.

**The contract under review** is specced in [`docs/specs/migrations.md`](../../../docs/specs/migrations.md),
harvested from 51 source files — `../Birko.Data.Migrations.CosmosDB/Context/CosmosDBDataMigrator.cs`, `../Birko.Data.Migrations.CosmosDB/Context/CosmosDBMigrationContext.cs`, `../Birko.Data.Migrations.CosmosDB/Context/CosmosDBSchemaBuilder.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Partly live.** `Birko.Data.Migrations.SQL` is imported by **3** consumer aggregators, but `SqlDataMigrator` (which `SH-H032` names) has **0** consumer `.cs` references, and no consumer selects the ElasticSearch / InfluxDB / MongoDB stores the other four findings name.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** includes `SH-H032`, a match-all degrade on delete/update — [[TASK-109]]'s family, which was P0 — but reachable only from a migration a consumer authors, not from an ordinary read or write. Rank that finding higher than its siblings when draining.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/migrations.md` lying until `/specs regen migrations` runs, and **that spec
diff is the fix's evidence**.

## Acceptance criteria

- [x] All 5 findings are marked **confirmed**, **confirmed-narrower**, **confirmed-wider** or **refuted** against
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
- [x] Any behavioural fix is followed by `/specs regen migrations`, with the spec diff reviewed as the
      change's evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [x] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- The other 34 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-152]] owns those.
- Low-severity findings in this area — [[TASK-178]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

`N/A — fully covered by automated tests.` This is a library contract with no UI and no visual or
copy judgement, so a human adds nothing a test cannot assert: every claim here is about rows left in a
table, documents left in a collection, or an exception reaching a caller, and all of those are asserted
mechanically (263 tests, eight mutations — see § Progress log step 6).

⚠ **Two automated tests are environment-gated and did not run in this session** — they are *not* manual
steps (no human judgement, no hardware), only infrastructure this machine did not have, since Docker was
not running:

- `MigrationVersionRowTransactionTests.An_aborted_run_leaves_no_version_row_behind` — needs a MongoDB
  **replica set**, not merely a mongod (transactions are unavailable on a standalone server, and the
  test skips loudly rather than passing if it finds one). `mongod --replSet rs0` + `rs.initiate()`, then
  `BIRKO_MONGO_HOST=localhost dotnet test`.
- `MigrationBookkeepingDurabilityTests.An_authentication_failure_is_not_reported_as_an_empty_applied_set`
  — needs a live InfluxDB to produce a server-*reported* failure. `BIRKO_INFLUX_HOST=localhost dotnet test`.

Both areas have offline cover (source scans), which is weaker and is labelled as such in place. Set
`BIRKO_REQUIRE_LIVE` to turn their absence into a failure rather than a skip.

## Implementation plan

_Populated by `/tasks plan TASK-314` — leave empty until then._

## Progress log

- step 2 — picked; ranked above TASK-317 because SH-H032 claims an unbounded destructive write (match-all degrade on migration delete\/update), which outranks TASK-317's silent loss of queued work on severity; and this is the only high-tier candidate measured partly-live (Birko.Data.Migrations.SQL imported by 3 consumer aggregators) where the other four measured 0 consumer references.
- step 3 — verified: all 5 CONFIRMED, 2 of them **wider** than filed.
  - `SH-H032` CONFIRMED as filed, 4 backends. `SqlDataMigrator.ParseFilterToWhere` (`:180-204`),
    `RavenDBDataMigrator.ParseFilterToRql` (`:131-172`), `CosmosDBDataMigrator.ParseFilterToSql`
    (`:220-259`) all return an empty clause for `{"status":{}}`, and each caller appends the WHERE only
    when non-empty. `ElasticSearchDataMigrator.ParseFilter` returns `BoolQuery { Must = [] }`, which ES
    treats as match-all. MongoDB and InfluxDB are **immune by a different mechanism** (Mongo hands the
    JSON to the driver, so `{status:{}}` is an exact-match on an empty subdocument; Influx refuses a JSON
    filter outright, CR-M111) — pinned, not fixed.
  - `SH-H029` CONFIRMED-**WIDER**. The filed `if (!searchResponse.IsValid) return new HashSet<long>();`
    holds, and the `_client.Indices.Exists(indexName).Exists` gate one line above it has the identical
    defect and fires first: NEST's `ExistsResponse.Exists` is `HttpStatusCode == 200`, so an unreachable
    or unauthorized cluster answers "the index does not exist" and returns the same empty set.
  - `SH-H030` CONFIRMED. `catch (InfluxException) { }` at `InfluxMigrationStore.cs:113`; the existing
    CR-L146 comment already concedes it cannot separate an empty bucket from an auth/connectivity failure.
  - `SH-H031` CONFIRMED. `MongoMigrationRunner.cs:78` calls `store.RecordMigration`, which issues a
    **sessionless** `ReplaceOne` (`MongoMigrationStore.cs:111`). `RemoveMigration`'s `DeleteOne` has the
    same defect on the Down path — same root cause, so fixed together.
  - `SH-H033` CONFIRMED-**WIDER**. The 365-day `Expire` rule holds (`InfluxMigrationStore.cs:47`), and
    the points are timestamped `.Timestamp(migration.CreatedAt, …)` (`:148`), which is the migration's
    *authored* date. So a migration authored more than a year ago is not merely expired later — it falls
    outside the retention window at write time and is never durably recorded at all.
- step 4 — layer: **local** to the framework, but across six sibling repos. `SH-H032`'s shared guard goes
  in `Birko.Data.Migrations` (the base every migrator already imports) rather than four times — the
  one-producer rule, same shape as `BoundedFilterGuard` in `Birko.Data.Core` (§ TASK-329). No dependency
  or upstream package is implicated.
- step 5 — fix in 8 production files across 6 repos; tests in 7 files across 7 suites.
  - `SH-H032`: one producer, `Birko.Data.Migrations/Context/MigrationFilter.cs`, wired into all four
    translating migrators (SQL, ElasticSearch, RavenDB, CosmosDB) on **all three** filter-taking verbs —
    update, delete **and count**. `WholeTableWriteException.ForDataFilter` added to `Birko.Data.Core` so the
    refusal is the framework's own type (§ Conventions: do not invent a per-backend exception) while naming
    a door a JSON-filter caller actually has.
  - `SH-H029`: both gates in `ElasticSearchMigrationStore.GetAppliedVersions` now throw.
  - `SH-H030`: the `InfluxException` swallow rethrows. `SH-H033`: retention `0` (infinite).
  - `SH-H031`: `MongoMigrationStore.EnterSession` — a self-restoring scope the runner enters — and both
    bookkeeping writes take the session overload.
  - Suites: Migrations 29, SQL 125, ES 23, Raven 20, Cosmos 29, Mongo 14, Influx 23 = **263/263**
    (205 before, 58 new). Plus 894 in four `Birko.Data.Core`-consuming suites, to show the exception
    change did not ripple: Core 102, InMemory 74, JSON 23, SQL 695 — 0 failed.
- step 6 — eight disjoint mutations, each reverted from an explicit backup copy, working tree verified
  byte-identical afterwards.
  | # | Mutation | Split |
  |---|---|---|
  | A | `MigrationFilter.RequireBounded` → no-op | **16 of 263**, across exactly the 5 guard-using suites |
  | B | `IsExplicitMatchAll` → always false (guard too broad) | **14** — the contract pins bite |
  | C | revert **only** the unfiled `Indices.Exists` half of SH-H029 | **3 of 23** (ES) |
  | D | restore the SH-H030 swallow | **1 of 23** (Influx) |
  | E | restore the 365-day retention | **1 of 23** (Influx) |
  | F | restore sessionless Mongo bookkeeping writes | **1 of 14** (Mongo) |
  | G | ES `ParseFilter` reports a constant term count | **3 of 23** (ES) |
  | H | Raven `ApplyFilterToQuery` returns a constant | **1 of 20** (Raven) |
  - **Fix-dependent (evidence):** every test in `DegradedFilterWholeTableWriteTests`,
    `MigrationFilterTests`, the three `DegradedFilterRefusalTests`, `AppliedVersionsFailureTests`,
    `MigrationBookkeepingDurabilityTests`, and
    `MigrationVersionRowTransactionTests.Both_bookkeeping_writes_take_…`.
  - **Contract pins (NOT evidence — pass either way):** every `…_is_not_refused` /
    `The_explicit_match_all_door_…` / `An_ordinary_…` test;
    `AppliedVersionsFailureTests.The_write_path_still_throws_on_a_failed_response`;
    `MigrationBookkeepingDurabilityTests.A_transport_failure_still_propagates`;
    `MigrationVersionRowTransactionTests` scope-semantics pair.
  - **Gated, did not run here:** `An_aborted_run_leaves_no_version_row_behind` (needs a MongoDB
    **replica set**; Docker was not running) and
    `An_authentication_failure_is_not_reported_as_an_empty_applied_set` (needs a live InfluxDB). Both skip
    loudly under `BIRKO_REQUIRE_LIVE`. Their areas are covered offline by source scans, which are weaker and
    are labelled as such in place.
  - ⚠ Two mutations initially failed **0**, and both were my tests being wrong rather than the fix:
    mutation D showed the SH-H030 tests never reached the swallow (see § Outcome), and mutation H showed the
    Raven count scan could not see the counting it depended on. Both were rewritten until they bit.
- step 7 — respecced `migrations` (`/specs regen migrations`, stable-wording rule: 99 insertions,
  29 deletions in a 1,312-line spec — surgical, not a rephrase).
  - **Requirements changed:** *MongoDB migrations join a session transaction only on a replica set*
    (bookkeeping now joins); *The ElasticSearch migration store … refuses to read a failure as "nothing
    applied"* (renamed — the old title asserted the defect); *The InfluxDB migration store* (infinite
    retention, failure reported); *The data migrator's filter argument …* (the degraded-filter refusal,
    its verb family, and the two backends that are immune by a different mechanism).
  - **Scenarios:** 3 rewritten (each had documented a defect as shipped behaviour — *"the store's
    `ReplaceOne` is issued **without** the session"*, *"an empty `HashSet<long>` is returned"*, *"the
    exception is swallowed and an empty set is returned"*), 6 added.
  - **Diff review: every behavioural change maps to a confirmed finding in this task. Nothing
    unexplained, so no `/tasks spawn` from the diff.**
  - Stamp: `generated-at` 283dbff, `generated-on` 2026-09-17, 8 sibling `source-commits` refreshed,
    `shaped-by-unresolved` **re-derived 80 → 151** rather than carried forward (289 feature-linked tasks;
    the rise is the ~132 `todo` tasks STORY-051's intake filed, unresolved by construction — not evidence
    lost). ⚠ The sibling SHAs initially named the commits the harvest **read** (the documented dirty-tree case);
    corrected to the commits that carry the fixes before the aggregator commit, so the staleness baseline
    is exact rather than one commit behind on eight repos.
  - **Unmapped check: 1** — `../Birko.Data.Migrations.TimescaleDB/ContinuousAggregateParts.cs`, covered by
    the map's own glob but absent from the last resolved source list (it arrived with TASK-260). Added to
    `sources:`; `.map.yml` needs no change.

## Outcome

**All 5 findings CONFIRMED — 2 of them wider than filed. None refuted.** Five migration defects that all
end in the same place: a migration runner doing something far larger than the author asked for, silently.

### What the fix was

- **`SH-H032` — a filter that names fields but produces no terms was read as "no filter".** `{"status":{}}`
  takes the object branch in every translator and its operator loop adds nothing, so the clause came back
  empty and each caller appended the constraint *only when non-empty* — `DELETE FROM {table}` with no
  `WHERE`. Confirmed on all four named backends (SQL, ElasticSearch, RavenDB, CosmosDB). The rule now lives
  once in `Birko.Data.Migrations.Context.MigrationFilter` and refuses with the framework's own
  `WholeTableWriteException`.
- **`SH-H029` / `SH-H030` — a read that *failed* answered "nothing has been applied".** `GetCurrentVersion()`
  then yields 0 and `Migrate()` replays every registered migration, destructive `Up` bodies included,
  against an already-migrated cluster or database. Both now throw.
- **`SH-H031` — the MongoDB version row was written sessionlessly** inside a runner transaction, so it
  committed immediately and survived the `AbortTransaction()` that discarded the data it described. The
  store now joins the session through a self-restoring scope.
- **`SH-H033` — the InfluxDB bookkeeping bucket expired after 365 days.**

### Judgement calls, and what was rejected

- **One producer in `Birko.Data.Migrations`, not four copies.** The stricter-looking alternative — a guard
  per backend — is the shape § Conventions repeatedly records as the cause (TASK-329's `BoundedFilterGuard`
  is the precedent). Priced first: all four consumer aggregators that import Migrations already import
  `Birko.Data.Core`, so reusing `WholeTableWriteException` breaks no build. **Rejected: a new per-backend
  exception type** — § Conventions forbids it explicitly, so one `catch` still selects the refusal everywhere.
- **A new `ForDataFilter` factory rather than the existing constructor.** The existing wording ends by
  offering an `x => true` predicate, and a caller holding a JSON string has no expression tree to write one
  in. Naming it would be the exact defect `WholeTableWriteException`'s own remarks warn about — a message
  pointing at a door the reader cannot take. Pinned by a test asserting the message does *not* say it.
- **The guard covers `CountDocuments` too, which the finding did not ask for.** A count answering for the
  whole collection while a delete built from the identical filter is refused is § TASK-215's *guard the
  whole verb family* and § TASK-313's *a destructive statement must not select a different set than its own
  read*. Safe to widen because refusing cannot break working code here: the only way to mean "everything"
  in this dialect is the explicit `{}` door, which is untouched and pinned.
- **MongoDB and InfluxDB were left alone, deliberately, and it is recorded.** Mongo hands the parsed
  document to the driver, where `{"status":{}}` is an exact match on an empty subdocument — not a match-all;
  Influx refuses a JSON filter outright (CR-M111). Immune by a *different mechanism*, so "fix all six from
  symmetry" would have been change without a defect.
- **The Cosmos guard was hoisted above `GetPartitionKeyProperty`**, whose `ReadContainerAsync` is a network
  round trip. Refusing should not first cost a request — and it is what makes the refusal assertable offline.
- **Influx `RecordMigration` still stamps `migration.CreatedAt`.** With infinite retention that is durable,
  and re-applying a migration overwrites its own point rather than adding a second. Changing it was not
  required by the finding and would alter `RemoveMigration`'s delete window. Pinned so a later change that
  reinstates an expiry has to confront the pairing.

### What the session got wrong, and how

- **⚠ `SH-H030`'s first test suite was vacuous, and only mutation D revealed it.** Pointing the client at a
  closed port never reaches the swallow: `GetAppliedVersions` opens with `EnsureInitialized()` →
  `FindBucketsAsync()`, **outside** the try. § TASK-291 records this exact trap. The second attempt
  pre-seeded `_migrationsBucket` to reach `QueryAsync` inside the try — and a connection refusal *there*
  surfaces as a raw `HttpRequestException`, which is not an `InfluxException` and was never swallowed
  either. So the swallow only ever fired for failures Influx itself **reports**, which needs a live server.
  The offline cover is now an honest source scan plus a gated live test, and both facts are written into
  the test file so the next reader does not repeat the two attempts.
- **⚠ Mutation H exposed the same class of weakness in the Raven count scan** — it asserted the guard's call
  site but not the helper's counting, so a mutation making that helper always claim success failed nothing.
- **⚠ My Cosmos fixture's `OpenTcpConnectionTimeout` requires Direct mode**, and its `ArgumentException`
  read exactly like the fix failing. The suite also sat at 40 s waiting on SDK retries; the pins now assert
  the property that actually matters — a refusal is synchronous and pre-I/O — which took it to 6 s.

### Flagged, not fixed

- Two pre-existing CS8602 warnings in `Birko.Data.MongoDB/Stores/AsyncMongoDBStore.cs:113-114`, surfaced by
  building this area. Different project, untouched by this change — spawned as [[TASK-456]] rather than
  fixed here, because a defect fix must not quietly widen into a neighbouring project.
- The two environment-gated tests above (§ Human test plan) — infrastructure, not judgement.
- step 8 — merge gate run inline across 16 repos (the skills read one repo's diff; a polyrepo change
  needs the documented inline fallback).
  - **Standards ([[verify-birko-conventions]], invoked directly so step 0 was mandatory):** 3 findings,
    all fixed. Register-on-introduce required (b) `CLAUDE.md` § Conventions to record the fifth instance
    of the scope-guard family, (c) § Architecture to record the new
    `Birko.Data.Migrations -> Birko.Data.Core` edge — plus `Birko.Data.Migrations/CLAUDE.md`
    § Dependencies — and check 9 a `Recent Updates` entry. Checks 1-7,10 clean; 6/7/7b/8 N/A (no new
    project).
  - **Fidelity:** all 7 acceptance criteria met; scope widened in two places (`CountDocuments`, and both
    `SH-H029` gates) with the reason recorded on each.
  - **Correctness:** 2 defects **in my own diff**, both fixed — see below.
  - **Security:** ran (the diff touches data-access query construction). No exploitable finding. The new
    refusals are fail-closed, and `ForDataFilter` deliberately interpolates the *shape* (operation,
    collection, scope) and **never the filter JSON**, which carries caller data (§ TASK-308). ⚠ One
    pre-existing observation, not introduced here: `ElasticSearchMigrationStore` interpolates NEST's
    `DebugInformation` into an exception message, which can carry the request URI — `RecordMigration` has
    always done this, so the new read-path throw is consistent rather than a new class of exposure.
  - ⚠ **Two defects the gate caught in my own work, neither visible from the tests (263/263 were green
    with both present):**
    1. My Python writes used `utf-8-sig` + default newlines, which **added a BOM** to files that had none
       and rewrote line endings — turning a 1-line projitems edit into a whole-file diff across 8 files.
       Now written byte-faithfully against `git show HEAD:<file>`.
    2. Inserting `ForDataFilter` *above* the 4-arg constructor **orphaned that constructor's `<summary>`
       and both `<param>` blocks onto the new method**, leaving the constructor undocumented and the new
       method carrying two summaries with mismatched params. Moved below it, with its own `<param>` set.
  - **5d out-of-scope sweep:** all 5 existing bullets are boundaries naming their owners
    ([[TASK-152]], [[TASK-178]], the other 14 per-area tasks) — no spawn. One item surfaced *during* the
    work had no owner and got an id: [[TASK-456]].

### Landed commits

`pr:` names the producer (`MigrationFilter`); the change spans 16 repos and 15 commits.

| Repo | SHA | What |
|---|---|---|
| `Birko.Data.Core` | `cd3e0c4` | `WholeTableWriteException.ForDataFilter` |
| `Birko.Data.Migrations` | `8b18f2f` | **`MigrationFilter` — the one producer** |
| `Birko.Data.Migrations.SQL` | `f278fdb` | SH-H032 |
| `Birko.Data.Migrations.ElasticSearch` | `4add7c8` | SH-H029 + SH-H032 |
| `Birko.Data.Migrations.RavenDB` | `e0725e2` | SH-H032 |
| `Birko.Data.Migrations.CosmosDB` | `955ca0e` | SH-H032 |
| `Birko.Data.Migrations.MongoDB` | `9bfa718` | SH-H031 |
| `Birko.Data.Migrations.InfluxDB` | `d875843` | SH-H030 + SH-H033 |
| `*.Tests` (7 repos) | `a2a2aec` `3f91b72` `16a74a9` `7e49faf` `22ad2cf` `d43f207` `0d50233` | the 58 new tests |
