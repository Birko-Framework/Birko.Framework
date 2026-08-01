---
id: TASK-135
parent: STORY-052
feature: FEATURE-016
status: review
priority: P1
assignee: ai
created: 2026-08-01
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `b-input type="decimal"`: comma-locale decimal entry, owned by the component

> **Backfilled 2026-08-01.** The work shipped first — `Birko.Web.Components` `7dfacf4` (the mode) and
> `e3727cf` (`percent` routed through it) — and this file was written afterwards from those commits, the
> code, and the origin task. So the acceptance criteria below are a **record of what was built**, not an
> independent target it was built against; read them as such. What is *not* backfilled is the status: the
> outstanding steps in § Why this is `review` are genuinely outstanding, not paperwork.

## Context

Origin: **Reps `TASK-104`** (`Consumers/WorkoutTracker/tasks/EPIC-001-workout-tracker/TASK-104-decimal-comma-separator-not-accepted.md`,
`status: done`) — note this is the *consumer's* TASK-104, unrelated to this repo's TASK-104 (`b-chart`).
Found on an iPhone device pass, 2026-07-31: the owner typed a weigh-in of **81.8 kg** and it stored as
**818 kg**. On device: *"I pressed the `,` on the phone keyboard and it did not take it."*

Two failures, both silent:

1. HTML's "valid floating-point number" grammar accepts only `.`, so on a comma keypad (Slovak, Czech,
   German, French, Spanish…) **WebKit refuses to insert the character at all**. `81,8` leaves the field
   holding `818`, which parses perfectly and stores a hundredfold-wrong value. Nothing invalid was ever
   submitted, so no validation could fire.
2. Even where a comma reaches JS, `parseFloat('81,8')` returns **81** — it stops at the character it cannot
   read rather than failing. Silent truncation, same class of bug.

Reps' own task recorded the framework opportunity and deliberately did **not** take it: *"`b-input` has no
notion of a decimal field — each consumer must know to pass `type="text" inputmode="decimal"` and to parse
with a comma-aware helper, which is exactly the knowledge nobody has until a comma-locale device bites
them."* That is this task.

## What shipped

- **`type="decimal"`** on `b-input` — a component-level mode, **not** an HTML input type. Renders
  `type="text" inputmode="decimal"`: text accepts the separator, `inputmode` still summons the numeric
  keypad. An explicit `inputmode` still wins.
- **The component owns `min`/`max`/`step`** in this mode and does not forward them to the inner control.
  It has to: the inner control is `type="text"`, where the browser reports valid for anything, and because
  the control is form-associated that is *worse* than no validation — it would actively tell the form an
  out-of-range value is fine. Enforced in `syncFormState` as `rangeUnderflow` / `rangeOverflow` /
  `stepMismatch`, with `badInput` for unparseable text and the `error` attribute still winning as the app's
  verdict. Step alignment is checked in a scaled integer domain, because `(0.3 - 0) % 0.1` is
  `0.09999999999999998` and would reject a legitimate value.
- **`parseDecimal`** in `birko-web-core` — accepts either separator, and is deliberately stricter than
  `parseFloat`: trailing junk, two separators and a lone separator give `null`, not a plausible wrong number.
- **`b-form` wiring**, which is where the real bug was: the field-type switch had **no `decimal` case**, so
  it emitted no `type`, `b-input` fell back to `text`, and the whole mode vanished silently. Measured before
  that fix: `9999` against `max=500` reported `checkValidity() === true`, and so did `"abc"` — the exact
  failure the mode exists to prevent, reintroduced one layer up.
- **`percent` routed through the same mode** (`e3727cf`), since a percent is a decimal by definition. Its
  `%` suffix and 0-100 ⇄ 0-1 conversion are keyed on the **schema** type, so both survived. The damaging
  half was `_convertPercent` coercing with `Number()`: for `'12,5'` that is `NaN`, its `!isNaN` guard
  skipped the branch, and the **raw string** was handed out as the *stored* value where `0.125` was
  expected. Mis-validation is recoverable; handing a string to something expecting 0-1 is corruption.
