---
id: TASK-184
parent: STORY-054
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-L038, SH-L039, SH-L040, SH-L041, SH-L042, SH-L043, SH-L044, SH-L045, SH-L046, SH-L047, SH-L048, SH-L049, SH-L050, SH-L051, SH-L052, SH-L053, SH-L054]
pr: null
github-issue: null
jira-key: null
---

# Triage the 17 low spec-harvest findings in `caching`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-054]] — which held all
387 low findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **17** findings
that fall in the `caching` area.

**Findings:** `SH-L038`–`SH-L054` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Low severity →
`### area: caching`, lines 3250–3353.

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

**Added by [[TASK-169]] (not a harvested finding):** SH-M048's fix bumps a per-key generation on removal only, so a
`HybridCache.GetAsync` that read the OLD value from L2 can still backfill L1 after a concurrent `SetAsync` wrote the new
value there (served stale for up to `L1DefaultExpiration`). Closing it means Set also writes L2, bumps, then L1, which
gives up the parallel write. Reproduce, then fix or waive with the measurement. SH-L049 (unobserved `l1Task` on
Remove / RemoveByPrefix / Clear) was likely changed by the same fix: those now always clear L1 after L2.

## Acceptance criteria

- [x] All 17 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
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
- [x] [[STORY-054]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 370 low findings — they belong to the other 21 per-area tasks under [[STORY-054]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Medium-severity findings in this area — [[TASK-169]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A — fully covered by automated tests: a library contract with no UI surface. The Redis tests run live.

## Implementation plan

## Outcome (2026-10-18)

**17 findings: 16 confirmed (L049 and L050 found fixed by [[TASK-169]]), 1 narrower (L047), 0 refuted.** Verdicts are in
the findings file. Decided by the user: a non-positive TTL is refused (L041); Redis stores a null as a hit (L045);
L047 is documented only.

- **MemoryCache:** the sweep removes any expired entry (L039), and expired entries are removed only if unchanged (L042).
  Operations after Dispose throw, and Dispose leaves held locks alone (L043, L044). Expiry runs on an optional
  `TimeProvider` with torn-free last-access ticks (L052).
- **Options and serializer:** `CacheEntryOptions.Validate` and the factories refuse a non-positive TTL; every backend
  validates its writes (L041). Priority is documented as carried only (L040). `CacheSerializer` round-trips null (L045);
  its doc no longer claims pluggability (L046).
- **Hybrid:** the backfill options are capped (L038), and HybridCache.cs no longer needs ImplicitUsings (L051). Dispose is
  guarded and once (L043, L044). TASK-169's handed-over race is closed: `SetAsync` bumps the key's generation after L2
  and writes L1 after the bump, so a read holding the old L2 value cannot leave it in L1.
- **Redis:** the stampede lock is fenced by a per-call token (L048), and Dispose is guarded and once (L043).
- **SQL store:** keys name the entity type (L053), and a disabled store still invalidates (L054).
- **Red check (HEAD production, new members stubbed):** Memory 11 of 13 (2 pins), Hybrid 5 of 7 (pins L049, L050),
  Redis live 3 (L043, L045, L048), SQL 2 of 2. L041 on Redis and Hybrid is a pin against removing their own call (the
  refusal also happens further in). L051 is by inspection.
- **Regression** (live Redis): Caching 57, Hybrid 59, Caching.Redis 47, SQL.Caching 37, Redis 12,
  BackgroundJobs.Redis 51, MessageQueue.Redis 64, all green. Sandbox builds.
