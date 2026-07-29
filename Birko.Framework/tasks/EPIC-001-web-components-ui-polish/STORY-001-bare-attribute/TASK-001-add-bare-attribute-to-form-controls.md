---
id: TASK-001
parent: STORY-001
status: review
priority: P2
assignee: ai
created: 2026-05-28
depends-on: []
blocks: [TASK-002]
pr: null
github-issue: null
jira-key: null
---

# Add `bare` attribute to all form controls

## Context

Eight components need a `bare` boolean attribute that strips the `.field` wrapper — no label slot, no error-message row below. Intended for inline use cases (toolbars, table cells, floating action bars) where the outer stacked chrome is unwanted. Pairs with `size="sm"` for dense layouts.

## Acceptance criteria

- [x] `b-input` supports `bare="true"`; rendering skips `.field` / label / error span
- [x] Same for `b-select` (both native and searchable paths), `b-multi-select`, `b-textarea`,
      `b-tag-input`, `b-date-picker`, `b-datetime-picker`. **`b-search-input` needed no change** — it
      renders no `.field` / label / error at all (only `.search-wrap`, which positions the icon and
      clear button), so it is already bare. Given a no-op `bare` attribute would have been misleading
      API surface, it is documented as already-bare and asserted as such in the harness instead.
      **Extended later** — see TASK-091's follow-up: `b-time`, `b-date-range-picker`, `b-range`,
      `b-color-picker` and `b-markdown-editor` were absent from this list, still hand-rolled the chrome,
      and were migrated onto `renderField` in that pass. `bare` now covers **twelve** controls; only
      `b-file-upload` / `b-option-group` remain hand-rolled (no error row to strip).
- [x] Error / label state still surfaced via attributes (just not rendered by the component) — see the
      decision below; this turned out to be the only non-mechanical part of the task.
- [x] Tests added for at least three representative components — now **99 checks across all twelve**, in
      the new Playground `bare-smoke` harness (64 across seven at the time this task closed).
- [x] `Birko.Web.Components/CLAUDE.md` updated with the `bare` convention (new
      "### `bare` attribute convention (form controls)" + a Recent Updates entry). `README.md` and
      `API.md` also document the attribute for consumers.

## Decisions taken while implementing

**A shared `renderField()` rather than seven `bare ? … : …` branches.** Added to
`src/inputs/label-hint.ts`, which already owned `renderLabel` / `renderError` / `fieldAria`. Each
component now passes its control markup in and the helper owns the wrapper/label/error decision, so a
future control gets it right by construction. Everything belonging to the *control* — a `<datalist>`,
a `.dropdown` / `.dp-panel` popover, a `.combo` / `.container` wrapper — stays inside the control, since
those popovers are resolved by selector and positioned against their trigger and would break if they
were treated as chrome.

**How "error / label state still surfaced" was resolved (the AC's fuzziest line).** Removing the chrome
removes the `<label>` that gave the control its accessible name *and* the error span that
`aria-describedby` pointed at — so bare mode would have silently shipped an unnamed control with a
dangling IDREF. `fieldAria()` therefore gained `bare` + `label` and rebuilds both from attributes:
`aria-label` from `label`, and the message as `title` (hover text for sighted users, accessible
description for AT) instead of `aria-describedby`. The `has-error` border is untouched. So a bare
control still shows *and* announces its error state; it simply has nowhere to print the message, which
is the consuming page's job. Chromed mode is byte-for-byte unchanged — no `aria-label`, no `title`,
still linked to the error span — and that is asserted, so the default rendering cannot regress.

**Where the tests live.** `Birko.Web.Components` ships no unit runner (`package.json` has no `scripts`),
so this AC had no home. Rather than stand one up, the change follows the project's existing route:
a `?smoke=1`-gated harness in `Birko.Web.Playground` (`src/bare-smoke.ts`, alongside the EPIC-002
`backport-smoke.ts`), executed in a real browser by that repo's `verify.mjs`. **This is the same open
question TASK-035 has** — if a unit runner is ever added, these checks are the obvious first migration.

## Verification

- `bare-smoke`: **99/99** — default chrome intact; bare strips wrapper/label/error on all twelve; error
  state still surfaced; no dangling `aria-describedby`; message on `title`; `aria-label` fallback;
  chromed mode gains neither `aria-label` nor `title`; `bare` reactive via `observedAttributes`;
  value round-trip and DOM-edit reflection in bare mode; datalist reachable from `list=`; the pickers'
  and combos' popovers survive; `b-search-input` already bare; bare measurably shorter than chromed.
- `backport-smoke`: **73/73** (unchanged) — 67 gallery components render, none empty, no page errors.
- `tsc --noEmit` clean on both `Birko.Web.Components` and `Birko.Web.Playground`.
- The gallery gained a live `bare` toggle on all twelve controls, so the human test plan below is
  runnable by flipping it in the Inputs section rather than hand-editing markup.

## Out of scope

- Migration of `b-editable-table` cells onto these bare variants (TASK-002)
- Bare variants on non-form components

## Human test plan

_For behaviour that unit/AI tests can't fully cover (UI/UX, edge cases, system integrations, manual verification). A human or agent runs these steps at `/tasks close` time and when `/feature review` checks the feature._

- [ ] In a browser, drop a `<b-input bare size="sm">` into a toolbar/flex row and confirm it renders with **no** label slot, no error-message row, and no `.field` stacked chrome — just the control
- [ ] Place a bare control inside a table cell next to a `size="sm"` sibling and confirm baseline/height alignment (no extra vertical space from a collapsed wrapper)
- [ ] Set the `error` attribute on a bare control and confirm the error state is still reflected on the host (e.g. `aria-invalid` / border) even though no message row is drawn
- [ ] Spot-check at least one non-converted (full-chrome) instance of the same component to confirm default rendering is unchanged
- [ ] Verify in Chromium + Firefox (Shadow DOM slot rendering differs between engines)
