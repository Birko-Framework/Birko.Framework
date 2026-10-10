---
id: TASK-548
parent: EPIC-001
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] `<b-qty-stepper>` with `value`, `step` (decimal allowed, e.g. `0.5`), `min`, `max`, `unit` (label text) and
      `name`; form-associated (`ElementInternals`), submits its value, takes part in `b-form` validity and reset
- [ ] − / + buttons change the value by exactly one step; − disabled at `min`, + disabled at `max`
- [ ] Typed input is clamped to `[min, max]` and snapped to the step grid **anchored at `min`** on commit
      (blur / Enter), visibly; the rounding rule (nearest step, ties up) is documented
- [ ] **No float drift:** step arithmetic is done in integer step units, so 0.1 × 3 shows `0.3`, not
      `0.30000000000000004`; a test pins it
- [ ] Number display is locale-aware (decimal comma in `cs` / `sk`) and typed input accepts the locale separator
- [ ] Keyboard: ArrowUp/ArrowDown step, PageUp/PageDown step ×10, Home/End to min/max (WAI-ARIA spinbutton pattern);
      the new value is announced to screen readers
- [ ] Buttons have translatable accessible names (`bwc.*` keys); `size` variants and `bare` like the other inputs
- [ ] Emits `change` once per committed value (not per keystroke)
- [ ] Tests: decimal and integer steps, clamping, off-grid snap, float drift, disabled ends, keyboard, form submit
      and reset
- [ ] `Birko.Web.Components/CLAUDE.md`, README / docs updated; Playground page

## Out of scope

- Pricing or stock checks — the consumer's job
- A "max = stock" binding — `max` is an attribute the consumer sets

## Human test plan

- [ ] Type 0.7 with step 0.5, min 0.5 → corrected to 0.5 visibly on blur
- [ ] On a phone, tap + and − through the range; − greys out at min
- [ ] NVDA: arrow keys announce each new value with its unit

## Implementation plan

_Populated by `/tasks plan TASK-548` — leave empty until then._
