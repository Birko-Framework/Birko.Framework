---
id: TASK-601
parent: STORY-038
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: unassigned
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-039]
pr: null
github-issue: null
jira-key: null
---

# The framework's touch-target token is defeated by the framework's own reset

> **Moved from WorkoutTracker TASK-147 on 2026-10-14.** It was filed there as Birko.Web work, where the framework's pick never saw it. Every TASK / STORY / FEATURE id in the body below is **WorkoutTracker's**. There, TASK-147 stays open as the Reps-side check, blocked on this task.


## Context

**Measured 2026-08-03 while checking a TASK-134 acceptance criterion** that claimed a 44px touch target. It was
false, and fixing it locally exposed why it is false everywhere.

`tokens.css` declares the touch size in rem, with its intent in the comment:

```css
--b-control-min-height-sm: 1.75rem;  /* 28px — dense grids, toolbars */
--b-control-min-height:    2.375rem; /* 38px — default */
--b-control-min-height-lg: 2.75rem;  /* 44px — touch targets */
```

Those px annotations assume a **16px root**. `reset.css` — the same stylesheet consumers are told to adopt
alongside the tokens — then sets:

```css
html { font-size: var(--b-text-base); }   /* 0.875rem → 14px */
```

So in any app that adopts both, the root is 14px and `2.75rem` resolves to **38.5px**, not 44px. The token
documented as "touch targets" lands 5.5px under the platform floor, and the intent recorded in its own comment is
silently wrong.

**Measured in Reps at 320×568:**

| Control | Height | |
|---|---|---|
| Hand-rolled `.side` button (before TASK-134) | **37px** | no min-height at all |
| Same, with `--b-control-min-height-lg` | **38.5px** | the token, resolving against a 14px root |
| Framework `b-button` (the rest row) | **38.9px** | every button in every consumer |
| Today's week-row `Change` (TASK-138, 2026-08-07) | **38.9px** | the same defect, re-measured on a new control — see the note below |

The third row is the one that matters: **adopting the catalogue does not fix this.** TASK-134 assumed it would —
"the b-buttons alongside were already compliant" — and that was wrong, caught only because the assertion measured
all three buttons rather than the one that had been fixed.

Reps floored its own hand-rolled row with `max(var(--b-control-min-height-lg, 2.75rem), 44px)` as a local stopgap,
which is exactly the kind of per-consumer patch that belongs upstream instead.

### Re-confirmed 2026-08-07 while building TASK-138 — and it changed how that task tests

TASK-138 added a `Change` action to Today's week row. It shipped first as `size="sm"` and measured **36.5px**;
dropping the small variant took it to **38.9px** — this defect exactly, on a control written months after it
was filed. Two things worth carrying back here:

- **The screenshot was no help.** The stakeholder preview for that control renders perfectly well at 36.5px;
  only the measurement said otherwise. That is the second time this has happened after TASK-149, which is the
  argument for measuring every new interactive control rather than the ones that look suspicious.
- **The new lane invariant (family G) asserts a *relative* floor, not 44.** It requires the control to be no
  smaller than an ordinary `b-button` on the same surface, because forking a local padding override to reach 44
  on one button is what the catalogue rule forbids — and it would make that control diverge from every other
  button the moment this task lands. So the absolute floor stays this task's to assert, and family G's guard
  rises with it automatically instead of needing a rewrite. Anything that fixes the token here should re-run
  family G and consider tightening it to `assertTouchFloor`.

## Why this is not merely cosmetic

44px is the iOS Human Interface floor and WCAG 2.1 SC **2.5.5** Target Size (Enhanced), at level AAA — *not* SC
2.5.8 Target Size (Minimum), which is 24 × 24 at AA and which this line cited until 2026-08-04 (the same slip was in
TASK-149 and in the component; see that task's corrections note). The 44 is the right target to hold this library to;
the citation overstated what conformance requires. Under it, taps land off-target — and
this app's most-tapped controls are used mid-set, one-handed, while holding a weight. It is also the sibling of
TASK-123 (every semantic tint below the AA contrast floor): a token whose documented intent is not what it
computes, in the shared library, so it is every consumer's defect at once.

## Change

Decide **where** the floor belongs — this carries an open decision, because the two options have different blast
radii:

1. **Make the token absolute** — `--b-control-min-height-lg: 44px` (and reconsider the other two). Honest and
   immediate; loses the rem-scaling that lets the whole control set grow with a user's font size, which is a real
   accessibility property, not decoration.
2. **Make it root-independent** — `max(2.75rem, 44px)`, keeping the scaling while guaranteeing the floor. Slightly
   more subtle; preserves both properties. **Recommended.**
3. **Stop the reset shrinking the root** — set the base size on `body` rather than `html`, so `rem` keeps meaning
   16px. The cleanest in principle and by far the widest blast radius: every rem in every consumer changes size.
   Almost certainly not now, but it is the underlying mistake and should be recorded as such.

Whichever lands, the px comments in `tokens.css` need to become true or go — a comment asserting "44px" beside a
value that computes to 38.5 is what let this survive.

## Acceptance criteria

- [ ] `b-button` (and the other interactive controls sized from these tokens) measure **≥ 44px** at the coarse
      pointer, in an app that adopts `reset.css` — i.e. with a 14px root.
- [ ] The `sm` / default variants are reviewed in the same pass and their comments made true, rather than only the
      one this was found through.
- [ ] Nothing grows on desktop where it should not — a dense grid or toolbar using `-sm` is not silently inflated.
- [ ] The px annotations in `tokens.css` state what the values actually compute to under the shipped reset.
- [ ] Reps drops its local `max(...)` floor on `.side` once the token is correct, so the stopgap does not outlive
      the defect.
- [ ] A guard in the WebKit lane: assert the coarse-pointer height of a `b-button` and of Reps' hand-rolled
      controls, red-verified against today's values (38.9px / 37px). This is the same shape as TASK-110's 16px
      font-size invariant, and for the same reason — a floor nobody measures regresses silently.
- [ ] Checked against `Birko.Web.Playground` and Symbio before landing: a global control-height change is exactly
      the kind that looks free and moves every form on every screen.

## Human test plan

- [ ] On the iPhone, tap each of the session's rest controls repeatedly at the edges of the buttons — no missed
      taps.
- [ ] The same row at 320px (Display Zoom, or the WebKit lane's viewport) — still 44px, still not wrapping.
- [ ] One dense surface (Settings' per-exercise rows) to confirm nothing grew that should not have.
