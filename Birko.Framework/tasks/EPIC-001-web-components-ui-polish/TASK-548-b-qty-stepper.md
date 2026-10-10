---
id: TASK-548
parent: EPIC-001
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
created: 2026-10-10
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-qty-stepper — a form-associated quantity stepper with decimal step, min, max and a unit label

## Context

Requested by FlowerFurStudio (its TASK-013, EPIC-002 STORY-005, MVP-labelled). It is on the **storefront's critical
path** — the user's stated priority is to close the FlowerFurStudio storefront first (2026-10-10). Used on the cart,
product detail and checkout pages (FFS P02, P04, P06–P09, P11).

The shop sells fabric in 0.5 m steps and 3D models by the piece; the product carries `MinOrderQuantity`,
`OrderQuantityStep` and `SaleUnit` (Symbio G04), and Symbio prices a decimal quantity as sent (G03, verified). Today
the FFS product page counts steps with a plain number input (`Catalogue/OrderQuantityRule`); this replaces it.

Nothing in `Birko.Web.Components/src/inputs` is a stepper. `b-input` (`:332`) treats `step` as a validity
constraint, not an increment, so this is a new component, not a `b-input` mode.

## Acceptance criteria

- [x] `<b-qty-stepper>` with `value`, `step` (decimal allowed, e.g. `0.5`), `min`, `max`, `unit` (label text) and
      `name`; form-associated (`ElementInternals`), submits its value, takes part in `b-form` validity and reset
- [x] − / + buttons change the value by exactly one step; − disabled at `min`, + disabled at `max`
- [x] Typed input is clamped to `[min, max]` and snapped to the step grid **anchored at `min`** on commit
      (blur / Enter), visibly; the rounding rule (nearest step, ties up) is documented
- [x] **No float drift:** step arithmetic is done in integer step units, so 0.1 × 3 shows `0.3`, not
      `0.30000000000000004`; a test pins it
- [x] Number display is locale-aware (decimal comma in `cs` / `sk`) and typed input accepts the locale separator
- [x] Keyboard: ArrowUp/ArrowDown step, PageUp/PageDown step ×10, Home/End to min/max (WAI-ARIA spinbutton pattern);
      the new value is announced to screen readers
- [x] Buttons have translatable accessible names (`bwc.*` keys); `size` variants and `bare` like the other inputs
- [x] Emits `change` once per committed value (not per keystroke)
- [x] Tests: decimal and integer steps, clamping, off-grid snap, float drift, disabled ends, keyboard, form submit
      and reset
- [x] `Birko.Web.Components/CLAUDE.md`, README / docs updated; Playground page

## Out of scope

- Pricing or stock checks — the consumer's job
- A "max = stock" binding — `max` is an attribute the consumer sets

## Human test plan

- [x] Type 0.7 with step 0.5, min 0.5 → corrected to 0.5 visibly on blur — owner, 2026-10-10
- [ ] On a phone, tap + and − through the range; − greys out at min
- [x] NVDA: arrow keys announce each new value with its unit — owner, 2026-10-10
- [x] With the app locale set to `cs` / `sk`, the field shows `1,5` and accepts a typed comma — owner, 2026-10-10

**Walkthrough 2026-10-10 (owner, Edge + Narrator, Playground):** typing/snap, buttons and keys, Narrator (after the
plain-field rework), CS / SK formatting via the new header Locale switch — all passed. **Open:** the phone check, and
Narrator occasionally appending "new line" after an announced value ("2 m new line"). Moving the status region
inside the row did NOT fix it (owner re-check). Now announcements go through `announce()` → `Element.ariaNotify()`
(Chrome/Edge), with the region only as the fallback — needs one more Narrator re-check. Also observed: arrow keys
"sometimes" do nothing — that is Narrator's scan mode taking the arrows (Caps Lock+Space toggles it), not a defect.

## Implementation plan

Done directly (component scaffold via `/new-birko-web-component`).

## Resolution (2026-10-10)

`Birko.Web.Components/src/inputs/b-qty-stepper.ts` (`BQtyStepper extends FormControlComponent`), exported from
`inputs/index.ts`; keys `bwc.qtyStepper.{decrement,increment,decrementNamed,incrementNamed}` in `locales/en.json`;
API.md, ACCESSIBILITY.md (new Spinbutton row), README and CLAUDE.md (inventory row + Recent Updates) updated.

Decisions beyond the criteria:

- **An assigned value is not snapped.** Typed input is snapped on commit, but a `value` the app sets (from the
  server) is kept verbatim and reported via `stepMismatch` / `rangeUnderflow` / `rangeOverflow` — snapping it
  would show a quantity nobody stored. The first press from an off-grid value lands on the adjacent grid point.
- **Buttons are named after the field** (`Decrease {label}`), so a cart of steppers is distinguishable in a
  screen reader's button list; plain `Decrease` / `Increase` without a label.
- ~~Spinbutton pattern~~ **reverted after the owner's Narrator test (2026-10-10):** the unit was not read and the −
  button could not be reached ("new line" instead of "Increase"). Measured cause: Chrome drops `aria-valuetext` on a
  text `<input>` (the accessibility tree showed `valuetext: "1.5"`), and Tab-skipped / `disabled` buttons fall out of
  Narrator's reading order. Now a plain text field described by the unit (tree: textbox "Fabric", value 1.5,
  description "m"), − / + in the Tab order with `aria-disabled` at a limit, every step announced with its unit.
  Suite still 57/57 (checks rewritten for the new pattern). Human test step 3 to be repeated.
- `min` defaults to `0`, `step` to `1`, no `max`. Unreadable typed text reverts with no `change`.

**Proof.** Playground `src/qty-stepper-smoke.ts` (registered in `app.ts` and `verify.mjs`): 57/57, every other
suite green, no EMPTY component. Mutation: replacing the scaled-integer step with `value + step` fails 6 checks
(`0.30000000000000004`, `0.9999999999999999`, off-grid landing). `device-fix-check.mjs`: 68/68, both new
`TEXT_ENTRY_CASES` (default and `size="sm"`) at 16px under a coarse pointer. `a11y-name-check`: OK.
`tsc -p Birko.Web.Components` clean.

**Not automated:** the locale display (`1,5` under `cs`) — switching the global locale in the shared verify page
would disturb the suites that assert English; covered by the human test plan instead.
