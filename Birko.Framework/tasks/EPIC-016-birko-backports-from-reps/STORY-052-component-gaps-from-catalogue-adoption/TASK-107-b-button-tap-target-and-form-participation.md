---
id: TASK-107
parent: STORY-052
feature: null
status: review
priority: P2
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `b-button`: a reachable tap target, and form participation

## Context

Reps tried to adopt `b-button` across **69** hand-rolled buttons and **stopped before writing any code**,
because reading the component first turned up two gaps that would each have shipped as a silent
regression (`Consumers/WorkoutTracker/tasks/…/STORY-010-reps-adopt-cleanups/TASK-102-adopt-b-button.md`,
§ "Blocked (2026-07-30)"). That is the process working: the cost of reading the component was one
afternoon, and the cost of not reading it would have been a broken save path on seven edit pages.

## The form gap was already decided — `b-button` was just left out

The task arrived framed as an open decision between (a) `ElementInternals`, (b) forwarding `type` and
re-dispatching a synthetic submit, and (c) documenting that consumers must wire clicks. **It is not open.**
`Birko.Web.Core` has shipped `FormControlComponent` (`static formAssociated = true` + `attachInternals()`)
since 2026-07-29, and **15 controls already extend it** — `b-input`, `b-select`, `b-textarea`, `b-checkbox`,
`b-radio`, `b-switch`, `b-range`, `b-multi-select`, `b-tag-input`, `b-markdown-editor` and the five pickers.
Option (a) is the catalogue's settled answer; `b-button` was the only member of the family still on
`BaseComponent`.

That omission was reasonable — `FormControlComponent` is built around an abstract `value`, and a button does
not have one — but it is still an omission, and it is why the story reads as an undecided policy question
from the consumer's side. Extending it costs three overrides and buys the whole contract:

- `this.form` resolves the real form owner **across the shadow boundary**, which is the actual defect: an
  element inside a shadow root has no form owner, so `<b-button type="submit">` in a `<form>` did nothing;
- `<fieldset disabled>` propagation, which `b-button` never had;
- `formValue()` returns `null` — a button contributes no `FormData` entry. Native `name`/`value` submitter
  semantics are **not reproducible**: `form.requestSubmit(submitter)` accepts only a native submit button
  belonging to the form, so there is no way to be the submitter. Contributing unconditionally would be worse
  than contributing nothing (the value would be sent even when another button submitted). Documented, not
  faked.

`type="submit"` calls `form.requestSubmit()` rather than `submit()`, so constraint validation runs and the
`submit` event stays cancellable — exactly what a native submit button does.

## `type` defaults to `button`, not to native's `submit` — decided by grep, not by taste

From first principles `submit` is the better default: a component called `b-button` inside a `<form>` should
behave like a `<button>`, and defaulting to `button` *preserves the very class of silent-nothing-happens bug
that raised this ticket*. That is the conclusion I reached and then abandoned, because the codebase disagrees:

| Consumer | `b-button` usage | `<form>` in the app | Effect of a `submit` default |
|---|---|---|---|
| **Presenter** | 5 in one `<form>` | yes — with a `submit` listener | **breaks it** (see below) |
| Symbio | 102 files | **zero `<form>` elements anywhere** | none |
| DraCode / gameshow-app | 10 / 2 files | none | none |
| Reps | none yet | 7 edit pages | n/a — has not adopted |

Presenter's landing page (`src/pages/landing-page.ts:82-86`) has `<form id="form">` whose `submit` listener
calls `_openAdHoc()`, containing `#save` whose own click calls `_save()`. With a native-faithful default, one
tap on Save would run **both** — save the deck *and* navigate to open it. That is a live regression in a
shipped consumer, against a hypothetical benefit for future ones.

So `type="submit"` is opt-in, the opt-in is one attribute, and the deviation from native is documented at the
class, in `API.md` and in `README.md` so the next reader meets the decision rather than the surprise.

## Tap target: a token, not a fourth size rung

No size reached ~44px — default 8px vertical, `sm` 4px, `lg` still 8px (it only widens horizontally). The
consumer could not fix it from outside either: overriding `--b-space-sm` on the host also hijacks the
button's own `gap`, and re-pointing a global token to reshape one component is the anti-pattern the
catalogue exists to remove.

`--b-button-padding-y` / `--b-button-padding-x` now thread through **all three** size rules with today's
values as defaults, so one rule raises every call site instead of an attribute per button — which is what a
69-button sweep actually needs. A `size="touch"` rung was rejected: `size` means overall scale (it changes
font-size too), and "medium but taller" muddles that axis; the token is orthogonal and composes with all
three sizes.

