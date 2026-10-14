---
id: TASK-609
parent: EPIC-001
feature: FEATURE-001
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-10-14
depends-on: []
blocks: []
related: [TASK-580, TASK-597]
pr: null
github-issue: null
jira-key: null
---

# `b-drawer` renders its title as unescaped HTML in a `<span>`, and the dialog has no accessible name

## Context

Spawned from [[TASK-580]] (its out-of-scope line: "check `b-drawer` while here"), 2026-10-14.
`Birko.Web.Components/src/layout/b-drawer.ts` `render()` emits `<span>${title}</span>`:

- **The title is HTML.** It is interpolated raw, so a title carrying a record or tenant name with markup runs in the
  page. TASK-580 measured this on `b-modal`, whose title code was identical: an `<img onerror>` title **ran**. Same
  defect as `b-toast`'s TASK-597. This is why it is P2, not P3.
- **No accessible name.** The `<dialog>` has no `aria-labelledby` at all (b-modal at least had one), so a screen
  reader announces an unnamed dialog.
- **Not a heading**, so headings inside the drawer follow no heading (axe `heading-order`), as in TASK-580.

## Acceptance criteria

- [x] The title is rendered as text: markup in it is shown, never parsed or run
- [x] The title is a heading, `<h2>` by default, with `heading-level` (1–6), and no visual change, as `b-modal` now does
- [x] The `<dialog>` is labelled by the title; with no title there is no empty heading and no dangling reference
- [x] A Playground smoke alongside `modal-title-smoke`, proven red first

## Out of scope

- Other overlays (`b-confirm-dialog` already escapes its title and labels its dialog).

## Human test plan

N/A — covered by the Playground smoke (DOM, accessible name, computed type, payloads).

## Progress

**2026-10-14 — done, in Birko.Web v0.1.10** (Web `02f6bd3`, Playground `b3f61a8`).

- Same shape as [[TASK-580]]'s `b-modal` fix: `<h{level} id="drawer-title-…" class="overlay-title">`, `heading-level`
  1–6 (else 2), `margin: 0; font: inherit`; the `<dialog>` gains `aria-labelledby` when there is a title; no title
  renders an empty `<span>` and no reference. README's "Same API as `b-modal`" now holds for `heading-level` too.
- **The escaping half was real here as well:** the red run showed the `<img onerror>` title executing.
- Gate: `modal-title-smoke` now runs every check for both components (48). **Red: 16 drawer checks failed**, the
  modal's 24 passed; 48/48 after, full `verify.mjs` green.

