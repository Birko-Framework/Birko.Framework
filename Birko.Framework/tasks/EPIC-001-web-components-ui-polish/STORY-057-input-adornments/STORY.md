---
id: STORY-057
parent: EPIC-001
status: planned
created: 2026-09-24
---

# Input adornments — prefix / suffix slots on text-like controls

## User story

As a developer using a Birko text-like control, I want to put something **inside** the field's box, before or
after the text (an icon, a unit, a × clear button), and have the control keep its text clear of it. Then I
don't have to overlay my own element and fight the shadow DOM for padding.

## Why now

- **A consumer asked for it.** Presenter's landing page (Presenter TASK-006) ships a "Clear" button *beside*
  its `b-input`, which appears and disappears as the box goes between empty and not, shifting the row. The
  owner picked "× inside the field" over the alternatives. `b-input` cannot do that today: no slot, and a
  consumer cannot pad the inner `<input>` from outside the shadow root, so an overlaid × would sit on top of
  long URLs. Presenter TASK-012 waits on this story.
- **The pattern already exists, hand-rolled, once.** `b-search-input` overlays its icon and × on its own
  input and carries a comment about getting the inset right: `formControlSheet`'s size variants set the
  `padding` **shorthand**, which resets `padding-left/right`, so the inset has to be repeated per size or
  text runs under the icon at `size="sm"`. A second copy of that workaround in every control that wants an
  adornment is the drift this story exists to prevent.
- `b-select` already has its own `clearable`. The shared version should agree with it on name and
  behaviour rather than grow a second vocabulary.

## Behaviour

- Named slots `prefix` and `suffix` inside the control's box. Slotted content is vertically centred, and
  the inner control's inline padding grows to clear it at every `size`.
- A `clearable` attribute is the built-in suffix: a × shown only while the control has a value. It has a
  localised `aria-label`, clears the value, emits the control's normal `change`/`input` events, and returns
  focus to the field.
- No layout shift: adornments live inside the box, so the control's outer size never changes when one
  appears.

## Tasks

| Task | What |
|---|---|
| [[TASK-484]] | `b-input`: `prefix` / `suffix` slots + `clearable` (the capability; Presenter's consumer need) |
| [[TASK-485]] | Extend the adornment slots to the other text-like controls, and move `b-search-input` onto them |