**Rejected: a `pointer: coarse` media query inside the component.** It was the tempting option — it would fix
every consumer with no opt-in — but it re-renders every existing consumer without their asking (a desktop
back-office on a touch-capable laptop reports `coarse`), and it makes a component's size depend on the input
device rather than on its design, which is undebuggable when it goes wrong. Mechanism in the framework,
policy in the app; a phone-first consumer writes one line:

```css
@media (pointer: coarse) { b-button { --b-button-padding-y: var(--b-space-lg); } }
```

### The origin task's premise did not survive measurement

TASK-102 says Reps' `.btn` uses `--b-space-md` and lands at ~44px, implying the same padding fixes
`b-button`. It does not. Measured at the default font scale: **32px** default → **39px** at `--b-space-md` →
**46px** at `--b-space-lg`. `b-button` fixes its font at `--b-text-sm` with `line-height: 1.4`, while Reps'
`.btn` is `font: inherit` — so equal padding buys less height. (Reps' own rules are also mixed: 7 use
`--b-space-md`, 5 use `--b-space-sm`, i.e. the same 8px `b-button` already had, so "every button shrinks" is
too strong.) **Reps should expect to need `--b-space-lg`, not `--b-space-md`.** The numbers are carried in
the smoke check *names*, because a bare assertion against a magic 44 tells you nothing when it fails and the
answer depends on each consumer's font scale.

## Acceptance criteria

- [x] A consumer can reach a ~44px tap target without overriding a global token — `--b-button-padding-y` /
      `--b-button-padding-x`, honoured by the default, `sm` and `lg`.
- [x] `b-button` participates in its owning form: `type="submit"` submits (with validation), `type="reset"`
      resets, `type="button"` (default) does neither.
- [x] The form story is settled **once for the family** — by joining the existing `FormControlComponent`
      convention rather than inventing a second mechanism for buttons.
- [x] The decision and its rationale are written down (this file, the class doc, `API.md`, `README.md`,
      `CLAUDE.md` § Recent Updates) — the next consumer meets it before the code.
- [x] Backwards compatible: default padding per size unchanged, default variant unchanged, and the default
      `type` chosen specifically so no existing in-form consumer changes behaviour.
- [x] Playground verifier green — 152/152 backport-smoke (17 new), all six harnesses, no page errors.
- [x] Reps E2E **95 passed**, unchanged.

## Verification

- **Playground 152/152.** Both directions are asserted, which is what caught the bug below: `type="submit"`
  fires the form's `submit`; the default does **not**, while its own click handler still runs exactly once;
  `reset` resets; `disabled` and `loading` block submission even via a programmatic `.click()` (which
  `pointer-events: none` does not); a required empty field blocks the submit (proving `requestSubmit`, not
  `submit`); and a `type="submit"` button outside any form is a silent no-op rather than a throw — the
  overwhelmingly common case, given Symbio has 102 files of `b-button` and no forms at all.
- **Reps E2E 95/95**, unchanged. Reps has not adopted `b-button`, so this is a pure regression check on the
  shared framework source it compiles.
- **Presenter** — the one consumer with `b-button`s inside a form — still builds (its `tsc --noEmit` has
  **pre-existing, unrelated** failures: its tsconfig cannot resolve `birko-web-core` at all; the esbuild
  build that actually ships succeeds).
- **The listed-element risk was checked, not assumed.** Form association makes `b-button` a submittable
  listed element, so `form.elements` grows and `:invalid` can match. No consumer iterates `form.elements` or
  selects `:invalid` — verified by grep across all six local consumers.

## A bug this found in its own implementation

The click handler was first bound in `onMount()`. Every **positive** submit assertion failed while every
negative one passed — the signature of a dead listener. `BaseComponent._afterRender()` **aborts and replaces**
its listener `AbortController` on every render, so anything registered during `onMount` is detached by the
first re-render; the contract is to bind in `onUpdated()` and let the per-render abort clear the previous
registration. Worth knowing beyond this component, and now recorded in `Birko.Web.Components/CLAUDE.md`.

## Findings for others (not fixed here)

`Presenter/src/Presenter.Web/src/pages/landing-page.ts` carries two comments that TASK-035 made stale on
2026-07-29: line ~168 and line ~213 both say `b-input` "is not a form-associated custom element … (framework
gap, tracked as Birko.Framework TASK-035)". It is now. Its explicit guard and its Enter-key handler still
work and are still needed (a form-associated custom element does **not** provide implicit submission — that
needs a native text control), so nothing is broken; only the stated reasons are out of date. Presenter's
call, not this task's.

## Cross-links

- Origin: `Consumers/WorkoutTracker/tasks/EPIC-002-birko-backports/STORY-010-reps-adopt-cleanups/TASK-102-adopt-b-button.md`
- The convention this joins: Reps `STORY-009 / TASK-090` → framework `TASK-035` (`FormControlComponent`)
- Siblings from the same consumer: [[TASK-104]] (`b-chart`), [[TASK-105]] (`b-card`)
