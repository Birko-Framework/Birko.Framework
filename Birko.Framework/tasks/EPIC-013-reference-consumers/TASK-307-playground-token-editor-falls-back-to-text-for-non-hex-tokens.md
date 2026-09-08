---
id: TASK-307
parent: EPIC-013
feature: FEATURE-013
status: todo
priority: P3
assignee: ai
created: 2026-09-08
depends-on: []
blocks: []
related: [TASK-038]
findings: []
pr: null
github-issue: null
affects: [Birko.Web.Playground]
---

# The playground's token editor has no editor for `rgba()`/`hsla()` tokens, and none for lengths

## Context — a "tracked follow-up" that was not tracked

`src/app.ts`'s `renderTokenRow` carries this comment:

```
// Alpha-carrying hex (#rgba or #rrggbbaa) → show the opacity slider so the alpha byte is editable.
// rgba()/hsla() tokens still fall to a text b-input (b-color-picker is hex-based) — tracked follow-up.
```

Measured 2026-09-08: **nothing tracked it.** No task in the tree mentions it, so per § *findings become
tasks* it gets an id — the comment is otherwise a promise to nobody.

Two related gaps, grouped because both live in that one function and both are the same shape (the
editor chosen is a plain text `b-input` where a typed control would serve):

1. **`rgba()` / `hsla()` tokens** get a text field. `isColor(base) && /^#/.test(current)` gates the
   colour picker on the value being **hex**, so a functional-notation colour is editable only as text —
   no swatch, no alpha slider, and a typo silently produces an invalid declaration the browser drops.
2. **Length tokens** get a text field too. [[TASK-038]]'s own criterion asks for *"sensible editors per
   token kind (color → `b-color-picker`, lengths → number+unit)"*; the colour half shipped, the length
   half did not.

## Why P3

The playground is a developer tool, the text field **works** (the token is applied, exported and
round-tripped exactly as typed — verified by `verify.mjs`'s token/export checks), and every affected
token can be edited by typing a valid value. This is ergonomics, not correctness.

## Acceptance criteria

- [ ] `rgba()`/`hsla()` tokens get a colour editor with an alpha control, **or** the limitation is
      stated in the UI rather than only in a source comment — a text field that looks like every other
      text field gives the user no signal that the swatch is missing on purpose.
- [ ] A length token (`--b-space-*`, `--b-radius-*`, sizing) gets a number+unit editor, or that half of
      TASK-038's criterion is explicitly withdrawn with a reason.
- [ ] ⚠ Whatever is chosen must keep the **export** byte-identical for an unedited token — the diff is
      computed against the base value as a string, so a control that normalises `rgba(0,0,0,.5)` to
      `rgba(0, 0, 0, 0.5)` would make an untouched token appear changed and pollute every export.
      That is the trap here, and it is why this is not a two-line change.
- [ ] Covered in `verify.mjs` alongside the existing token/export checks, driven through the real
      control the way those are.
- [ ] Proven able to fail.

## Out of scope

- The rest of [[TASK-038]], now in `review`.
- `b-color-picker`'s own hex-only nature — changing that is a framework component change
  (`Birko.Web.Components`), not a playground one, and would need its own measurement of who relies on
  the current `value` shape. [[TASK-035]] already records that it submits base hex while `.value` keeps
  the alpha byte.

## Human test plan

- [ ] Open the token drawer, filter for a `rgba(` token, and confirm whatever editor it gets is
      obviously appropriate to it.
