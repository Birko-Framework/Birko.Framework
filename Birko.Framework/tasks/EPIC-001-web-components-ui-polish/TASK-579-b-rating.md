---
id: TASK-579
parent: EPIC-001
feature: FEATURE-001
priority: P2
status: todo
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `b-rating` — a form-associated star-rating input

## Context

Asked for by the FlowerFurStudio storefront: its TASK-012 (`b-rating` in Birko.Web.Components) and, behind it,
TASK-025's review form, which submits a star rating. It is the storefront's last open framework gap (Outline
"Birko.Framework — open work & priorities", 12 Oct). The storefront renders **read-only** stars on its
server-rendered pages as plain markup and CSS; the component is for **input**, with a read-only mode for places
a client page shows a rating it already has.

No rating control exists in `Birko.Web.Components` today; `b-radio` is the nearest (a radio group).

## Acceptance criteria

- [ ] `b-rating` with `value`, `max` (default 5), `name`, `required`, `disabled` and `readonly`; whole stars only
- [ ] Keyboard-operable as a radio group: arrows move and select, Home/End go to the ends, one tab stop; each
      star has an accessible name ("3 of 5"), translatable through `bwc.rating.*`
- [ ] Form-associated like the other `b-*` controls: the value submits with a form, `required` validates,
      a form reset restores the initial value
- [ ] `readonly` shows the value with no interaction and no tab stop, and is announced as a value, not a control
- [ ] Theming through `--b-*` tokens; the filled star meets WCAG AA non-text contrast (3:1) in light and dark
- [ ] A Playground page and its smoke test; README and component catalogue entries

## Out of scope

- Half stars (the storefront does not need them)
- Read-only stars on server-rendered pages: the storefront keeps those as plain markup

## Human test plan

- [ ] Rate with the keyboard only, then with a screen reader (NVDA or Narrator): each star is announced with its
      position, and the chosen value is announced on change