- **One `COMMA_TYPED_TYPES` set** shared by the render and the rule coercion, so the two cannot drift apart
  again — `_checkRule`'s comma-aware coercion originally gated on `'decimal'` only, so a `min`/`max`/`range`
  rule still silently passed for any comma percent.

## Acceptance criteria

- [x] A comma-locale user can type a decimal separator into a Birko decimal field at all.
- [x] The parser is stricter than `parseFloat` — no plausible-looking wrong numbers from partial parses.
- [x] `min`/`max`/`step` are enforced in the mode rather than advertised and ignored, and reach the form
      (the control is form-associated, so silence would be an active lie).
- [x] The mode is reachable **through a `b-form` schema**, not only as a hand-written tag.
- [x] `percent` accepts a comma and stores a **number**, in both conversion directions.
- [x] A `min`/`max`/`range` **rule** and the corresponding **attribute** agree on a comma value.
- [x] Documented for the next consumer, who will not know the problem exists until it bites them:
      `API.md` § `type="decimal"`, `CLAUDE.md`, and the tradeoff (no native spinner arrows) recorded rather
      than discovered.
- [x] Playground verifier green — 189/189 then 197/197 — with each fix falsified independently
      (removing the `b-form` switch case fails 5 of 11; reverting only the rule coercion fails exactly 1),
      and the checks that *survive* falsification named in the commits so they are not trusted alone.

## Why this is `review`, not `done`

Two outstanding items, neither of which is paperwork:

1. **The origin consumer still carries a fork.** Reps fixed this in its own code (`ec69529`:
   `type="text" inputmode="decimal"` plus `parseDecimal` at each of five parse sites) *before* the framework
   mode existed, and has not migrated to `b-input type="decimal"`. STORY-052's acceptance test is explicit
   that this is the failure mode it exists to prevent: *"An adoption gap is done when every consumer
   benefits without any of them special-casing — the fix lives in the component or it is not a fix. A
   consumer keeping a fork **is** the failure mode."* The migration is Reps-side work and belongs in that
   repo's tree (its own `STORY-009`, as its TASK-104 note suggests); this task is not done until it exists
   and lands.
2. **The framework mode has never been exercised on a comma-locale device.** Reps' device proof
   (2026-07-31 / 08-01, Slovak keypad) was of *Reps' hand-rolled inputs*, not of `b-input`. And per Reps'
   own note, this cannot be closed headlessly: *"a headless run cannot reproduce WebKit's refusal to insert
   the character into a `type="number"` field, which was the whole bug."* The premise of the mode is a
   WebKit behaviour no Chromium harness can show.

## Human test plan

- [ ] **On a real iPhone with a Slovak (or other comma) keypad**, in the playground gallery: type `81,8`
      into a `b-input type="decimal"` and confirm the comma is inserted, `numericValue` reads `81.8`, and an
      out-of-range value is refused. This is the only proof the mode works — the headless suite structurally
      cannot reproduce the bug it fixes.
- [ ] The same, through a `b-form` `percent` field, confirming `12,5` stores `0.125`.

## Out of scope

- Reps' migration off its fork (that repo's tree — but it is what closes item 1 above).
- `range` fields with `valueType: 'percent'`, which keep `Number()` in their own branch: the value arrives
  already numeric from `b-range`, so no comma can reach it. Flagged in `e3727cf` rather than changed.
- Everything `b-form.validate()` does with the validity this mode produces — that is [[TASK-136]], and the
  hole it fixes is the reason this mode's `badInput` reached no consumer for a day.

## Cross-links

- Shipped as: `Birko.Web.Components` `7dfacf4`, `e3727cf`; playground checks in `Birko.Web.Playground` `1ac63f3`
- Origin: Reps (`Consumers/WorkoutTracker`) `TASK-104` — *consumer* id; `ec69529` is its own fix
- Consumed by: [[TASK-136]] (`b-form.validate()` surfacing the validity this mode reports)
- Siblings from the same consumer: [[TASK-104]] (`b-chart`), [[TASK-105]] (`b-card`), [[TASK-107]] (`b-button`)
