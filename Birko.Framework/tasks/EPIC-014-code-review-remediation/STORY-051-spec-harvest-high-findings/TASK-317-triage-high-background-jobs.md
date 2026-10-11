---
id: TASK-317
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
findings: [SH-H001]
pr: null
github-issue: null
jira-key: null
---

# Triage the 1 remaining high spec-harvest finding in `background-jobs`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **1** open finding in the `background-jobs` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H001` | CosmosDB named-queue dequeue never matches null-QueueName jobs, so no dispatcher-enqueued job ever runs | `Birko.BackgroundJobs.CosmosDB/CosmosDBJobQueue.cs:65` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: background-jobs`, lines 32-38.

**The contract under review** is specced in [`docs/specs/background-jobs.md`](../../../docs/specs/background-jobs.md),
harvested from 42 source files — `../Birko.BackgroundJobs.CosmosDB/CosmosDBJobQueue.cs`, `../Birko.BackgroundJobs.CosmosDB/CosmosDBJobQueueSchema.cs`, `../Birko.BackgroundJobs.CosmosDB/Models/CosmosJobDescriptorModel.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Latent.** `CosmosDBJobQueue` / `Birko.BackgroundJobs.CosmosDB`: referenced only by the Sandbox aggregator. No consumer selects the CosmosDB backend.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** claims total silent loss of dispatcher-enqueued work, which is severe — but on **CosmosDB only**, and no consumer selects that backend (see below).

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/background-jobs.md` lying until `/specs regen background-jobs` runs, and **that spec
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
- [x] Any behavioural fix is followed by `/specs regen background-jobs`, with the spec diff reviewed as the
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
- Medium-severity findings in this area — [[TASK-158]] owns those.
- Low-severity findings in this area — [[TASK-175]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

N/A — library code with no UI surface; fully covered by automated tests (`QueueNameDequeueTests`).

## Implementation plan

_Populated by `/tasks plan TASK-317` — leave empty until then._

## Outcome

**Fixed, confirmed as filed.** On Cosmos DB a background job enqueued without a queue name — which is every job
`JobDispatcher.EnqueueAsync` / `ScheduleAsync` / `EnqueueWithPriorityAsync` creates — was never dequeued by the
processor, because the Cosmos predicate matched only `j.QueueName == queueName` and the processor always asks for
`"default"`. It now also admits `j.QueueName == null`, the contract every other backend implements.

- **Step-6 split:** with the new term removed, **3/9** of `Birko.BackgroundJobs.CosmosDB.Tests` fail — the fix-dependent
  `A_dispatcher_enqueued_job_is_dequeued_by_the_processors_default_queue`, `An_unnamed_job_is_dequeued_by_any_named_queue`,
  `A_due_scheduled_unnamed_job_is_dequeued_by_a_named_queue`. **Contract pins, not evidence** (pass either way):
  `A_named_queue_does_not_take_another_queues_job`, `A_named_queue_takes_its_own_job`, `An_unnamed_dequeue_takes_any_job`,
  plus the 3 pre-existing `CosmosDBJobQueueTests`.
- **Judgement call — what the test double proves.** `InMemoryCosmosStore` compiles the filter in memory, so the tests pin
  the predicate's *meaning*, not Cosmos SQL's evaluation of it. That `= null` matches the stored documents rests on
  `CosmosGuidIdSerializer` (default `JsonSerializerOptions`) writing `"QueueName": null` explicitly — the same reliance the
  existing `ScheduledAt != null` guard already makes. Rejected: a live-emulator test, which no CI lane runs for this
  project; recorded here instead so a serializer change that drops nulls (`DefaultIgnoreCondition.WhenWritingNull`) is
  known to break this (an `IS_DEFINED`-aware predicate would then be needed).
- **Merge gate:** inline review of the diff (one predicate term + comment, one new test file, spec, docs). No convention
  hit: the change is a read filter, not a write scope, so rules 1-5 do not apply; no nullable warnings. No security
  surface.
- **Flagged, not fixed:** [[TASK-561]] (FIELD-028, P2) — Redis serves an unnamed job only to a queue literally named
  `"default"`, so a processor with any other `DefaultQueueName` never runs dispatcher jobs there. Also already specced and
  filed elsewhere, not reopened here: the Cosmos read-then-write dequeue race and the unguarded `CancelAsync` (spec §
  *Cosmos DB has a read-then-write race*, § *Cancellation is status-guarded everywhere except Cosmos DB*; SH-M001 / SH-M002,
  [[TASK-158]]).
- **Deploy note (CHANGELOG):** stranded `Pending` documents on an existing Cosmos container start running on first poll.

## Progress log

- step 2 — picked; ranked above TASK-318 (SH-H020, Redis non-group reads from "$") because both are silent loss on a latent backend, but SH-H001 claims *every* dispatcher-enqueued job never runs (total) while SH-H020 loses only messages written before a reader starts; key 1 (severity) broke the tie
- step 3 — verified: held as filed. `CosmosDBJobQueue.DequeueAsync` (`CosmosDBJobQueue.cs:65`) filters `queueName == null || j.QueueName == queueName`; every sibling (InMemory `InMemoryJobQueue.cs:48`, SQL/Mongo/ES/Raven/JSON/XML `if (queueName != null)` branch) admits `j.QueueName == null` on a named dequeue. `BackgroundJobProcessor.cs:48` always passes `DefaultQueueName` ("default"); `JobDispatcher` Enqueue/Schedule/EnqueueWithPriority leave `QueueName` null — so on Cosmos only `EnqueueOnAsync` jobs ever run. The Cosmos serializer (`CosmosGuidIdSerializer`, default `JsonSerializerOptions`) writes `"QueueName": null` explicitly, so `= null` matches the stored documents (same mechanism the existing `ScheduledAt != null` guard relies on)
- step 4 — layer: local (one predicate in Birko.BackgroundJobs.CosmosDB; the siblings already implement the contract)
- step 5 — fix in `Birko.BackgroundJobs.CosmosDB/CosmosDBJobQueue.cs` (one predicate term); tests in `tests/Birko.BackgroundJobs.CosmosDB.Tests/QueueNameDequeueTests.cs` (6); suite 9/9 green
- step 6 — reverted fix: 3/9 failed; fix-dependent = the three unnamed-job dequeue tests; contract pins = the three named/unnamed-dequeue pins + 3 pre-existing tests
- step 7 — respecced background-jobs; requirement *Queue-name scoping on dequeue* text (Cosmos joins the admitting backends) and scenario *Cosmos DB hides a null-queue job* replaced by *Cosmos DB serves a dispatcher-enqueued job to the processor's default queue*; spawned TASK-561 from the Redis scenario
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
