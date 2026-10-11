---
id: TASK-319
parent: STORY-051
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
picked-by: fix-next
priority: P1
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-H038]
pr: null
github-issue: null
jira-key: null
---

# Triage the 1 remaining high spec-harvest finding in `schema-index-and-ddl`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **1** open finding in the `schema-index-and-ddl` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H038` | ES reindex reports Success with Failures=0 when the response carries per-document failures | `Birko.Data.ElasticSearch/IndexManagement/ReindexHelper.cs:98` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: schema-index-and-ddl`, lines 276-290.

**The contract under review** is specced in [`docs/specs/schema-index-and-ddl.md`](../../../docs/specs/schema-index-and-ddl.md),
harvested from 37 source files — `../Birko.Data.ElasticSearch/IndexManagement/ElasticSearchIndexManagerAdapter.cs`, `../Birko.Data.ElasticSearch/IndexManagement/IndexInfo.cs`, `../Birko.Data.ElasticSearch/IndexManagement/IndexManager.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Latent.** `ReindexHelper`: **0** consumer `.cs` files; `Birko.Data.ElasticSearch` is imported by 2 aggregators without using it.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** reporting success over per-document failures is a silent wrong answer, but confined to the ElasticSearch reindex helper.

⚠ **Finding-id hygiene.** [[TASK-197]] records that a working tree once cited `SH-H038` for an
unrelated field-mapping defect, and corrected it. `SH-H038` is the ElasticSearch reindex finding and
nothing else — do not inherit that mis-citation.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/schema-index-and-ddl.md` lying until `/specs regen schema-index-and-ddl` runs, and **that spec
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
- [x] Any behavioural fix is followed by `/specs regen schema-index-and-ddl`, with the spec diff reviewed as the
      change's evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a
      ticked box with the work undone, and never as an `## Out of scope` sentence describing work
- [x] [[STORY-051]]'s **Progress** line and its task table reflect this area's closed count

## Out of scope

- `ReindexHelper.Reindex(…, waitForCompletion: false)` reports a merely started reindex `Successful` — deferred to [[TASK-560]].
- The by-query responses (`UpdateByQuery` / `DeleteByQuery`) discarded by the stores — [[TASK-502]] owns them; the `timed_out` measurement was added there.
- The other 38 open high findings — they belong to the other 14 per-area tasks under
  [[STORY-051]].
- The 18 high findings already covered by [[STORY-051]]'s existing 31 task files. If triage shows one of
  those fixes did **not** hold, that is a regression: file it fresh and say so, per `/tasks intake`
  § *Re-running a pass*.
