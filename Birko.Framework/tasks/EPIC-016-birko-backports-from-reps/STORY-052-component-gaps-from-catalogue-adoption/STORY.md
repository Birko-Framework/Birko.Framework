---
id: STORY-052
parent: EPIC-016
status: in-progress
created: 2026-07-30
---

# Component gaps found by consumers adopting the `b-*` catalogue

## User story

As a **Birko.Web consumer replacing a hand-rolled widget with the catalogue equivalent**, I want the
gaps that swap exposes fixed **in the component**, so that adopting `b-*` is never a trade of one
set of rough edges for another — and so the next consumer inherits the fix instead of rediscovering
the gap.

## Why a separate story

[[STORY-038]] is a **done completed-ledger** of the capabilities Reps pushed upstream; it is closed
and must not be reopened. This story is a different shape and stays open for the life of the epic:
not "move an app-side capability into the framework", but "a framework component already exists, a
consumer adopted it, and the adoption found something the component gets wrong."

The distinction matters because the two have opposite acceptance tests. A backport is done when the
consumer can delete its copy. An adoption gap is done when **every** consumer benefits without any
of them special-casing — the fix lives in the component or it is not a fix. A consumer keeping a
fork *is* the failure mode this story exists to prevent.

## Behaviour

- A gap is written up in the **consumer's** tree first (that is where the evidence is: real data,
  real viewport, screenshots), then handed here with the analysis and acceptance criteria intact —
  not re-derived.
- The consumer task becomes a **pointer** to the framework task rather than holding the work.
- The fix is general. If it needs a knob, the knob is a documented option with a backwards-compatible
  default, not a branch on who is calling.
- **"No" is a legitimate outcome, and it gets written down.** A gap the consumer can close in one line of
  its own code is not automatically framework work — the backports that earned their place were things
  consumers got *wrong* or would rediscover the hard way. A rejection is recorded in the task with its
  reasoning, so the next consumer to hit it finds a decision rather than an unexplored idea (TASK-105 §
  "Considered rejection").
- A gap may also turn out to be a **framework-wide question** rather than a component fix. It gets its own
  decision task rather than being settled as a side effect of the component change that surfaced it
  (TASK-105 → TASK-106).
- Coverage lands in `Birko.Web.Playground`'s `backport-smoke.ts` — the framework has no in-tree JS
  unit runner, so the playground build + headless verify is the vehicle. Prefer exporting the pure
  maths so it is assertable without a DOM, then assert the rendered behaviour on top.
- Verification finishes **in the consumer that reported it**, at the size and theme that showed the
  problem. The playground proves the component is correct; only the reporting surface proves the
  complaint is answered.

## Tasks

| Task | Component | Origin |
|---|---|---|
| [[TASK-104]] — small-chart axis polish (tick density, nice scale, latest-value overlay, threshold labels) | `b-chart` | Reps `EPIC-002 / STORY-009 / TASK-092` |
| [[TASK-105]] — `padding="md"` rung + `--b-card-shadow`; layout/gap rejected | `b-card` | Reps `EPIC-002 / STORY-010 / TASK-089` |

Spun out of the above, and deliberately **not** decided inside a component task:
[[TASK-106]] (`tasks/_loose/`) — whether `::part` becomes a catalogue-wide convention or stays the one-off
it is today (`b-sidebar` alone). Raised by TASK-105's rejection of a layout attribute on `b-card`.

## Success criteria

- Each gap is fixed in `Birko.Web.*`, with no consumer-specific branch and no consumer left on a fork.
- Existing adopters keep working: a behavioural default only changes when the change is the point of
  the ticket, and it is called out in `Recent Updates` when it does.
- Playground verifier green, with the new behaviour asserted rather than merely not-crashing.
- The reporting consumer re-checked by eye on the surface that raised it.
