---
id: TASK-484
parent: STORY-057
feature: FEATURE-001
status: todo
priority: P2
assignee: ai
created: 2026-09-24
depends-on: []
blocks: [TASK-485]
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-input: `prefix` / `suffix` slots + `clearable`

## Context

`b-input` (`Birko.Web.Components/src/inputs/b-input.ts`) renders its `<input>` in shadow DOM with no slot
and no `::part`, so a consumer cannot put anything inside the field's box or pad the inner input to make
room. Presenter's landing page needs a × clear inside its URL field (Presenter TASK-012, blocked on this).
Its current workaround, a button beside the field, shifts the row as it appears and disappears.

Prior art to reuse, not copy: `b-search-input` already overlays an icon and a × on its input. Its comment
records the trap that matters here. `formControlSheet`'s size variants set the `padding` **shorthand**,
which resets `padding-left/right`, so the inset must be applied per size, or text runs under the adornment
at `size="sm"`. `b-select` already exposes `clearable`; match its name and semantics.

## Acceptance criteria

- [ ] `<slot name="prefix">` and `<slot name="suffix">` render inside the field box, vertically centred
- [ ] The inner `<input>`'s inline-start / inline-end padding grows to clear slotted content, correct at every `size` (the shorthand trap above), and only when the slot is non-empty (`slotchange`)
- [ ] `clearable` attribute: a × in the suffix position, shown only while the value is non-empty. It has a localised `aria-label` (new `bwc.*` key in every locale the package ships), clears the value, emits the same `change`/`input` a keystroke does, and returns focus to the input
- [ ] `clearable` and a slotted `suffix` coexist (× sits inside the slotted content, or the order is documented)
- [ ] No outer size change when the × appears or disappears (no layout shift)
- [ ] Form association unaffected: a cleared value reaches `ElementInternals.setFormValue` / `b-form` as empty (STORY-023)
- [ ] `disabled` hides the ×; `bare` keeps working
- [ ] `API.md` documents the slots and `clearable`; `ACCESSIBILITY.md` notes the × button's label and focus behaviour
- [ ] Tests in `Birko.Web.Testing` (or the package's existing test home) for clear → value/event/focus, and padding with/without slot content

## Out of scope

- Other controls, and moving `b-search-input` onto the slots — [[TASK-485]]
- Consumer changes — Presenter TASK-012 switches its landing page over once this lands

## Human test plan

_To be finalised at `/tasks plan TASK-484`. Expected minimum:_

- [ ] Playground: `b-input clearable` at `size="sm"`, default and `lg`. Type a long value and check the text never runs under the ×
- [ ] Keyboard: Tab reaches the ×, Enter/Space clears it, focus lands back in the field
- [ ] Screen reader announces the × with its label, in EN and SK
- [ ] Light and dark theme: the × uses `--b-*` tokens and is legible in both

## Implementation plan

_Populated by `/tasks plan TASK-484` — leave empty until then._
