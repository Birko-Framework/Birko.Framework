---
id: TASK-132
parent: STORY-023
feature: FEATURE-001
status: todo
priority: P2
assignee: ai
created: 2026-08-01
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `b-form`: `required` on a checkbox / switch is inert — an unchecked toggle counts as filled

## Context

Found while making `b-form.validate()` surface a control's own validity
(`Birko.Web.Components` commit `9402219`, playground checks in `16a72d8`). It is **not** that fix's
regression — it predates it — but it is the reason that fix deliberately excludes the `valueMissing`
flag, and it was discovered by writing the check that proves the exclusion is a decision rather than an
oversight.

Two mechanisms disagree about the same field, and the weaker one wins:

- **`b-form`'s own emptiness test** (`_validateField`, `src/inputs/b-form.ts`) is
  `value === undefined || value === null || value === '' || (Array.isArray(value) && value.length === 0)`.
  For a `checkbox` / `switch` field, `_getFieldValue` returns a **boolean**, so `false` is not empty —
  the `required` rule passes and `_validateField` returns before anything else runs.
- **The control** forwards `required` to its inner native checkbox, so it reports
  `validity.valueMissing === true` and `checkValidity() === false`. Measured (headless, playground):
  `unchecked checkbox checkValidity=false valueMissing=true`, same for `switch`, while
  `validate()` returned `valid: true`.

So `required: true` on a `checkbox` or `switch` field in a `b-form` schema does **nothing**. The classic
shape of this is a consent / "I agree" gate that submits happily unticked.

`validate()` cannot fix this by adopting `valueMissing`, because `required` returns before the control is
ever consulted — the emptiness test itself is what has to learn that a toggle's empty value is `false`.

### The blast radius is zero today, which is the argument for doing it now

Swept all 16 local consumers: **no consumer sets `required` on a `checkbox` / `switch` schema field.**
Symbio has 52 files with toggle fields and not one is required; the only hit anywhere is the smoke check
written to pin this gap. So closing it changes no shipped behaviour — the caution recorded in
`API.md` ("closing it would start blocking forms that have always submitted") is true of unknown
out-of-tree consumers and **measurably false of every consumer in this workspace**. That window will not
stay open: the first consumer to put a consent box in a `b-form` inherits a silent hole.

## Acceptance criteria

- [ ] A `required` `checkbox` / `switch` field fails `validate()` while unchecked, and passes once checked.
- [ ] The error lands on that field (the `error` attribute, via `_applyErrors()`), like every other field's.
- [ ] The message comes from the same place as every other `required` message (`common.required` via `fmt`,
      honouring a rule-level `message` override) — not from the browser's `validationMessage`, so a form's
      messages stay consistent and translatable.
- [ ] Emptiness is decided **per field type**, not by special-casing `false` globally — `false` is a
      legitimate collected value for a non-required toggle and must keep round-tripping through
      `getValues()` / `data` unchanged.
- [ ] `b-form`'s existing `required` behaviour for every other field type is untouched (blank string, empty
      array, `undefined`) — pinned by the checks already in the suite.
- [ ] The now-obsolete "known gap" notes are removed rather than left contradicting the code:
      `src/inputs/b-form.ts` (`_controlVerdict`'s `valueMissing` paragraph), `API.md` § *What `validate()`
      takes from the controls themselves*, and the two pinning checks in `backport-smoke.ts` are inverted
      to assert the fix.
- [ ] Playground verifier green, with the inverted checks falsified by reverting the fix.

## Out of scope

- Adopting `valueMissing` (or any other flag) into `_controlVerdict`'s whitelist — see [[TASK-134]]. This
  task fixes the emptiness test; it does not widen what `validate()` reads off the controls.
- `b-radio`'s `required`, which is a different defect with a different cause — see [[TASK-133]].
- The control-level story: `b-checkbox` / `b-switch` are already correct (they report `valueMissing`
  faithfully) and `b-radio` deliberately sets `supportsRequiredValidation = false`. Nothing changes there.

## Human test plan

N/A — covered by automated tests (headless playground checks; a toggle's checked state is not a visual
judgement).

## Cross-links

- The change that surfaced it: `Birko.Web.Components` `9402219`, `Birko.Web.Playground` `16a72d8`
- The pinning checks: `Birko.Web.Playground/src/backport-smoke.ts` — "premise: an unchecked required
  checkbox reports ITSELF invalid" / "…and validate() still passes it — a known b-form gap, left alone
  here on purpose"
- The convention it sits inside: [[TASK-035]] (`FormControlComponent`), `Birko.Web.Components/CLAUDE.md`
  § *Form-association convention* — "`required` is forwarded to the inner input on checkbox/switch"
