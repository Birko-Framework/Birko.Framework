---
id: TASK-174
parent: STORY-054
feature: FEATURE-014
# status: done | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-L102, SH-L103, SH-L104, SH-L105, SH-L106, SH-L107, SH-L108, SH-L109, SH-L110, SH-L111, SH-L112, SH-L113, SH-L114, SH-L115, SH-L116, SH-L117, SH-L118, SH-L119, SH-L120, SH-L121, SH-L122, SH-L123, SH-L124, SH-L125, SH-L126, SH-L127, SH-L128, SH-L129, SH-L130]
pr: null
github-issue: null
jira-key: null
---

# Triage the 29 low spec-harvest findings in `event-bus-and-messaging`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-054]] — which held all
387 low findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **29** findings
that fall in the `event-bus-and-messaging` area.

**Findings:** `SH-L102`–`SH-L130` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Low severity →
`### area: event-bus-and-messaging`, lines 3642–3817.

**The contract under review** is specced in [`docs/specs/event-bus-and-messaging.md`](../../../docs/specs/event-bus-and-messaging.md), harvested from
8 source globs — `../Birko.EventBus/**/*.cs`, `../Birko.EventBus.Outbox/**/*.cs`, `../Birko.EventBus.MessageQueue/**/*.cs`, and 5 more. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/event-bus-and-messaging.md` lying until `/specs regen event-bus-and-messaging` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 29 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen event-bus-and-messaging`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-054]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 358 low findings — they belong to the other 21 per-area tasks under [[STORY-054]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Medium-severity findings in this area — [[TASK-155]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A — fully covered by automated tests: a library contract with no UI surface; Redis exercised live.

## Implementation plan

_Populated by `/tasks plan TASK-174` — leave empty until then._

## Outcome (2026-10-18)

**29 findings: 23 confirmed, 6 narrower (SH-L111, L121, L128, L130, and L104 / L115 with no behaviour test), 0
refuted.**
- **Decided by the user:** unused options are refused until implemented (`TransportSupport`), and [[TASK-668]] is
  spawned to implement them: TimeToLive (MQTT, in-memory), Priority, PrefetchCount, the bus's
  RetryPolicy / DeadLetterOptions. ReplyTo / GroupId are kept as carried headers (L111).
- **Waived:** L121 (both outbox stores agree; a claimed entry cannot vanish). **Documented:** L128.
- **Transport side (this session):**
  - Redis: L102 public PollError, L103 entry-id time, L104 no false async.
  - MQTT: L105 per-handler copies, L106 / L107 settings.
  - Refusals: L108–L110, L114. Bus: L112 AutoSubscribe at start, L113 delivery counts.
  - L115 / L116 concurrency guards; L125 envelope; L130 token-source ownership.
- **Bus / outbox side (a fork, reviewed here):**
  - InProcessEventBus: L117 OCE filter, L118 subscribe / dispose lock.
  - Outbox: L119 cached MethodInfo, L120 Subscribe guard, L122 locks, L123 clock.
  - Core: L124 ICorrelatedEvent, L125 initialisers, L126 guard, L127 sweep, L129 cache.
- **Red on HEAD:** 24 tests, 16 from the transport side and 8 from the fork. The fork stubbed L123's clock parameters
  as ignored.
  - Pins that pass either way: L115 concurrent dispose (the race did not reproduce), L119 / L122 and L129 (behaviour
    unchanged).
  - The first L105 test depended on dispatch order and passed on HEAD; it was rewritten to be order-independent and
    is red on HEAD.
- **Inverted / adjusted tests:**
  - CR-L258's "AutoSubscribe=false registers no hosted service" (now: registered, flag read at start).
  - Two Redis producer round-trip tests that set a Priority (now 0).
- **Test-only:** EventBus.MessageQueue tests import the in-memory transport.

**Regression, live:** all 11 test projects importing a changed project green; Sandbox builds.
