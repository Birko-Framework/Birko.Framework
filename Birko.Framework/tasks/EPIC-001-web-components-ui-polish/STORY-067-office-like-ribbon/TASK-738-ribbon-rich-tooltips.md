---
id: TASK-738
parent: STORY-067
feature: FEATURE-019
status: verify  # verified with real hover and keys 2026-10-22; the human test plan is left
priority: P2
assignee: ai
created: 2026-10-22
depends-on: []
blocks: [TASK-742]
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: rich tooltips (title, description, shortcut)

## Context

FEATURE-019 D9 (approved by the user 2026-10-22): Hovering a command shows a proper tooltip with the command name, a one-line description and its keyboard shortcut, instead of the browser's plain label.

Commands use the native title attribute (label only).

Look first: this changes what a stakeholder sees, so show a mockup or 2-3 options in the Playground and get the user's pick before editing the shipped files (CLAUDE.md, feature lifecycle step 8).

## Acceptance criteria

- [x] RibbonItem can carry a description and a shortcut; the tooltip shows title, description and shortcut
- [x] Built on b-tooltip (or its pattern): delay, keyboard focus shows it, Escape hides it, aria-describedby links it
- [x] No native title left behind to double-announce
- [x] Components README, API.md and CLAUDE.md inventory updated; i18n keys under `bwc.ribbon.*`

## Out of scope

- The Avalonia ribbon (TASK-742)

## Human test plan

- [ ] Hover and keyboard-focus commands: the tooltip shows, reads in a screen reader, and hides on Escape

## Outcome (2026-10-22)

Committed to Birko.Web and the Playground.

- **Decided by the user.** Asked to pick from three looks, the user asked for b-tooltip itself "with some theme on it".
  Of the themes, light is the ribbon's default (dark stays b-tooltip's default). Timing: hover delay plus keyboard
  focus.
- **b-tooltip.**
  - `theme="light"` builds a card from the surface tokens, so no new token was needed.
  - `heading` / `shortcut` attributes form a structured layout in which `text` is escaped too. `text` used alone
    still inserts markup, as before.
  - `showFor(anchor, content)` / `hide()` / `shown` add an anchored mode. It is a manual popover, so it is drawn in the
    top layer above a ribbon flyout, and it flips upward when it would leave the viewport at the bottom.
- **b-ribbon.**
  - `RibbonItem.description` / `shortcut` feed one shared `<b-tooltip class="ribbon-tooltip">`, with `tooltip-theme`
    defaulting to light. Folded groups show their name, and ▾ parts "More options: …".
  - The tooltip shows after 500 ms of hover (`BRibbon.TOOLTIP_DELAY_MS`); after a shown one, the next shows at once
    for 300 ms. It shows on `:focus-visible` focus. Escape (captured) only hides it; pointerdown, Enter/Space and a
    menu or flyout opening hide it.
  - Native `title` removed from commands, ▾ parts and chunks; `aria-description` / `aria-keyshortcuts` added.
- **Changed behaviour:** with a tooltip up, the first Escape hides it and the second does what Escape did before.
  WCAG 1.4.13 needs a dismissal that does not move focus. TASK-731's flyout checks were updated for it.
- **Fixed from TASK-731:** ←/→ used centres, so from "Cut" → went to the wider "Copy" below. A candidate must now be
  wholly beyond the current command.
- **Gate.** Playground `keyboard-check.mjs` 79/79, with real hover and keys, three runs.
  - New: delay, at-once switching, ▾ tooltip, leaving hides, focus shows and follows, Escape twice, a menu opening
    hides it, top layer over a flyout. 7 fail on the TASK-732 ribbon and the old b-tooltip.
  - `tooltip-theme-smoke` 11/11: the old text form unchanged, structured text escaped, the light surface, ribbon
    aria attributes, no title, one shared tooltip, `tooltip-theme`.
  - `ribbon-scaling-smoke`'s two name checks read `aria-label` now, not `title`.
  - `verify.mjs` clean twice; entry-points 409/409; `tsc` clean. Checked in Chrome (Cut's tooltip, light).
- **Test notes.** Hit-testing cannot see the tooltip (`pointer-events: none`), so on-top is checked as "top layer,
  opened later". A click on `#kb-after` landed inside the hanging flyout, so that check clicks `#kb-before`.
