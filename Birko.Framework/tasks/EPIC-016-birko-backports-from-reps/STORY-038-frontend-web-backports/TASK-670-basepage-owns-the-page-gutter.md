---
id: TASK-670
parent: STORY-038
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
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

# The page gutter is copied into eleven Reps pages instead of living in `BasePage`

> **Moved from WorkoutTracker TASK-124 on 2026-10-19.** It was filed there as Birko.Web work, where the framework's pick never saw it. Every TASK / STORY / FEATURE id in the body below is **WorkoutTracker's** (its TASK-123 is this repo's [[TASK-599]]). There, TASK-124 stays open as the Reps-side check, blocked on this task.


## Context

**Filed 2026-08-02, late.** Recorded on **TASK-089** as *"Worth a `Birko.Web.Shell` backport under STORY-009;
not taken here, since changing the default padding touches every consumer page and deserves its own task"* —
and then not filed. Found by a sweep for exactly this pattern, alongside TASK-123 and TASK-125.

### The defect, and why it is a defect rather than a duplication

`.base-page { padding: 1rem }` is repeated in **eleven** Reps pages. TASK-089's own note names the consequence
precisely: *"two pages forgetting to opt in is the predictable result"* — and that is what happened, which is
how the copies were noticed at all. A gutter every page needs, that every page must remember to add, will be
forgotten; the forgetting is the design, not the developer.

This is the same shape as **TASK-122** (`hidden` not hiding) and the argument is the stronger of the two:
TASK-122 had two hand-rolled copies, this has eleven. The bar this project set for lifting something into the
framework — *repeated* hand-rolling, not a single speculative caller (see TASK-114's declined helper, which had
one) — is cleared by a wide margin.

## The care this needs

It is a one-line rule with a wide blast radius, which is why TASK-089 declined to fold it in:

- The gutter moving into `BasePage.styles` changes the default for **every page in every consumer**, including
  Symbio's. A page that already sets its own padding would then get both unless the base rule is written to be
  overridable — which it must be, since pages that deliberately go full-bleed exist.
- **Opposite requirement to TASK-122.** That guard is `!important` on purpose, because "hidden means hidden" is
  an invariant. A gutter is a *default*: a page must be able to override it with an ordinary rule. Do not copy
  the `!important` from the neighbouring change — the two look alike and are not.
- Reps' eleven copies come out **after** the base rule lands, and their removal is the proof it works. Removing
  them first would leave the app un-guttered until the framework change is committed and pulled.

## Acceptance criteria

- [ ] `BasePage` provides the page gutter as an **overridable default** — a page setting its own padding gets
      its own, not both.
- [ ] A page that deliberately wants no gutter can say so without `!important` gymnastics; the mechanism is
      documented next to the rule.
- [ ] All eleven Reps copies are removed, and the pages render unchanged — verified by the existing layout
      invariants in the WebKit lane, which already assert phone-width geometry.
- [ ] The two pages that *forgot* the gutter (the finding that exposed this) now get it automatically. Name
      them here when picked up — they are the before/after proof.
- [ ] Symbio is checked for pages that set their own page padding, or the risk is explicitly recorded as
      unassessed.

## Out of scope

- Any other repeated per-page style. This is the gutter only; a wider sweep for copied page chrome is its own
  task if the pattern proves broader.
- Changing the gutter *value*. It moves; it does not change size. A visual change would need its own decision.

## Human test plan

- [ ] After the eleven copies come out, walk the app at phone width and confirm nothing shifted. The automated
      invariants cover geometry at one viewport; this is the "does every screen still look right" pass that a
      layout default deserves, and it is cheap to do alongside another device session.

## Implementation plan

_Populated when picked up._
