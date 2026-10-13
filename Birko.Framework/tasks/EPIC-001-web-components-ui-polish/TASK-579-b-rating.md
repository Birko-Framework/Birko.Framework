---
id: TASK-579
parent: EPIC-001
feature: FEATURE-001
priority: P2
status: in-progress
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `b-rating` — a form-associated star-rating input

## Context

Asked for by the FlowerFurStudio storefront: its TASK-012 (`b-rating` in Birko.Web.Components) and, behind it,
TASK-025's review form, which submits a star rating. It is the storefront's last open framework gap (Outline
"Birko.Framework — open work & priorities", 12 Oct). The storefront renders **read-only** stars on its
server-rendered pages as plain markup and CSS; the component is for **input**, with a read-only mode for places
a client page shows a rating it already has.

No rating control exists in `Birko.Web.Components` today; `b-radio` is the nearest (a radio group).

## Acceptance criteria

- [ ] `b-rating` with `value`, `max` (default 5), `name`, `required`, `disabled` and `readonly`; whole stars only
- [ ] Keyboard-operable as a radio group: arrows move and select, Home/End go to the ends, one tab stop; each
      star has an accessible name ("3 of 5"), translatable through `bwc.rating.*`
- [ ] Form-associated like the other `b-*` controls: the value submits with a form, `required` validates,
      a form reset restores the initial value
- [ ] `readonly` shows the value with no interaction and no tab stop, and is announced as a value, not a control
- [ ] Theming through `--b-*` tokens; the filled star meets WCAG AA non-text contrast (3:1) in light and dark
- [ ] A Playground page and its smoke test; README and component catalogue entries

## Out of scope

- Half stars (the storefront does not need them)
- Read-only stars on server-rendered pages: the storefront keeps those as plain markup

## Human test plan

- [ ] Rate with the keyboard only, then with a screen reader (NVDA or Narrator): each star is announced with its
      position, and the chosen value is announced on change

## Implementation plan

Drafted 2026-10-13 at pick. Code in `Birko\Web` (`Birko.Web.Components/src/inputs/b-rating.ts`); the smoke test in
`Birko.Web.Playground`. Ships in Birko.Web v0.1.4.

1. **Structure: native radios behind the stars.** `b-rating extends FormControlComponent`. Inside the shadow root, a
   `role="radiogroup"` holds one visually hidden `<input type="radio">` per star (values `1`…`max`, a name unique to
   the instance), each with a `<label>` drawing an SVG star and an `aria-label` of "{value} of {max}"
   (`bwc.rating.star`). Native radios give one tab stop, arrow keys that move and select, and focus handling for free,
   and the DOM stays in reading order, so arrows match what is on screen. Home/End are added (radios lack them).
2. **Form association:** `value` is `"1"`…`"max"` or empty. `formValue()` is the value, or `null` when unrated (nothing
   submitted). `required` is set on every radio and `validationSource()` returns the first one, so the group's
   native `valueMissing` drives validity. `formAnchor()` is the group. The base's attribute-based reset fits (the
   state is the `value` attribute). `syncFormState()` at the end of `onUpdated()`, and `change` `{ name, value }` on a
   user pick, none on a programmatic set (as native).
3. **Read-only:** no inputs at all, so no tab stop and nothing operable: a `role="img"` with an `aria-label` of
   "Rated {value} of {max}" (`bwc.rating.readonly`) or "Not rated" (`bwc.rating.none`). `disabled` keeps the radios
   but disables them (and `<fieldset disabled>` through the base).
4. **Look:** filled stars from the value; hover preview through `:has()` (the hovered star and those before it).
   Fill `--b-rating-color`, defaulting to `--b-color-warning`; an empty star is an outline in `--b-text-muted`
   (already AA). **Contrast measured, not assumed**, in all five themes: filled-star fill and empty-star outline
   against the background, at least 3:1 (WCAG 1.4.11). `size` = shape weight (star diameter), with the coarse-pointer
   44 px target floor the other pickers carry. The usual `renderField` label, hint, description and error rows.
5. **Registration:** `inputs/index.ts`, `src/index.ts`, `locales/en.json` (`bwc.rating.*`), the README path table
   (the entry-points check enforces it), the README section, and the CLAUDE.md inventory row.
6. **Playground:** a gallery card (Inputs), and `rating-smoke.ts` registered in `verify.mjs`: keyboard (arrows,
   Home/End, one tab stop), names, `change`, `FormData`, `required` validity, reset, read-only without inputs or
   tab stop, disabled, and contrast in every theme. Prove it fails against a broken build. `entry-points-check` must
   still pass.
