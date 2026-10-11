---
id: TASK-318
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
findings: [SH-H020]
pr: null
github-issue: null
jira-key: null
---

# Triage the 1 remaining high spec-harvest finding in `event-bus-and-messaging`

## Context

Filed by `/tasks intake --epic EPIC-014 --story STORY-051` on 2026-09-08. [[STORY-051]] had **31 task
files covering 18 of its 57 findings**, and the other **39 had no task at all** — so they were
invisible to `/tasks pick`, to the `Next up` snapshot and to [[fix-next]]. *A checklist line is filed,
not scheduled.* This is the same decomposition [[STORY-053]] received on 2026-08-09 for the medium
findings, applied a month later to the tier that outranks it: **high** means silent data loss,
cross-tenant leakage, auth bypass, or a destructive operation on the wrong rows.

This task owns the **1** open finding in the `event-bus-and-messaging` area.

| Finding | Claim | Named location |
|---|---|---|
| `SH-H020` | Redis non-group subscription reads from "$" forever and never receives any message | `Birko.MessageQueue.Redis/RedisConsumer.cs:224` |

Detailed in [`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md)
§ High severity -> `### area: event-bus-and-messaging`, lines 164-170.

**The contract under review** is specced in [`docs/specs/event-bus-and-messaging.md`](../../../docs/specs/event-bus-and-messaging.md),
harvested from 80 source files — `../Birko.EventBus.EventSourcing/DomainEventPublished.cs`, `../Birko.EventBus.EventSourcing/EventReplayService.cs`, `../Birko.EventBus.EventSourcing/EventStoreEventBus.cs`, and more. Every one points into a **sibling repo**, so a
fix here normally lands as three commits in three repos (production, regression suite, this file) per
CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before
fixing. The prior to carry in comes from the 15 high findings that *were* checked by hand at harvest
time: **13 CONFIRMED** (one of them re-verified **wider** than filed), **2 CONFIRMED-NARROWER**,
**0 refuted**. So expect most to hold and a meaningful minority to need their scope corrected — and
note that "wider" has happened, so a claim is not a ceiling. Refuting on the record is a valid close; a
finding silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Measured consumer reach, 2026-09-08** (so the fix is priced on what it protects, not on the claim's
wording): **Latent.** `RedisConsumer` / `Birko.MessageQueue.Redis`: referenced only by the Sandbox aggregator.

⚠ Latent is **not** a reason to skip or downweight a finding — the framework's recent history is
largely defects that stayed latent until a consumer selected the backend, and § TASK-219/256 record
that such a window *"closes the moment one does"*. It is a reason not to overstate urgency in a report.

**Why P1:** a subscription that never receives anything is loud in effect (nothing works) rather than silent, and it is Redis-non-group only.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The
harvest specced what the code *does*, defects included, which is exactly what let it find them. So a
behavioural fix leaves `docs/specs/event-bus-and-messaging.md` lying until `/specs regen event-bus-and-messaging` runs, and **that spec
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
- [x] Any behavioural fix is followed by `/specs regen event-bus-and-messaging`, with the spec diff reviewed as the
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
- Medium-severity findings in this area — [[TASK-155]] owns those.
- Low-severity findings in this area — [[TASK-174]] owns them. Where a fix closes findings across
  tiers, do it once and cross-reference; do not split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is
  not a finding here — only a test a confirmed fix needs.

## Human test plan

N/A — library code with no UI surface; fully covered by automated tests (`RedisNonGroupReadPositionTests`), with the
driver behaviour they emulate measured against a live Redis 7.

## Implementation plan

_Populated by `/tasks plan TASK-318` — leave empty until then._

## Outcome

**Fixed; confirmed wider, with the mechanism corrected.** A Redis Streams subscription without a consumer group — the
default — never received a single message: it started reading at `"$"`, which StackExchange.Redis refuses for `XREAD`,
so every poll threw into an `internal` error event nobody can subscribe to and retried forever. The read position now
starts at the stream's newest entry id (or `"0-0"` for an empty stream), and it advances past every entry read, so an
unparseable or expired entry no longer pins the position and spins the loop.

- **Measured (live Redis 7, SE.Redis 2.13.17, throwaway container, removed):** `XREAD "$"` →
  `InvalidOperationException: StreamPosition.NewMessages cannot be used with StreamRead.` before and after an add;
  `XREVRANGE COUNT 1` gives the tail; `XREAD` from the tail returns 0 before an add and exactly the new entry after it;
  `XREAD "0-0"` / `XREVRANGE` on a missing stream return empty.
- **Step-6 split (53 tests in the project):** tail resolution reverted to `"$"` → **2/53** red:
  `A_default_subscription_receives_a_message_published_after_it_started`,
  `A_default_subscription_skips_what_was_already_in_the_stream`. Position advance moved back after processing →
  **1/53** red: `An_unparseable_entry_does_not_hold_the_read_position`. **Contract pin, not evidence:**
  `A_FromBeginning_subscription_reads_the_whole_stream` (and the 49 pre-existing tests).
- **Judgement calls.** (1) The tail is resolved at the first poll, inside the loop's `try`, not in `SubscribeAsync`: a
  connection error then retries like any other instead of failing the subscribe — the cost is that a message produced
  between `SubscribeAsync` returning and the first poll can be skipped (one scheduling hop; the old code delivered
  nothing at all). Rejected: resolving in `SubscribeAsync`, which would make subscribe throw on a transient outage and
  still not close the window for a message produced before the call. (2) The fake stream **throws on `"$"`** rather
  than returning empty, so the tests fail on the original code for the reason production did. (3) `PollError` stays
  `internal` — making poll faults observable is a public-API decision outside this finding.
- **Flagged, not fixed (already filed):** SH-M102 (an auto-created consumer group starts at `0-0` and replays the whole
  stream despite `FromBeginning = false`) and SH-M103 (a transient error during the pending drain kills the
  subscription), both under [[TASK-155]].
- **Merge gate:** inline review of the diff. No convention hit (a read position, not a write scope); no nullable
  warnings; no security surface.

## Progress log

- step 2 — picked; ranked above TASK-555 (Update of a missing entity reports success) because SH-H020 claims total silent non-delivery on the *default* configuration (no consumer group, FromBeginning false) while TASK-555 is a wrong success on a misuse path; key 1 (severity: silent loss of every message) broke it
- step 3 — verified: held in effect (zero delivery, silent), **mechanism corrected, and wider by one defect.** Measured against live Redis 7 with StackExchange.Redis 2.13.17: `StreamReadAsync(key, "$")` does not "return nothing" — the client refuses it before sending (`InvalidOperationException: StreamPosition.NewMessages cannot be used with StreamRead.`). So on the default non-group path (`RedisStreamSettings.ConsumerGroup` null, `FromBeginning` false) every poll throws; `RunPollLoopAsync`'s catch-all raises `PollError` and retries 2 s later, forever; `LastReadId` (only set in `ProcessEntryAsync`) never leaves null. `PollError` is `internal`, so nothing outside the assembly can observe it, and `IsActive` stays true — the silence holds. **Wider, same position tracking:** `ProcessEntryAsync` returns before `state.LastReadId = entry.Id` for an unparseable or TTL-expired entry, so on the non-group path such an entry at the end of a batch is re-read on every poll — with no delay, since the batch is non-empty — a hot loop until a newer entry arrives
- step 4 — layer: local (Birko.MessageQueue.Redis; the client's refusal of "$" on XREAD is documented driver behaviour — the framework's usage is wrong)
- step 5 — fix in `Birko.MessageQueue.Redis/RedisConsumer.cs` (tail resolution + `ResolveStreamTailAsync`; position advance moved first); tests in `tests/Birko.MessageQueue.Redis.Tests/RedisNonGroupReadPositionTests.cs` (4); suite 53/53 green
- step 6 — tail reverted: 2/53 red; advance reverted: 1/53 red; fix-dependent = the two default-subscription tests + the unparseable-entry test; contract pin = FromBeginning test; driver semantics re-measured live
- step 7 — respecced event-bus-and-messaging; requirement *Redis Streams delivery, failure retention and reclaim* (start position, advance rule) and scenario *Subscription with no consumer group and default options* rewritten; scenario *An unparseable entry without a consumer group* added
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log
