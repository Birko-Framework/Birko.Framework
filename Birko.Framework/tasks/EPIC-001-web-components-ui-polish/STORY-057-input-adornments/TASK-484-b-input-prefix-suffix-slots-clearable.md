---
id: TASK-484
parent: STORY-057
feature: FEATURE-001
status: done
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

- [x] `<slot name="prefix">` and `<slot name="suffix">` render inside the field box, vertically centred
- [x] The inner `<input>`'s inline-start / inline-end padding grows to clear slotted content, correct at every `size` (the shorthand trap above), and only when the slot is non-empty (`slotchange`)
- [x] `clearable` attribute: a × in the suffix position, shown only while the value is non-empty. It has a localised `aria-label` (new `bwc.*` key in every locale the package ships), clears the value, emits the same `change`/`input` a keystroke does, and returns focus to the input
- [x] `clearable` and a slotted `suffix` coexist (× sits inside the slotted content, or the order is documented)
- [x] No outer size change when the × appears or disappears (no layout shift)
- [x] Form association unaffected: a cleared value reaches `ElementInternals.setFormValue` / `b-form` as empty (STORY-023)
- [x] `disabled` hides the ×; `bare` keeps working
- [x] `API.md` documents the slots and `clearable`; `ACCESSIBILITY.md` notes the × button's label and focus behaviour
- [x] Tests in `Birko.Web.Testing` (or the package's existing test home) for clear → value/event/focus, and padding with/without slot content

## Out of scope

- Other controls, and moving `b-search-input` onto the slots — [[TASK-485]]
- Consumer changes — Presenter TASK-012 switches its landing page over once this lands

## Human test plan

The Playground's Input card now has a `clearable` toggle, next to `size` / `disabled` / `bare`.

- [x] Playground: `b-input clearable` at `size="sm"`, default and `lg`. Type a long value and check the text never runs under the × — owner, 2026-10-09
- [x] Keyboard: Tab reaches the ×, Enter/Space clears it, focus lands back in the field — owner, 2026-10-09
- [x] Screen reader announces the × with its label, EN: Windows Narrator reads "Clear, button" in the Playground — owner, 2026-10-09
- [x] Screen reader, SK: Narrator in Slovak reads the × label in Presenter — owner, 2026-10-09. The package ships only `en.json`, so SK means a consumer registering `bwc.input.clear`
      (Presenter's TASK-012), or `label-clear` on the element. Checked together with the Presenter sign-off below
- [x] Light and dark theme: the × uses `--b-*` tokens and is legible in both — owner, 2026-10-09
- [x] Sign-off in Presenter: TASK-012 replaces its button beside the field with `clearable` — owner, 2026-10-09

## Implementation plan

Executed inline, without a separate `/tasks plan` pass. See the Outcome section.

## Outcome

**What was built.** `b-input` (`Web/Birko.Web.Components/src/inputs/b-input.ts`) has `prefix` / `suffix` slots
and `clearable` (+ `label-clear`, i18n `bwc.input.clear`). Adornments overlay the input inside its box. The input's
inline padding is set as an **inline style** from the measured adornment width, which beats the size variants'
`padding` shorthand at every size. The × comes after the slotted suffix (documented order). It hides with
`visibility`, so it keeps its space while empty and nothing moves. A clear dispatches the native `input`
event, so value, `change`, the form value and validity all go the same way as a keystroke. Docs: `API.md`, `ACCESSIBILITY.md`,
and the package's `CLAUDE.md` (table row + Recent Updates).

**Proof.** `Consumers/Birko.Web.Playground/src/input-adornment-smoke.ts`, registered in `app.ts` and in
`verify.mjs`'s expected-suite list, has 50 checks. They measure geometry at sm/md/lg (text clears prefix and suffix,
vertical centring, the × inside the box, no size or padding change as it appears), `slotchange` growing and
releasing the padding, and clearing (value, one `change` with `''`, one composed native `input`, focus back on
the input, FormData entry dropped, no resurrection after a re-render). They also cover `label-clear`, `disabled`, `bare`, and
a default input staying untouched. `node verify.mjs`: every suite green, input-adornment-smoke 50/50, no EMPTY
component. `device-fix-check.mjs` 68/68. `tsc --noEmit` clean. Three mutations each turned the suite red:
no inline padding → 7 fail at all three sizes; clear without the input event → 4 fail; `display: none` instead of
`visibility` → 7 fail, including the layout-shift check.

**Judgement calls.**
- *A cleared field has **no** FormData entry, not `''`.* That is the existing contract for any empty `b-input`
  (form-assoc-smoke: "empty control contributes no FormData entry"). The criterion's "reaches the form as empty" is
  met in that sense. My first assertion expected `''` and was wrong.
- *No layout read while rendering.* The first cut measured with `getBoundingClientRect()` in `onUpdated`. That
  blocked the Playground's main thread, and `verify.mjs` timed out on navigation. Found by stashing the change:
  baseline green, component alone red, the measurement lines alone red. Widths now come only from a
  `ResizeObserver`, which exists only while a slot has content or `clearable` is set, so a plain `b-input` measures
  nothing. Recorded in the package's CLAUDE.md because `b-input bare` is the planned editable-table cell.
- *New key `bwc.input.clear` ("Clear")* rather than reusing `bwc.common.clear`, as the criterion asks.
  `b-select` keeps its own.

**Found at sign-off, not this task's defect.** Presenter's URL field has no `label`, so Narrator announces the
placeholder (`https://example.com/deck.md`) as the field's name, ahead of the typed value. Chrome's accessibility
tree confirms `b-input` exposes the value correctly (name = placeholder, value = `abc`; with `label`, name = the
label). The fix belongs in Presenter: give the field a `label`. `b-input` has no way to give a field an
accessible name without a visible label that keeps the error row; `bare` removes that row.
