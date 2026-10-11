---
id: TASK-325
parent: STORY-053
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
findings: [SH-M426, SH-M427, SH-M428]
pr: null
github-issue: null
jira-key: null
---

# Fix the 3 verified medium findings in `unit-of-work-and-transactions`

## Context

Filed by [[TASK-195]] on 2026-09-08, which rated, ID'd and folded in the 16 findings recovered from the
harvest's lost first-schema pass. `unit-of-work-and-transactions` is one of the **three areas that had no per-area task at all**,
because the 44 tasks under [[STORY-053]]/[[STORY-054]] cover only the 22 areas that had rated findings —
and these three are precisely the ones that did not. Folding ids in without creating this task would have
left them exactly as they were: filed, and scheduled by nothing.

⚠ **These are not triage tasks, and that is the one way they differ from their 44 siblings.** Every
finding below was **hand-verified against the code on 2026-09-08** as part of the rating pass, so the
verdict is already in the findings doc. There is no confirm-or-refute step: the work is the fix.

| Finding | Verdict | Claim |
|---|---|---|
| `SH-M426` | CONFIRMED-NARROWER (downgraded from high) | An ElasticSearch commit failure leaves the buffer queued and the UoW active, so there is no way to retry only the failed subset |
| `SH-M427` | CONFIRMED | `SqlUnitOfWork` leaks the `DbConnection` when `OpenAsync` or `BeginTransactionAsync` throws |
| `SH-M428` | CONFIRMED | A failed SQL `CommitAsync`/`RollbackAsync` skips `CleanupAsync`, holding the connection open |

Detail — including each verdict and what was measured — in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: unit-of-work-and-transactions`. The recovered originals are in
[`STORY-055/RECOVERED-FINDINGS.md`](../STORY-055-spec-harvest-unrated-areas/RECOVERED-FINDINGS.md).

**The contract under review** is specced in [`docs/specs/unit-of-work-and-transactions.md`](../../../docs/specs/unit-of-work-and-transactions.md). Its
sources point into **sibling repos**, so a fix here normally lands as three commits in three repos
(production, regression suite, this file) per CLAUDE.md § Integration model.

**Why P1:** `SqlUnitOfWork` is named in **3** consumer `.cs` files, so `SH-M427`/`SH-M428` are live, and `SH-M427` leaks a **pooled** connection on every retried failed `BeginAsync` — a resource defect that compounds under exactly the conditions that cause it. `SH-M426`'s type has 0 consumer references and rides along because it is the same shape.

⚠ **`SH-M428`'s reachability is measured, not hypothetical, and that is new since it was filed.** It names a deadlock-victim commit as its trigger; [[TASK-306]] reproduced precisely that on live SQL Server 2022 — error **1205**, *"Rerun the transaction"*, 1 run in 12 under CPU load. So the condition that skips `CleanupAsync` is one this tree has now observed.

**All three are the same shape — cleanup that is a sequential statement where it should be a `finally`** — so they are one edit per file, not three investigations. `SH-M427` needs try/catch around the open/begin pair; `SH-M428` needs try/finally around commit and rollback; `SH-M426` needs the same for the ES buffer, or per-item outcomes exposed.

⚠ **Do not use [[TASK-305]] as the answer to `SH-M428`.** A retry policy would re-run the commit; it would not release the connection a failed commit is still holding. They are adjacent, not substitutes.

**Ordering constraint — the spec documents these defects as shipped behaviour.** The harvest specced what
the code *does*, defects included. So a behavioural fix leaves `docs/specs/unit-of-work-and-transactions.md` lying until
`/specs regen unit-of-work-and-transactions` runs, and **that spec diff is the fix's evidence**.

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
- [x] `/specs regen unit-of-work-and-transactions` run after any behavioural fix, with the spec diff reviewed as the evidence
- [x] Anything too large for this task is spawned via `/tasks spawn` — never left as a ticked box with the
      work undone, and never as an `## Out of scope` sentence describing work
- [x] [[STORY-053]]'s task table and `finding-count` reflect this area's closed count

## Out of scope

