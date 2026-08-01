---
id: TASK-105
parent: STORY-052
feature: FEATURE-016
status: review
priority: P3
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `b-card`: the missing `md` padding rung, and elevation as a token

## Context

Found adopting `b-card` in **Reps** while converting its six Progress cards
(`Consumers/WorkoutTracker/tasks/EPIC-002-birko-backports/STORY-010-reps-adopt-cleanups/TASK-089-adopt-layout-and-state-components.md`,
§ "Cards (AC3)"). Three things came out of that conversion. **Two are fixed here, one is a considered
rejection, and a fourth is a framework-wide question that is deliberately not settled in this pass**
([[TASK-106]]).

## What landed

**1. `padding="md"`.** The scale was `none / sm / (default lg) / xl` — `md` was simply absent, though
`--b-space-md` exists and is used elsewhere in this very component (the card *header* already pads with
it). Reps' cards wanted `md` and had to round up to `lg`, so every Progress card got roomier than its
hand-rolled predecessor. That is an oversight in an otherwise complete ladder, not a design stance.
Purely additive: one `:host([padding="md"])` rule.

**2. `--b-card-shadow`, defaulting to `var(--b-shadow-sm)`.** `.card` hard-coded its elevation while the
same component already exposes `--b-card-header-bg` and `--b-card-header-text` — so it has *already*
decided that consumers may retint parts of it, and fixing the shadow while exposing the header colour is
an inconsistent line to draw. Elevation is a real design axis (flat vs raised), and the alternative open
to a consumer today — overriding `--b-shadow-sm` to flatten one card — silently flattens every other
component in scope.

Both are backwards compatible by construction: the default padding is still `lg` and the default shadow
still resolves to `--b-shadow-sm`.

Files: `Birko.Web.Components/src/layout/b-card.ts`, `API.md`, `README.md`, `CLAUDE.md` (component table +
Recent Updates). Coverage in `Birko.Web.Playground/src/backport-smoke.ts`.

## Considered rejection — no `layout` / `gap` / `direction` on `b-card`

Reps' conversion traded its `.card` CSS for a `.card-inner` light-DOM wrapper, because `b-card`'s flex
container lives in its shadow root: slotted children land in a plain padded body with nowhere for the
column gap to live. Every consumer wanting a stacked card writes that wrapper. The obvious-looking fix is
a `layout="column" gap="md"` attribute pair. **Rejected, and recorded so it is not re-proposed:**

- **It is not the card's job.** `b-card` is chrome — background, border, radius, elevation. How its
  contents stack is the contents' business. A card that can be column or row with any gap is a styled
  `div` with a border, and the attribute surface grows without bound the moment someone wants
  `align-items` or a row-gap that differs from the column-gap.
- **The need is not card-specific, so solving it here fixes one context and leaves the rest hand-rolling.**
  Reps wants the same stack in its Today hero (transparent, centred, deliberately *not* a card), its
  settings blocks and its plans list rows. A card-scoped answer helps none of them.
- **The workaround is one line of flexbox.** Compare that against the backports that earned their place —
  safe-area insets, diacritic folding, the windowed offline mirror, the iOS focus-zoom floor. Every one of
  those was something a consumer got *wrong*, or would rediscover the hard way. A flex column is neither.

If this is ever revisited, the right question is not "should `b-card` have a layout attribute" but
"should the catalogue have a stack/cluster primitive" — a different, larger question, and one that would
serve the three non-card contexts too.

## Acceptance criteria

- [x] `padding="md"` resolves to `--b-space-md` and sits strictly between `sm` and `lg`.
- [x] `--b-card-shadow` overrides the card's elevation, per instance and by inheritance from an ancestor,
      **without** disturbing `--b-shadow-sm` for anything else in scope.
- [x] Backwards compatible — default padding still `lg`, default shadow still `--b-shadow-sm`, an
      unrecognised `padding` value still falls back to the default.
- [x] No layout/gap/direction option added; the rejection is recorded above.
- [x] `--b-*` tokens only, no new hard-coded colours or spacings; component table + `Recent Updates`
      updated per the repo convention.
- [x] Playground verifier green — 131/131 backport-smoke (10 new), all six harnesses green, no page errors.
- [x] Reps still renders identically (it has opted into neither option) — 0 cards differing, both themes.

## Verification

Playground: 131/131. **The five fix-dependent checks were proven to fail** by removing the `md` rule and
the token and re-running — 126/131, and exactly the five that should break did. The five back-compat
checks stayed green under the same perturbation, which is what makes them back-compat checks rather than
restatements of the fix.

Reps: rebuilt and loaded on the Testing host, Progress surface at phone width in light **and** dark.
**Zero** cards opted into either option, **zero** differed from the default, zero failed to render, no page
errors. That is the point of the check: Reps has adopted neither option, so any delta would have meant the
change was not backwards compatible.

Two things the measurement got right by not hard-coding:

- Body padding measured **14px**, not the 16px `--b-space-lg: 1rem` suggests, because the Birko reset sets
  `html { font-size: var(--b-text-base) }` = `0.875rem` = 14px, so a `rem` resolves against a 14px root
  throughout. The check compares against a probe carrying the live token rather than a px literal, so it
  asserts "the card still uses `--b-space-lg`" instead of "the card is 16px" — the latter would have failed
  here for the wrong reason, and would keep passing if the scale were ever rescaled.
- **Five** `b-card`s render, not the six the origin task counts. The sixth (`#metrics-offline-card`) is
  conditional on `windowState === 'unavailable'` — the offline stand-in for the body and steps cards — so it
  is correctly absent on a host that is reachable. Six in source, five on screen.

## Cross-links

- Origin: `Consumers/WorkoutTracker/tasks/EPIC-002-birko-backports/STORY-010-reps-adopt-cleanups/TASK-089-adopt-layout-and-state-components.md` § "Cards (AC3)"
- Open framework-wide question raised by the same conversion: [[TASK-106]] (`::part` as a catalogue direction)
- Sibling gap from the same consumer: [[TASK-104]] (`b-chart` small-chart axis)
