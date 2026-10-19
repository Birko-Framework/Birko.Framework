---
id: TASK-155
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
findings: [SH-M099, SH-M100, SH-M101, SH-M102, SH-M103, SH-M104, SH-M105, SH-M106, SH-M107, SH-M108, SH-M109, SH-M110, SH-M111, SH-M112, SH-M113, SH-M114, SH-M115, SH-M116, SH-M117, SH-M118, SH-M119, SH-M120, SH-M121, SH-M122]
pr: null
github-issue: null
jira-key: null
---

# Triage the 24 medium spec-harvest findings in `event-bus-and-messaging`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **24** findings
that fall in the `event-bus-and-messaging` area.

**Findings:** `SH-M099`–`SH-M122` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: event-bus-and-messaging`, lines 1052–1197.

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

- [x] All 24 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
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
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- **`SH-M120`** — moved out on 2026-10-11 as a live P1 in a consumer (Symbio TASK-905): confirmed and fixed by
  [[TASK-565]]; its distributed-bus twin is [[TASK-566]]. 23 findings remain here.

- The other 397 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-174]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A — fully covered by automated tests: a library contract with no UI surface; Redis exercised live, MQTT over a
mocked client (no broker container).

## Implementation plan

_Populated by `/tasks plan TASK-155` — leave empty until then._

## Outcome (2026-10-18)

**24 findings: 18 confirmed, 4 narrower (SH-M100, SH-M104, SH-M109, SH-M122), 0 refuted; SH-M099 found fixed by
SH-H020, SH-M115 by TASK-566, SH-M120 by TASK-565 (verdict already written); SH-M104's growth half waived.**

Decided by the user (2026-10-18), each changing what consumers see:
- SH-M102: a new Redis group honours `FromBeginning`.
- SH-M107 / SH-M113: refuse loudly.
- SH-M116: retry an unknown type, report a corrupt message.
- SH-M118: release the dedup mark on failure.

- **Redis consumer:**
  - `OnHandlerError` hook (M100); reclaim on a busy stream (M101); group at `$` unless FromBeginning (M102).
  - Setup retried on `RedisException` (M103); every entry id of a repeated message acked (M104); producer refuses
    `Delay` (M113).
- **MQTT:**
  - Handlers per filter and subscription, released by their subscription; broker UNSUBSCRIBE with the last one
    (M105 / M106).
  - Unsupported options and requeue refused (M107); producer refuses `Delay` (M113).
  - Connection-event subscribers isolated, so reconnect always starts (M108).
- **In-memory:**
  - `OnHandlerError` (M109); failed ManualAck stays pending (M110).
  - Dispose removes its subscribers (M111); delayed send on the producer's lifetime (M112).
- **Event bus:**
  - Outbox over distributed starts: the bus is its own singleton and the hosted service takes it (M114).
  - Unknown type throws, corrupt messages go to `MessageDropped` (M116); consume path under `IEventScopeAccessor`
    (M117).
  - Dedup releases on failure via the new required `IDeduplicationStore.ReleaseAsync` (M118); pipeline runs with no
    handlers (M119, also on the distributed path).
  - Outbox shutdown is not a failed attempt (M121); `UnpublishedEventsKey` in the exception's Data (M122).
- **Tests:** Redis live 5 (+1 mock), MQTT 8, in-memory 4, distributed 4, event bus 3, outbox 1.
  - Red on HEAD: 29. The new members the tests touch were stubbed as no-ops: `OnHandlerError` ×2, `MessageDropped`,
    `UnpublishedEventsKey`.
  - Pin: the Redis FromBeginning=true case passes either way.
- **Inverted / replaced tests:**
  - CR-L248's "a throwing handler still dedups" (now released).
  - CR-M201's "a pre-cancelled delayed send returns quietly" (now throws OperationCanceledException).
  - CR-L258's cast-guard test (now a null-argument test: the cast is gone).
- **Test-only changes:** the Redis redelivery test's reflection seam; the EventBus.MessageQueue test project imports
  the outbox.
- **Found on the way:** the M101 live test raced the M102 change. A message sent before the first poll creates the
  group is not delivered to it, as decided; the test now waits for the group (5 of 5 green after).