- **Re-verifying the findings.** Done on 2026-09-08; the verdicts are in the findings doc. If the code has
  moved since and a verdict no longer holds, that is a regression — say so and re-measure rather than
  silently fixing something else.
- The other severity tier of this same area — its low findings are [[TASK-327]].
- **The five recovered findings that were duplicates.** `SLI-1`→`SH-M307`, `SLI-2`/`SLI-3`→`SH-M316`
  ([[TASK-163]]), `SLI-4`→`SH-L297`, `SLI-6`→`SH-L298` ([[TASK-182]]). They are cross-referenced in the
  findings doc, not re-filed here.
- **Test gaps.** Test coverage was out of scope for the harvest sweep, so a missing test is not a finding
  here — only a test a fix needs.

## Human test plan

N/A — library code with no UI surface; fully covered by automated tests (`SqlUnitOfWorkFailureReleaseTests`,
`ElasticSearchUnitOfWorkCommitFailureTests`).

## Implementation plan

_Populated by `/tasks plan TASK-325` — leave empty until then._

## Outcome

**All 3 fixed — one shape, cleanup that was a sequential statement where it had to run on failure.**
`SqlUnitOfWork` now releases its connection when open, begin, commit or rollback throws, and rethrows the provider's
exception unchanged. `ElasticSearchUnitOfWork` spends its buffer when it sends it, and a partial failure throws
`ElasticSearchBulkCommitException` naming every failed item. In both, the first `RollbackAsync` after a failed commit
is a no-op, so the idiomatic catch-and-roll-back cannot replace the real error (the deadlock) with a
`NoActiveTransactionException`.

- **Step-6 split (fake `DbConnection`/`DbTransaction`; canned NEST bulk responses):**
  - SQL, whole fix reverted: **6/10** red — `A_failed_open_releases_the_connection`,
    `A_failed_begin_releases_the_opened_connection`, `Retried_failed_begins_leak_no_connection`,
    `A_unit_of_work_can_begin_again_after_a_failed_begin`, `A_failed_commit_releases_the_transaction_and_the_connection`,
    `A_failed_rollback_releases_the_connection`.
  - One guard at a time: begin catch → 4/10 (the four begin tests); commit catch → 1/10; rollback catch → 1/10;
    no-op rollback → 2/10 (`Catch_and_roll_back_after_a_failed_commit_keeps_the_commits_exception`,
    `The_no_op_rollback_is_spent_once`). Those two **pass on the original code** (it left the transaction open, so
    the rollback had something to roll back): they pin the no-op that the release made necessary, not the original
    defect.
  - **Contract pins (pass either way):** `Rollback_without_a_transaction_still_throws`,
    `A_successful_commit_releases_the_connection`.
  - ES, original Commit/Rollback (new types kept so the file compiles): **3/5** red —
    `A_partially_failed_commit_names_each_failed_item` (original threw `UnitOfWorkException: … failed: Unknown error`),
    `A_failed_commit_ends_the_unit_of_work_and_drops_the_sent_buffer`,
    `Catch_and_roll_back_after_a_failed_commit_keeps_the_commits_exception`. Buffer kept → 1/5; no-op rollback
    removed → 1/5. **Pins:** `A_successful_commit_ends_the_unit_of_work`, `Rollback_without_a_unit_of_work_still_throws`.
  - Full suites: SQL 710/710, ElasticSearch 186/186, SQLite 440/440.
- **Judgement calls.**
  - *Rollback after a failed commit is a no-op, once.* Rejected: making `RollbackAsync` on an inactive UoW always
    silent, which would hide a genuine double-rollback or a rollback with no begin — those still throw (pinned).
    Rejected too: leaving the UoW active after a failed commit so the caller's rollback releases it. That is the
    original defect for every caller that does not roll back.
  - *Release, don't explicitly roll back, on a failed commit:* disposing an uncommitted `DbTransaction` rolls it back,
    and an explicit `RollbackAsync` on a deadlock-victim transaction is itself an error on SQL Server. A failure while
    releasing is swallowed so it cannot replace the exception the caller needs.
  - *ES: clear before send.* If the request itself throws (transport), the buffer is gone too. Rejected: keeping it
    for that case, because a transport failure can arrive after the server applied the batch — re-sending blindly is
    the defect.
  - *NEST fixture realism:* NEST 7's item `IsValid` requires `_type` (real ES 7 responses carry `"_doc"`) and treats a
    404 `delete` as valid. Fixtures follow the real shape; noted in the test file.
