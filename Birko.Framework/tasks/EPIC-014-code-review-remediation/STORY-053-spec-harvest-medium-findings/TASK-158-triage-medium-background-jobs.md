---
id: TASK-158
parent: STORY-053
feature: FEATURE-014
# status: done | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M001, SH-M002, SH-M003, SH-M004, SH-M005, SH-M006, SH-M007, SH-M008, SH-M009, SH-M010, SH-M011, SH-M012, SH-M013, SH-M014, SH-M015, SH-M016, SH-M017, SH-M018, SH-M019, SH-M020, SH-M021]
pr: null
github-issue: null
jira-key: null
---

# Triage the 21 medium spec-harvest findings in `background-jobs`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **21** findings
that fall in the `background-jobs` area.

**Findings:** `SH-M001`–`SH-M021` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: background-jobs`, lines 448–575.

**The contract under review** is specced in [`docs/specs/background-jobs.md`](../../../docs/specs/background-jobs.md), harvested from
10 source globs — `../Birko.BackgroundJobs/**/*.cs`, `../Birko.BackgroundJobs.SQL/**/*.cs`, `../Birko.BackgroundJobs.ElasticSearch/**/*.cs`, and 7 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/background-jobs.md` lying until `/specs regen background-jobs` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 21 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen background-jobs`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 400 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-175]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A — fully covered by automated tests (offline and live on the Docker servers); a library with no UI.

## Resolution (2026-10-18)

A subagent first mapped all 21 findings against the current code: 19 still present, 2 partly fixed. Each verdict in
the findings doc names the traced code. **18 confirmed, 3 narrower (SH-M005, M011, M020). 12 fixed here; 9 spawned
before [[TASK-175]].**

**Two decisions by the user (2026-10-18):** `MaxRetries` becomes nullable (null = the policy, 0 = no retries;
[[TASK-662]]), and Redis drains by priority like the other backends ([[TASK-663]]).

**Fixed here:**
- **Loops** (M007–M010, core): outcomes written with `CancellationToken.None`; `JobOutcomeNotRecorded` and
  `EnqueueFailed` events, guarded against their subscribers and logged by the Hosting bridges; Stop() while waiting
  for a slot returns cleanly. 5 red on HEAD.
- **Serializer** (M004): JSON `null` metadata reads as empty. Red on HEAD.
- **In-process shared state:**
  - M021: JSON and XML queues on one file share one store and one gate. Red on HEAD, 2 + 2.
  - M011: InMemory transitions take the lock, and `ScheduledAt` is written before `Status`; fixed by inspection.
- **Claim and transition guards** (M012, M013) on SQL, MongoDB, ES and RavenDB: the claim also requires the attempt
  count read; Complete / Fail / Cancel are guarded native updates. Red on HEAD: 4 forced interleavings on SQLite. Mongo,
  ES and Raven take the same code (one script). ES passes its live suite.
  - **Corrected by TASK-662 (2026-10-18):** this said Mongo and Raven passed live too. Their job suites ran on
    in-memory doubles; live tests came with TASK-662, which found the MongoDB queue could not claim on a real server
    (a `$set` of a Guid threw in the store) and fixed it.
  - **Corrected by TASK-665 (2026-10-18):** on RavenDB the guarded update is not atomic. The store has no native
    filtered update, so the base runs it as a (possibly stale) index query and an unconditional write; a cancel racing
    a claim let both win. RavenDB now uses a compare-and-set (`UpdateIfAsync`).
  - **Corrected by TASK-667 (2026-10-18), a regression this task introduced:** on Elasticsearch the guarded
    transitions are `update_by_query` calls, which match the search index. A job completed or failed within a refresh
    (~1 s) of its claim was still indexed Pending, so the outcome was silently dropped, and Cancel cancelled a running
    job. Elasticsearch now uses a compare-and-set too, and `GetAsync` reads realtime.
  - **Contract change:** Complete / Fail of a non-Processing job is a no-op on every backend. InMemory, JSON and XML
    were aligned, and 4 tests that completed an unclaimed job now claim it first.
- **Lock providers:**
  - M006 (SQL × 3 dialects, Redis): one held name; another name throws, and releasing a name not held keeps the lock.
  - M019: SQL unlocks on dispose. Measured on MySQL: an idle pooled session kept `GET_LOCK` from another pool. In-process
    reuse hid it, because the driver resets on reuse.
  - M020: the Redis `IsLocked` ends at a local monotonic deadline.
  - Red on HEAD: 6 + 2 (M019) + 3 (Redis).

**Also caught by the Sandbox build:** `Random.Shared` inside a `Birko.*` namespace resolves to the `Birko.Random`
namespace where that project is imported, so the four queues use `System.Random.Shared`.

**Spawned:** [[TASK-661]] Cosmos conditional writes (M001, M002, M013 on Cosmos); [[TASK-662]] nullable `MaxRetries`
(M003); [[TASK-663]] atomic Redis transitions and priority (M014–M018); [[TASK-664]] stale-Processing reclaim on every
backend (M005).

**Flakes:**
- The SQL leader-election test `A_stopped_leader_releases_so_the_follower_takes_over` failed twice, each time at its
  first assertion (`a.IsLeader` false) on a run that included a build. Root cause in the test: it started both
  schedulers at once and assumed A won the first acquisition, which a cold run (JIT, the first pooled connection) can
  hand to B. Test-only fix: B starts once A leads. Since then 0/10 targeted and 2/2 cold full runs fail.
- ES `Racing_dequeuers_hand_each_job_out_once_and_never_throw` is a pre-existing flake: 1/5 on HEAD, 0/5 with this
  change.

**Regression, live:** all 10 BackgroundJobs suites green: core 105, Hosting 9, JSON 9, XML 9, SQL 50, Redis 31, Mongo
6, ES 8, Raven 7, Cosmos 9.
