---
id: TASK-777
parent: EPIC-001
status: verify
priority: P2
assignee: ai
created: 2026-10-27
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-carousel: opt-in stop/start control shown only on hover or keyboard focus

## Context

Requested by the FlowerFur storefront session (FlowerFur TASK-133, FEATURE-004 D10 changed 2026-10-27), approved by
the user. FlowerFur's home hero is a `b-carousel autoplay="6000" loop` over server-rendered slides. The owner does not
want the round stop/start button (`.rotation`, shadow DOM, no `part`) showing all the time, only on hover or while
keyboard focus is inside the carousel. The storefront cannot style it.

The owner accepts that touch devices have no hover, so the button will not show there (a WCAG 2.2.2 trade-off they
decided). The default stays as it is: always visible.

## Acceptance criteria

- [x] New attribute `rotation-control="hover"`. Absent (or any other value), the control is always visible, as before
- [x] With it, `.rotation` is hidden unless the host is hovered or has focus within. It is hidden by `opacity`, not
      `display`/`visibility`, so it keeps its tab stop and its accessible name: Tab into the carousel lands on it and
      shows it
- [x] Hidden, it takes no pointer input (`pointer-events: none`), so a tap on its spot reaches the slide
- [x] Unchanged: no autoplay under `prefers-reduced-motion`, pause on hover, stop for good on focus
- [x] Playground carousel-smoke + keyboard-check (real Tab and mouse) cover it; API.md, ACCESSIBILITY.md, CHANGELOG
- [ ] Commit reported back to the FlowerFur session

## Human test plan

1. In FlowerFur's home hero with `rotation-control="hover"` on a desktop browser: the stop button is not visible at
   rest; it appears while the pointer is over the carousel and disappears when it leaves.
2. Click into the address bar, then press Tab until focus reaches the carousel: the stop/start button appears with a
   focus ring and is the first stop in the carousel. Rotation stops. Enter/Space starts it again.
3. Tab out of the carousel: the button hides again.
4. A carousel without the attribute (Playground gallery) still shows the button at rest.

## Outcome (2026-10-27)

- Birko.Web `506d9f9`, tag `v0.1.19` (CSS only on `:host([rotation-control="hover"])`, plus API.md, ACCESSIBILITY.md
  and CHANGELOG). Playground `f806fe8`.
- **keyboard-check +7** (trusted input): hidden at rest; a real Tab from the button before lands on the stop button
  and shows it; Enter restarts rotation; focus leaving hides it; a real pointer over the carousel shows it, leaving
  hides it. 218/218.
- **carousel-smoke +3**: default shown at rest; hover mode hidden at rest but laid out, named, a tab stop. 59/59. Its
  focus check moved to keyboard-check, because verify.mjs's page never holds document focus (`document.hasFocus()`
  false), so programmatic focus there cannot match `:focus-within`.
- **Mutation** (the CSS removed): keyboard-check 3 red, carousel-smoke 1 red.
- verify.mjs 0 failing, entry-points 409/409.
- Stays at `verify` until FlowerFur's hero runs Human test plan steps 1–3.