- Medium-severity findings in this area — [[TASK-154]] owns those.
- Low-severity findings in this area — [[TASK-192]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

N/A — fully covered by automated tests: a fake Elasticsearch transport replays the exact responses in question
(HTTP 200 with `timed_out: true`, with a listed failure, and complete) and the tests assert what the helper did to
the indices — which were deleted, whether the alias moved — not only the result object. No UI surface.

## Outcome

**What was broken.** An Elasticsearch reindex that stops early — it ran out of time, or someone cancelled the task —
still answers HTTP 200, with no failures listed and only part of the documents copied. `ReindexHelper` reported that
as a success, so its zero-downtime path (`ReindexWithAlias`) pointed the alias at the half-filled index and, with
`deleteOldIndex: true`, deleted the only complete copy. The ES data migrator's `CopyData` had the same blind spot and
would record the migration as applied.

**The fix.** One public check, `ReindexHelper.IncompleteReason`: the per-outcome counts (`created + updated + noops +
version_conflicts`) must reach `total`, and a `timed_out` flag fails the copy even when they do. `Refusal` uses it in
all four reindex variants; `ElasticSearchDataMigrator.CopyData` calls the same method (it already depends on
`Birko.Data.ElasticSearch`), so there is one producer.

**The finding was both narrower and wider than filed.** Narrower: it also claimed per-document `failures` in an HTTP
200 were reported as success; measured against the original code, NEST's `IsValid` already includes them. I first
wrote an explicit failures check, saw it go unexercised under mutation, and removed it (rule 53); those cases stay as
**contract pins**. Wider: the migrator had the defect too, and the merge-gate review found the **cancelled** task —
`canceled` is not even surfaced by NEST 7 — which a `timed_out`-only check would still have swapped over. Checking
completeness by the counts closes both, and any other way to stop early that leaves the counts short.

**Step-6 split.** `ReindexFailureReportingTests` (14): **7/14** red against the original code and again with
`IncompleteReason` always passing — fix-dependent: `Reindex_that_timed_out_is_a_failure`,
`The_other_three_variants_report_a_timeout_as_a_failure`, `ReindexWithAlias_that_timed_out_keeps_the_source_and_the_alias`,
`ReindexWithAliasAsync_that_timed_out_keeps_the_source_and_the_alias`, `Reindex_that_was_cancelled_is_a_failure`,
`ReindexWithAliasAsync_that_was_cancelled_keeps_the_source_and_the_alias`,
`Reindex_flagged_timed_out_is_a_failure_even_when_the_counts_look_complete`; **1/14** red with only the `timed_out`
term removed (that last test — written because no other test exercised the flag). Contract pins (pass either way):
the four per-document-failure tests, the two alias-with-failures tests, and
`ReindexWithAlias_that_copied_everything_swaps_and_deletes`. `CopyDataTimeoutTests` (3): **2/3** red against the
original code and with the check removed (`A_timed_out_copy_fails_the_step`, `A_cancelled_copy_fails_the_step`); the
success case is a pin. Suites: ElasticSearch 181/181, Migrations.ElasticSearch 26/26; solution 0 errors.

**Judgement calls.**
- **Completeness by counts, not a list of stop reasons.** The review proposed it after finding `canceled`; naming each
  way to stop (`timed_out`, then `canceled`, then whatever ES adds) is the pattern that missed the second one.
- **`timed_out` is trusted over the counts.** If the server says a request timed out, counts that add up are not
  proof the copy is whole. Pinned by its own test, since every other timed-out fixture is also short.
- **No `deleted` count in NEST 7**, so a script that deletes documents (`ctx.op = "delete"`) is refused as
  incomplete. Rejected: ignoring the counts when a script is set — that reopens the silent swap for every scripted
  reindex. A loud false alarm on a rare script beats a silent delete of the source.
- **A partial copy is a failure, not a success with a warning**: the one caller that acts on the result deletes the
  source. `DocumentsProcessed` still reports what was copied.
- **The test transport keeps state** (which indices exist) so the alias tests prove the source survived and the
  alias never moved; and both transports now build responses through NEST's real sync **and async** deserializers
  (the review caught that the async path had been routed through the sync builder; the cause was a missing content
  type, not the async builder).

**Flagged, not fixed.**
- [[TASK-560]] — `waitForCompletion: false` returns `Successful` for a reindex that has only started (no counts, no
  task id kept). Not destructive today: the alias path always waits.
- [[TASK-502]] (existing) owns the stores' discarded `UpdateByQuery` / `DeleteByQuery` responses; it now carries the
  measurement about `IsValid` and `timed_out`.
- `DocumentsProcessed` counts only `created`, not `updated` — pre-existing, cosmetic, left as is (noted by the review).

## Progress log

- step 2 — picked; ranked above [[TASK-317]] (SH-H001, Cosmos named-queue dequeue never matches null-QueueName jobs) because SH-H038 is silent *destructive* loss — a reindex that skipped documents reports `Successful`, `ReindexWithAlias` swaps the alias to the incomplete index and with `deleteOldIndex: true` deletes the source, so the skipped documents are gone — whereas SH-H001's jobs stay persisted in Pending (recoverable), as do SH-H020's Redis messages ([[TASK-318]]). All three are latent (0 consumer references); the verified mediums ([[TASK-324]], [[TASK-325]]) are resource/cancellation defects, lower on key 1.
- step 3 — verified on first reading as written, in all four variants (narrowed by measurement in step 3b). `ReindexHelper.Reindex` / `ReindexAsync` / `ReindexWithScript` / `ReindexWithScriptAsync` (`Birko.Data.ElasticSearch/IndexManagement/ReindexHelper.cs:61-71`, `:98-108`, `:140-150`, `:179-189`) read `response.Failures` only inside `if (!response.IsValid)`; an HTTP 200 listing per-document failures (version conflicts, mapping rejections) or `timed_out: true` falls through to `ReindexResult.Successful(…, response.Created, …)`. `ReindexWithAlias` / `ReindexWithAliasAsync` (:242-256, :300-313) branch only on `Success`, so they swap the alias to the incomplete index and, with `deleteOldIndex: true`, delete the source. Reach: 0 consumer `.cs` references (latent).
- step 4 — layer: local (framework code reading NEST's response; NEST reports the failures correctly)
- step 3b — rescoped after measuring (recorded here rather than silently): run against the original code, the per-document-failure half of the claim never reproduced — NEST's `IsValid` already includes `failures` — so the finding is confirmed **narrower** (timed-out only) and **wider by one site** (`ElasticSearchDataMigrator.CopyData`). Acceptance criteria unchanged: they already ask for confirmed / narrower / refuted.
- step 5 — fix in `Birko.Data.ElasticSearch/IndexManagement/ReindexHelper.cs` (public `IncompleteReason` + `Refusal`, used by the four variants) and `Birko.Data.Migrations.ElasticSearch/Context/ElasticSearchDataMigrator.cs` (`CopyData` calls `IncompleteReason`); tests `tests/Birko.Data.ElasticSearch.Tests/IndexManagement/ReindexFailureReportingTests.cs` (14) and `tests/Birko.Data.Migrations.ElasticSearch.Tests/CopyDataTimeoutTests.cs` (3); suites ElasticSearch 181/181, Migrations.ElasticSearch 26/26; solution 0 errors
- step 6 — original code: 7/14 + 2/3 failed; `IncompleteReason` always null: 7/14 + 2/3; only the `timed_out` term removed: 1/14; fix-dependent = 9 (named in Outcome); contract pins = 8 (named in Outcome); an explicit failures branch was written, never went red under mutation, and was removed (rule 53)
- step 6b — merge-gate review (separate reviewer) found the cancelled-task route to the same data loss; the fix was widened from a `timed_out` check to a completeness check by counts, re-tested and re-mutated (numbers above are the final ones); it also corrected the async test transport and the comments
- step 7 — respecced by hand: `schema-index-and-ddl` (+ "Per-document failures in an HTTP 200 are a failed result" [pin], "A reindex that timed out is a failed result", "A partial copy never moves the alias or deletes the source") and `migrations` (CopyData fails a timed-out reindex)
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log

## Implementation plan

_Populated by `/tasks plan TASK-319` — leave empty until then._