- **Not changed, recorded:** `RavenDbUnitOfWork` has the same "failed commit leaves it active" shape for
  `SaveChangesAsync`, and Mongo never ends its session on commit failure. Not in this task's findings, and both hold
  no pooled connection (a session, disposed on `Dispose`), so no leak — but the catch-and-roll-back semantics now
  differ across the family. Recorded here rather than spawned: no defect is claimed until someone measures one.
- **Merge gate:** inline review of the diff. Public-surface change is additive (`BulkItemFailure`,
  `ElasticSearchBulkCommitException`); `ElasticSearchUnitOfWork` has 0 consumer references (TASK-325 Context), and
  `SqlUnitOfWork`'s signatures are unchanged. No nullable warnings; solution builds 0 errors.

## Progress log

- step 2 — picked; ranked above TASK-324 (store-lazy-initialization, 1 verified medium) because SH-M427/428 are live in 3 consumer files and leak a pooled connection under a measured trigger (TASK-306 deadlock 1205); TASK-555 (higher on severity: silent wrong success) excluded from the unattended pool — its acceptance opens with a cross-provider contract decision
- step 3 — verified: all three hold as recorded. `SqlUnitOfWork.BeginAsync` assigns `_connection` then awaits `OpenAsync` / `BeginTransactionAsync` with no try (`Birko.Data.SQL/UnitOfWork/SqlUnitOfWork.cs:107-110`); `CommitAsync` / `RollbackAsync` are `await …; await CleanupAsync();` sequential pairs (:140-141, :159-160). `ElasticSearchUnitOfWork.CommitAsync` clears `_context` only after both throw sites (`Birko.Data.ElasticSearch/UnitOfWork/ElasticSearchUnitOfWork.cs:140-158`). Design point found while verifying: a cleanup-in-finally alone would make the idiomatic `catch { await uow.RollbackAsync(); throw; }` raise `NoActiveTransactionException` inside the catch and **replace** the commit's exception (the deadlock 1205 the caller needs). Family today: Cosmos already ends the UoW on a failed commit (rollback then throws NoActive); Raven, like SQL, leaves it active
- step 4 — layer: local (Birko.Data.SQL, Birko.Data.ElasticSearch)
- step 5 — fix in `Birko.Data.SQL/UnitOfWork/SqlUnitOfWork.cs` (begin/commit/rollback release on failure, `ReleaseAfterFailureAsync`, one-shot no-op rollback) and `Birko.Data.ElasticSearch/UnitOfWork/ElasticSearchUnitOfWork.cs` (buffer spent on send, `ElasticSearchBulkCommitException` + `BulkItemFailure`, one-shot no-op rollback); tests in `tests/Birko.Data.SQL.Tests/UnitOfWork/SqlUnitOfWorkFailureReleaseTests.cs` (10) and `tests/Birko.Data.ElasticSearch.Tests/UnitOfWork/ElasticSearchUnitOfWorkCommitFailureTests.cs` (5); SQL 710/710, ES 186/186, SQLite 440/440
- step 6 — SQL whole revert 6/10 red; per guard 4/1/1/2; ES original 3/5 red, per guard 1/1; pins named in Outcome; original ES partial failure measured as "failed: Unknown error"
- step 7 — respecced unit-of-work-and-transactions; requirements rewritten: *A failed SQL commit or rollback releases the connection*, *A failed SQL begin releases the created connection*, *Elasticsearch commit failure is reported as UnitOfWorkException* (bulk exception), *A failed Elasticsearch commit ends the unit of work*; amended: *Commit and rollback require an active transaction* (one-shot exception), *Elasticsearch commit flushes one bulk request* (clear before send)
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
