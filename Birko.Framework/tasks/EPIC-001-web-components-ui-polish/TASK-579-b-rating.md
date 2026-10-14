---
id: TASK-579
parent: EPIC-001
feature: FEATURE-001
priority: P2
status: done
assignee: ai
created: 2026-10-12
depends-on: []
blocks: []
pr: null  # Birko.Web ddef16e, shipped in v0.1.4; Playground 75bd14d
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

- [x] `b-rating` with `value`, `max` (default 5), `name`, `required`, `disabled` and `readonly`; whole stars only
- [x] Keyboard-operable as a radio group: arrows move and select, Home/End go to the ends, one tab stop; each
      star has an accessible name ("3 of 5"), translatable through `bwc.rating.*`
- [x] Form-associated like the other `b-*` controls: the value submits with a form, `required` validates,
      a form reset restores the initial value
- [x] `readonly` shows the value with no interaction and no tab stop, and is announced as a value, not a control
- [x] Theming through `--b-*` tokens; the filled star meets WCAG AA non-text contrast (3:1) in light and dark
- [x] A Playground page and its smoke test; README and component catalogue entries

## Out of scope

- Half stars (the storefront does not need them)
- Read-only stars on server-rendered pages: the storefront keeps those as plain markup

## Human test plan

- [x] Rate with the keyboard only, then with a screen reader (NVDA or Narrator): each star is announced with its
      position, and the chosen value is announced on change
  - **Passed 2026-10-14:** the user tested the published Playground (Birko.Web v0.1.4, Inputs → Rating): keyboard only
    (one tab stop, arrows, Home/End), a screen reader (each star "n of 5", the value announced on change), and the
    size, disabled and finstat theme variants.

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

## Progress log

- 2026-10-13 — Built: Birko.Web `ddef16e`, Playground `75bd14d`.
  - **Measured contrast changed the plan:** `--b-text-muted` fails as the empty-star outline in finstat (2.3) and neon
    (2.9), so the outline is `--b-text-secondary`. finstat's brand yellow is 1.7:1 on white, so finstat sets
    `--b-rating-color: #a77400`. Rendered in the browser, every theme clears 3:1 on `--b-bg` and `--b-bg-secondary`;
    the lowest is the light fill on `--b-bg-secondary`, 3.04.
  - **Tests:** `rating-smoke` 50/50 inside `verify.mjs` (exit 0). A new `keyboard-check.mjs`, 14/14, uses trusted
    puppeteer input, because an in-page `KeyboardEvent` neither tabs nor moves a radio group. `entry-points-check`
    403/403; real `tsc` 0.
  - **Proved red:** dropping the finstat override (fill 1.75:1); focusable star labels (3 tab-stop checks); and each of
    the four review fixes reverted (see below).
  - **Close gate**, independent reviewer: no blockers. Fixed, each with a test that fails without it:
    - a read-only rating's name went stale when `.value` was set by script;
    - hover previewed a read-only rating;
    - a value set before a larger `max` arrived was lost;
    - after `form.reset()` the control ignored later `value` attribute changes; it now hands control back to the
      attribute, as a native input does.
    The attribute-versus-pick semantics are in the README for the storefront.
  - Spawned TASK-591: the Playground's alias map predates TASK-564, so a smoke cannot import a component by its own path.
  - **Status `verify`:** the human test plan (keyboard, then NVDA or Narrator) is not run. The reviewer flagged
    `aria-required` on the group plus `required` on each radio as a possible double "required"; listen for it then.
- 2026-10-14 — Shipped in Birko.Web v0.1.4 (pushed; Pages run 38520833926 green). The user signed off the human test
  plan on the published Playground. Done. The storefront (builds from Birko.Web `main`) can take TASK-012 and TASK-025.
