---
id: TASK-035
parent: STORY-023
status: review
priority: P3
assignee: ai
created: 2026-06-15
depends-on: [TASK-001]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Make form controls form-associated via ElementInternals

## Context

The `b-*` form inputs are Shadow DOM custom elements that do **not** currently participate in native `<form>` submission — they rely on `b-form` collecting values programmatically, or on consumers reading values via the components' JS API. An accessibility audit (2026-06-15) confirmed there is no use of `ElementInternals` / `attachInternals()` / `formAssociated` anywhere in `Birko.Web.Components`.

The same audit already shipped the SR/ARIA validation layer (a stable `BaseComponent.uid`, `fieldAria()` + `renderError()` helpers, `aria-invalid` / `aria-required` / `aria-describedby` + `role="alert"` error live-regions across all ~12 inputs). This task adds the **form-participation** layer on top of that — it is a feature, not an accessibility fix.

## Acceptance criteria

- [x] `BaseComponent` (or an opt-in mixin/subclass) supports form association: exposes `attachInternals()` result, documents the `static formAssociated = true` requirement on subclasses
      → **`FormControlComponent extends BaseComponent`** in `Birko.Web.Core/src/base/form-control-component.ts`.
      Not on `BaseComponent` itself: `formAssociated` is read per class at definition time and
      `attachInternals()` is constructor-only/once, so that would make `b-card` / `b-modal` / `b-table`
      submittable listed elements, `:invalid`-matchable and `<fieldset disabled>`-sensitive. Opt-in.
- [x] `b-input` converted first as the reference implementation: `setFormValue()` on change, `setValidity()` mirroring the `error` + `required` attributes, `formResetCallback` / `formDisabledCallback` handled
- [x] Remaining inputs converted — **all 12** from the original list: `b-textarea`, `b-select` (native +
      combo), `b-multi-select`, `b-tag-input`, `b-date-picker` (custom + native), `b-datetime-picker`,
      `b-time`, `b-range`, `b-color-picker`, `b-date-range-picker`, `b-markdown-editor`.
- [x] **Plus the three toggles the AC omitted** — `b-checkbox`, `b-switch`, `b-radio` (15 total). Scoped in
      with a consumer audit first; see "Toggle controls" below.
