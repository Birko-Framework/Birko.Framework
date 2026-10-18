---
id: TASK-169
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M035, SH-M036, SH-M037, SH-M038, SH-M039, SH-M040, SH-M041, SH-M042, SH-M043, SH-M044, SH-M045, SH-M046, SH-M047, SH-M048]
pr: null
github-issue: null
jira-key: null
---

# Triage the 14 medium spec-harvest findings in `caching`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **14** findings
that fall in the `caching` area.

**Findings:** `SH-M035`–`SH-M048` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: caching`, lines 660–745.

**The contract under review** is specced in [`docs/specs/caching.md`](../../../docs/specs/caching.md), harvested from
4 source globs — `../Birko.Caching/**/*.cs`, `../Birko.Caching.Hybrid/**/*.cs`, `../Birko.Caching.Redis/**/*.cs`, and 1 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/caching.md` lying until `/specs regen caching` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 14 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen caching`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 407 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-184]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A — fully covered by automated tests: a library contract with no UI surface. The Redis tests run live.

## Implementation plan

## Outcome (2026-10-18)

**14 findings: 13 confirmed, 1 found fixed (SH-M041, by SH-H006's `EscapeGlob`), 0 refuted.** All 13 fixed here except
SH-M044's after-commit half, spawned as [[TASK-669]]. Verdicts are in the findings file.

- **Cache backends.** HybridCache lets the caller's cancellation through (M037). Its removal goes L2, then a generation
  bump, then L1, so a concurrent read cannot backfill a removed key (M048). MemoryCache: a cancelled lock wait releases
  its reference (M038), and a stored null hits only for a compatible type (M039). Redis drops a stale `__meta` (M040),
  scans and flushes the database it writes to (M042), and works across every primary through the new
  `RedisConnectionManager.GetServers()` (M043; multi-node unmeasured, no cluster available).
- **SQL cached store.** It caches a JSON copy, never a shared instance (M047), and a not-found row is cached as `null` on
  every backend (M035). Single and collection reads have distinct keys (`BuildSingleKey`, M036). Reads inside a
  transaction bypass the cache (M044, read half). Every write invalidates with no token, and also when it threw (M045),
  and so does `DestroyAsync` (M046).
- **Red check (HEAD production, new members stubbed: `BuildSingleKey` as the old key, `GetServers` as one server):**
  red: SQL store 8/8, Memory 3, Hybrid 9 (M037 6, M048 3), Redis live 2 (M040, M042). Pins: Hybrid GetOrSet cancellation, driver timeout still
  absorbed, normal backfill; Memory CR-L036 stored-null hit; Redis M041 glob and M043 server list.
- **Regression** (live Redis): Caching 44, Hybrid 51, Redis cache 43, Birko.Redis 12, BackgroundJobs.Redis 51,
  MessageQueue.Redis 64, SQL.Caching 35, all green. Sandbox builds.
- **Handed to [[TASK-184]]:** the same backfill race against a concurrent `SetAsync` (not a harvested finding).
- **Docs:** `docs/caching.md` § SQL Query Caching described a constructor and options that do not exist; rewritten.
