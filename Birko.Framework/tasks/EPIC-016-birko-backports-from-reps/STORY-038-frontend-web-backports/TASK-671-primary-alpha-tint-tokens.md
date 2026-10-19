---
id: TASK-671
parent: STORY-038
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P3
assignee: unassigned
created: 2026-10-19
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# `--b-color-primary-alpha-*` is missing while the other four semantic tints have it

> **Moved from WorkoutTracker TASK-125 on 2026-10-19.** It was filed there as Birko.Web work, where the framework's pick never saw it. Every TASK / STORY / FEATURE id in the body below is **WorkoutTracker's** (its TASK-123 is this repo's [[TASK-599]]). There, TASK-125 stays open as the Reps-side check, blocked on this task.


## Context

**Filed 2026-08-02, late.** Recorded on **TASK-100** as *"Note for the framework backlog (not filed)"* and then
not filed. The smallest of the three found by one sweep for this pattern (with TASK-123 and TASK-124), and the
one most likely to be dismissed as trivia — which is roughly why it sat in a paragraph.

### The gap

The `--b-color-*-alpha-{bg,border}` family covers **danger / success / warning / info** but **not primary**.
TASK-100 hit it while building the end-of-absence offer card: with no primary tint available, the card is
**neutral rather than tinted**.

TASK-100 judged that defensible on its own terms, and it is — the four existing tints are already spoken for on
Today (illness, vacation, deload, and the queued-write note), so a neutral offer card does not collide with
them. The point is not that the card is wrong. It is that the *reason* it is neutral is a missing token rather
than a design choice, and nothing on the card says so. The next person to want a primary tint will find the
same gap and make the same local decision.

TASK-100's own note names the shape: *"the gap is the same shape as TASK-113's missing `*-light` tokens"* — and
that one turned out to be hiding a family-wide contrast failure (**TASK-123**). This is not a claim that this
gap hides anything comparable; it is a reason not to leave a known incompleteness in a token family
undocumented.

## Change

Add `--b-color-primary-alpha-bg` and `--b-color-primary-alpha-border`, per theme, matching how the other four
are derived. Then re-examine the offer card — **as a separate judgement**, not automatically.

## Acceptance criteria

- [ ] `--b-color-primary-alpha-{bg,border}` exist in **both** themes, derived the same way as the other four
      rather than hand-picked, so the family stays a system.
- [ ] The pair meets the same contrast expectation as the rest of the family. **Coordinate with TASK-123** — if
      that task changes how the semantic families derive their values, this one should land in the same shape
      rather than adding a fifth member to a pattern about to be replaced.
- [ ] Whether the end-of-absence offer card *should* become tinted is decided explicitly and recorded here.
      Adding the token does not oblige any surface to use it, and Today already carries four tints — a fifth may
      be noise. **Default to leaving the card alone** unless someone argues otherwise.
- [ ] Symbio is checked for a local workaround that hand-rolls a primary tint; if one exists, it comes out and
      is the proof this was worth doing.

## Out of scope

- Any other missing token. This is the primary alpha pair only.
- Restyling the offer card as part of landing the token — that is a visual decision with its own owner
  (FEATURE-004 D4), and the card is currently *fine*.

## Human test plan

- [ ] N/A — fully covered by automated tests if the token is added without changing any surface. If the offer
      card is retinted after all, that becomes a visual check on a device and this line should be replaced with
      it rather than ticked.

## Implementation plan

_Populated when picked up._