- [x] Native `<form>` submit includes the control values in `FormData`; `form.reportValidity()` reflects component errors
      (`checkValidity`/`validationMessage`/`validity` asserted on all 12; `reportValidity` is the same
      `ElementInternals` call — the bubble's *position* is the human test-plan item below.)
- [x] `b-form` behaviour verified unchanged (regression pass) — asserted in the harness, plus the
      pre-existing `backport-smoke` 73/73 and the 66-component gallery render.
- [x] Tests for `setFormValue` / `setValidity` on at least three representative components — 61 checks
      across all 12 in the new Playground `form-assoc-smoke` harness.
- [x] `Birko.Web.Components/CLAUDE.md` updated with the form-association convention — new
      "### Form-association convention (value-bearing controls)" (6 rules + the validity-precedence rule)
      and a Recent Updates entry. Also: `Birko.Web.Core/CLAUDE.md` documents `FormControlComponent` and its
      subclass contract; `README.md` + `API.md` gained a "Form participation" section with the
      submitted-shape table; and the three places that previously documented the *opposite*
      (`README.md`, `API.md`, `ACCESSIBILITY.md`) are corrected.

## Decisions taken while implementing

**Validity is *borrowed*, not reimplemented.** `syncFormState()` mirrors, in precedence order: (1) the
`error` attribute → `customError` with that message (the app's verdict — what `b-form` and page-level
validation set — must beat the browser's); (2) otherwise the **inner native control's own `validity`,
verbatim**. That last point is the reason this task pays off more than expected: `type="url"`, `min`,
`max`, `step`, `pattern` and `required` on the inner `<input>` were previously invisible to any wrapping
form, and are now enforced by it for free rather than being re-derived here and drifting from the
browser's rules. Controls with no usable native primitive (searchable `b-select`, `b-multi-select`,
`b-tag-input`, and the pickers, whose inner input holds a *formatted display string* rather than the
value) return `undefined` from `validationSource()` and get a generic `required`-only check in the base.

**Multi-value controls submit N entries, not a joined string** (decided with the user). `b-multi-select`
and `b-tag-input` override `formValue()` → `multiFormValue()`, giving one `FormData` entry per value
under the control's `name` — the native `<select multiple>` / checkbox-group shape, which ASP.NET Core
(every backend in this family) binds straight to `string[]`. It is also non-lossy: the comma-joined form
breaks on values containing a comma, and there is a test for exactly that. `value` / `inputValue` still
return the joined string, so `b-form` and every existing consumer are untouched — form participation is
an **additional** surface. `b-range` (range mode) and `b-date-range-picker` are *not* a native question
(no native dual control exists) — see "Decisions taken with the user" for how those were resolved.

**Empty means absent.** `formValue()` returns `null` rather than `''` when empty, so an untouched control
contributes no `FormData` entry at all — native behaviour, and the difference between a server binding
an empty list and binding `[""]`.

**`formDisabledCallback` must NOT write the host's own `disabled` attribute.** An element's disabled
state is the union of its own attribute and its ancestors', so reflecting the fieldset's state onto the
attribute makes the element *self*-disabled: re-enabling the fieldset leaves the computed state
unchanged, the callback never fires with `false`, and the control is stuck disabled permanently. Found by
the harness ("re-enabling propagates back" failed). The flag is now held separately and folded into an
overridden `boolAttr('disabled')`, so every component's existing `this.boolAttr('disabled')` in
`render()` honours it — inner `disabled`, `.disabled` classes and styling all follow with no
per-component work — and a public `disabled` getter exposes the union.

**Where the sync call goes.** `syncFormState()` belongs in `onUpdated()` **before any early return**, not
only in the `change` emit path: `setSelected()` / `setTags()` / `setOptions()` and panel clicks change
state and re-render *without* emitting. Getting this wrong is silent — the control simply never
registers a value. Caught by the harness on `b-multi-select` and `b-tag-input` (4 failures).

## Decisions taken with the user

- **Two-value controls → suffixed names.** `b-range` (`mode="range"`) submits `name-from` / `name-to`;
  `b-date-range-picker` submits `name-start` / `name-end`. Two values in one control has no native
  analogue, so rather than invent a delimiter (today's `b-range` `value` is a JSON blob, which no server
  form-binder reads) it submits two ordinary fields. `b-range` in single mode still submits one plain
  value under `name`. Both keep their existing `value` strings for back-compat.
- **`b-color-picker` → base hex.** `#rrggbb`, alpha byte dropped even in `alpha` mode; `el.value` still
  returns `#rrggbbaa` for anyone who needs the opacity.
- **`b-markdown-editor` → the markdown source**, never the rendered preview HTML.

## Toggle controls (`b-checkbox` / `b-switch` / `b-radio`)

Absent from the original AC, scoped in after a consumer audit that changed the design:

**Usage** — `b-radio`: **0** references anywhere (no direct use, and `type: 'radio'` appears in no `b-form`
schema). `b-checkbox`: 4 references, one live instance (Reps `#f-perside`). `b-switch`: 43 direct, plus the
bulk of 71 schema-driven `type:'switch'|'checkbox'` fields — almost all Symbio, almost all inside `b-form`.

**Every read path uses `.checked`; nothing reads `.value`.** `b-form._getFieldValue` (b-form.ts:924-926),
Symbio `modules-page.ts:194` / `notification-preferences-page.ts:204,234`, gameshow `components.ts:554`,
Reps `workout-exercise-edit-page.ts:177`. That made the `'true'`/`'false'` `.value` dead API surface and
freed the design: it is **left untouched**, and only `formValue()` follows native semantics. Realigning
`.value` would have been churn with nonzero risk and no consumer benefit.

**Submit semantics** — the `value` attribute (default `on`) **only when checked**, `null` otherwise. An
unchecked box must be *absent*: `bool` model binding reads absence as false, so a literal `name=false`
arrives as a present truthy string and mis-binds. `required` is forwarded to the inner input on
checkbox/switch, so the browser's own "must be checked" rule (and its ARIA) applies.

**Two things that turned out easier / harder than expected:**
- `b-radio` needs **no submission coordinator** — members share a `name` and only the checked one returns a
  value, so the form gets exactly one entry per group for free. (I had flagged this as "the real work".)
- But the existing `b-radio-change` sibling listener unchecks the previous member **without re-rendering
  it**, so that path needs its own `syncFormState()`; without it the group submits **two** entries. Caught
  by the harness.
- `required` on `b-radio` is **unsupported** and documented as such: it is a group property, and evaluating
  it per element marks every unchecked member invalid — one bubble per radio for one logical field. New
  `supportsRequiredValidation` hook on the base opts out.

**Live impact: zero.** Symbio has no native `<form>` (inert there); Reps' single `b-checkbox` has no `name`,
so it submits nothing either way; gameshow's switch is a filter outside any form.

## Reset model (fixed after review)

The first cut of `formResetCallback()` snapshotted the `value` attribute and restored by assigning `value`.
Correct for value-backed controls, **silently wrong** for two shapes — and the harness only tested reset on
`b-input`, so nothing caught it:

- **Toggles** — native reset restores *checkedness*. The generic path fed the `value` attribute through the
  `value` setter (which reads `'true'`/`'1'`), so `<b-checkbox value="yes" checked>` came back **unchecked**.
- **`b-multi-select`** — its selection comes from `setSelected()` and it has no `value` attribute at all, so
  reset cleared it instead of restoring anything.

Replaced the single `_initialValue` string with an overridable **`captureInitialState()` /
`restoreInitialState()`** pair holding an `unknown` (string, boolean, or `string[]` per control), plus a
public **`resetFormBaseline()`**. The baseline is taken at first sync = markup-declared state, matching
native (which ignores script-assigned values on reset), so a control populated imperatively after mount must
re-baseline or reset returns it to empty. Overridden in `b-checkbox` / `b-switch` / `b-radio` (checkedness)
and `b-multi-select` / `b-tag-input` (their list).

Coverage: 15 new reset checks spanning every state shape — string, checkedness in both directions, radio
group, imperative list with an explicit baseline, tag list, and both two-value controls. Verified as real
regressions by reverting the `b-checkbox` override and watching exactly those two checks fail (95/97), then
restoring it.

## Hardening after review

Two rough edges in the base, both closed:

- **`value` is now `abstract get/set value(): string`.** `formValue()` and `restoreInitialState()` both need
  it, and they previously reached it through a structural cast (`(this as unknown as { value }).value`) — so
  a subclass that forgot the accessor compiled cleanly and failed at runtime. Verified the guarantee by
  compiling a subclass without `value`: `TS2515: Non-abstract class 'BadControl' does not implement inherited
  abstract member value`. All 15 existing controls satisfy it unchanged, including `b-date-range-picker`,
  whose setter is wider than `string` (setter parameters are bivariant, so widening is allowed).
- **`formValue()` is called once per sync**, not twice. It is an overridable that may build a `FormData` or
  walk a selection, and the validity pass needs the same answer — so it is computed once in
  `syncFormState()` and threaded into `_syncValidity(value)`. Removes the wasted call and the chance of the
  two invocations disagreeing.

## Found, not fixed (pre-existing, unrelated)

`b-form`'s attribute builder (`b-form.ts:543`) emits `value="true"` for a `{type:'switch'|'checkbox',
value:true}` field but has no checkbox/switch case that turns a truthy `field.value` into the `checked`
attribute — so **schema-level `value: true` has never rendered a toggle as checked**. `setValues()` works
correctly (it routes through `_setFieldValue`, which does set `checked`). Deliberately not changed here:
it predates this task, and fixing it would make previously-unchecked toggles start rendering checked across
71 schema usages — a visible behaviour change that deserves its own decision. Worth a task.

## Consumer impact (audited before the toggle work)

- **No impact:** Symbio, DraCode, Latent, gameshow-app, BardStudio, Affiliate — **zero** native `<form>`
  elements; everything goes through `b-form` / `base-crud-page`, whose programmatic collection is asserted
  unchanged. Presenter's one `<form>` cannot submit (no light-DOM submit control; Enter is handled in JS),
  so its guard remains the active layer.
- **Behaviour change — Reps (WorkoutTracker), 7 pages:** `<form>` + real `<button type="submit">` + `b-input`
  with `required`/`min`/`step`. An invalid control now **suppresses the submit event** (verified in the
  harness), so the page's handler — and therefore its localised Slovak `#err` message — never runs; the user
  sees the browser's native bubble instead. `tests/ui-e2e/progress-log-today.spec.ts:187` fails as a direct
  result, and its own comment documents the assumption ("b-* controls are not form-associated; see
  TASK-090") that this task invalidates. `progress-page`'s `step="0.1"` fields add a sharper edge: a *stored*
  value violating the step makes the record un-saveable until edited.
  **Recommended fix: `novalidate` on those 7 forms** — verified in the harness to restore the submit event
  while keeping `FormData` / `reset()` / `<fieldset disabled>`. It is a Reps-side, product-UX decision
  (native bubble vs their localised messages), so it is deliberately not applied here.

## Verification

- `form-assoc-smoke` (new, `Birko.Web.Playground/src/form-assoc-smoke.ts`): **97/97** across all 15
  controls — formAssociated +
  `form` resolution; value in `FormData` from a seeded attribute, a property set and a real user edit;
  empty contributes nothing; `required` blocks the form (the exact Presenter hole); `type=url` /
  `min` / `step` enforced through the form; `error` attribute wins as `customError` and clears; `reset()`
  restores; `<fieldset disabled>` propagates both ways without touching the host attribute; multi-entry
  shape incl. a comma-containing value; searchable `b-select` submits the value not the label; pickers
  submit ISO not display text; native `b-date-picker` mirrors `min`; suffixed names for both two-value
  controls (and that range mode emits no bare `name` entry, while single mode emits no suffixes);
  `b-color-picker` submits base hex while `.value` keeps the alpha byte; `b-markdown-editor` submits
  source and never rendered HTML; a sweep asserting all 12 are `formAssociated` **and** that the three
  toggle controls are too; toggles submit `on` only when checked and nothing when unchecked, honour an
  explicit `value`, follow a real user click both ways, and enforce `required`; a `b-radio` group submits
  exactly one entry and still does so after switching members; `b-radio` `required` does not invalidate
  unchecked members; an invalid control suppresses the submit event while `novalidate` restores it;
  `b-form` regressions (boolean toggles via `setValues`, required flagging, value collection); and a
  control outside any form still works.
- `bare-smoke` **64/64** and `backport-smoke` **73/73** unchanged; 66 gallery components render, none
  empty, no page errors. `tsc --noEmit` clean on `Birko.Web.Components` and `Birko.Web.Playground`.

## Out of scope

- New ARIA / screen-reader semantics (already delivered in the 2026-06-15 accessibility pass)
- Cross-shadow tooltip accessible-name association (documented as a known Shadow DOM limitation)

## Human test plan

_For behaviour that unit/AI tests can't fully cover (UI/UX, edge cases, system integrations, manual verification). A human or agent runs these steps at `/tasks close` time and when `/feature review` checks the feature._

- [ ] Put converted controls inside a native `<form>`, submit it, and inspect the `FormData` (or server payload) — confirm each control's value is present under its `name`
- [ ] Mark a control invalid (set `error` / leave a `required` empty) and call `form.reportValidity()` — confirm the browser-native validation bubble appears anchored to the component
- [ ] Trigger a native form **reset** and confirm `formResetCallback` restores each control to its initial value
- [ ] Disable the form (`<fieldset disabled>`) and confirm `formDisabledCallback` propagates the disabled state into each control
- [ ] Regression: run an existing `b-form`-based screen and confirm value collection / validation behaves exactly as before (this layer must not break the programmatic path)
- [ ] Verify in Chromium + Firefox + WebKit (Safari) — `ElementInternals` / form-association support and validation-bubble behaviour differ across engines
