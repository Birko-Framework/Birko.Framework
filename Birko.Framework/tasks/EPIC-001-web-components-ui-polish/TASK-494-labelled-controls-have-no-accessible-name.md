---
id: TASK-494
parent: EPIC-001
feature: FEATURE-001
status: done
priority: P1
assignee: ai
created: 2026-09-26
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A labelled `b-*` control has no accessible name: its `<label>` labels nothing

## Context

Found by the owner on 2026-09-26 while running [[TASK-488]]'s screen-reader step. The reader announced the
Menu group field as **"header"** (its current value), not **"Menu group"**.

**Measured.** This is Chrome's computed accessibility tree (puppeteer `accessibility.snapshot`) for plain,
non-bare controls:

| Control | Computed name |
|---|---|
| `<b-input label="Weight">` | `""` |
| `<b-textarea label="Notes">` | `""` |
| `<b-select label="Status">` (native) | `""` |
| `<b-select searchable>` in a `b-form` "Menu group" field | `"header"`, from its placeholder, which holds the selected label |

**Mechanism.** `renderField` (`Birko.Web.Components/src/inputs/label-hint.ts`) renders
`renderLabel()` → `<label>Weight</label>`, with **no `for`**, as a *sibling* of the control. A label that
neither wraps its control nor names it with `for` associates with nothing. `fieldAria` sets `aria-label`
only in `bare` mode, where no `<label>` is rendered. So every non-bare control is nameless and falls back
to its placeholder or value. That covers all the controls that pass `label` to `fieldAria`: `b-input`,
`b-textarea`, `b-select` (both modes), `b-multi-select`, `b-tag-input`, `b-date-picker`,
`b-datetime-picker`, `b-date-range-picker` (its labelled inputs), `b-time`, `b-range`,
`b-color-picker`, `b-markdown-editor`. A sighted user sees the label, so nothing looked wrong.

**Fix shape.** Give the rendered `<label>` an id (`${uid}-label`), and have `fieldAria` emit
`aria-labelledby` pointing at it whenever a label is rendered (non-bare, label present). `bare` keeps
`aria-label`. Both elements share the control's shadow root, so an id reference resolves. The required
`*` is marked `aria-hidden`, so the name is "Weight", not "Weight*". Required-ness is already carried by
`required` / `aria-required`.

## Implementation plan

1. `renderLabel(label, hint, required, id?)` emits `id` on the `<label>` when given, and marks the required
   mark `aria-hidden="true"`. `renderField` passes `${uid}-label`.
2. `fieldAria`: non-bare + `label` → `aria-labelledby="${uid}-label"`.
3. Playground smoke `label-name-smoke`: compute each labelled control's accessible name from the real
   accessibility tree, not from attributes. It runs in the page, so it asserts the reference resolves:
   the `aria-labelledby` target exists in the same shadow root and its text is the label. `verify.mjs`
   (headless) additionally checks the Chrome accessibility snapshot. Prove it fails on the pre-fix
   code.

## Acceptance criteria

- [x] Every control listed in Context, rendered non-bare with `label="X"`, has the computed accessible name
      **"X"** (not the placeholder, the value or empty), measured from Chrome's accessibility tree.
- [x] A required control's name carries no `*`, and it is still exposed as required.
- [x] `bare` controls keep their `aria-label` name, unchanged.
- [x] A control with no `label` gets no dangling `aria-labelledby`.
- [x] Suite proven able to fail against the pre-fix `label-hint.ts`; full `verify.mjs` stays green.

## Out of scope

- Controls that do not pass `label` to `fieldAria`. **Verdict (measured 2026-09-26):** `b-option-group`
  is correct (`radiogroup "Plan choice"`), and the `b-color-picker` swatch is named "Pick a color" beside
  its now-labelled text input. `b-date-range-picker` ("Start date" / "End date") and `b-file-upload`
  ("Choose files") drop the field label → [[TASK-495]].
- Combobox roles and arrow keys → [[TASK-489]].

## Human test plan

- [x] Screen reader on `sr-creatable.html` (and a Symbio form): Tabbing into a field announces its label
      first. "Menu group", then the value.

✅ **Run by the owner on 2026-09-26: "all 3 works".** Menu group is announced by its label, then its value;
the Label field is announced as "Label"; the create and created announcements work.

## Progress log

- 2026-09-26 — `label-hint.ts`: `renderLabel` takes an id and hides the required mark from the name;
  `renderField` passes `${uid}-label`; `fieldAria` emits `aria-labelledby` (non-bare) or `aria-label`
  (bare). `b-multi-select`'s focusable container gains `role="combobox"`, because Chromium drops
  `aria-labelledby` on a generic div. It was the one control still nameless after the shared fix, and it
  already carried `aria-expanded` / `aria-controls`, so it is the select-only combobox pattern.
- 2026-09-26 — `Birko.Web.Playground/a11y-name-check.mjs` reads Chromium's computed names. It is a
  standalone script beside `a11y-description-check.mjs`, not an in-page smoke, because a name is only
  observable in the accessibility tree. Result: 16/16 with the fix; 14/16 fail against the pre-fix
  `label-hint.ts` (every control, and the required case computed ""). `verify.mjs` 9/9 suites, 0
  failing; `a11y-description-check` PASS.
- 2026-09-26 — Committed: Web `1b72750` (fix), Playground `28f5b75` (check). Renumbered from TASK-491 before
  commit: a parallel session had minted 491–493 meanwhile.
