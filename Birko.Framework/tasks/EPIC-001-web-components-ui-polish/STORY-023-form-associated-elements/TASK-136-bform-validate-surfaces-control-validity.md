---
id: TASK-136
parent: STORY-023
feature: FEATURE-001
status: review
priority: P1
assignee: ai
created: 2026-08-01
depends-on: [TASK-135]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `b-form.validate()` surfaces a control's own verdict — on a whitelist, not `checkValidity()`

> **Backfilled 2026-08-01**, same day the work shipped (`Birko.Web.Components` `9402219`,
> `Birko.Web.Playground` `16a72d8`). Written from those commits, so the acceptance criteria below are a
> **record of what was built** rather than an independent target it was built against. The status is not
> backfilled: § Why this is `review` is a real outstanding step.

## Context

[[TASK-135]] gave `b-input type="decimal"` a correct `badInput` verdict, and **nothing consumed it**.
`validate()` ran schema rules only — `grep checkValidity src/inputs/b-form.ts` returned nothing — while
every consumer reads exactly that path: the Shell base pages (`base-crud-page.ts:924`,
`base-form-modal.ts:235`, `base-detail-page.ts:244`) all do `const { valid, data } = form.validate()` and
nothing else. So the validity the mode was added to produce reached no consumer.

Measured headless against Symbio's real tax-rate `percent` schema (`required` + `min 0` + `max 100`):

| typed | before ([[TASK-135]]) | after [[TASK-135]], before this |
|---|---|---|
| `20` | valid → stored `0.2` | valid → stored `0.2` |
| `12,5` | char dropped → "required" error | valid → stored `0.125` ← the fix, kept |
| `abc` | char dropped → blocked by required | **`valid: true`, `data.percentage === "abc"`** |
| blank | blocked | blocked |

The consumer consequence, measured in Symbio: `"abc"` → `Number(...)` → `NaN` → serialized `null` → create
400s, and **edit silently succeeds with the old percentage kept**, because the update DTO's field is
nullable and the service guards on `HasValue`. That is worse than the mis-validation [[TASK-135]] replaced:
the form says saved and the value did not change.

## The decision this task exists for

**A blanket `host.checkValidity()` gate is not the fix.** `12.5` typed into a plain `type="number"` field
is *already* natively invalid — `step` defaults to 1, so the browser reports `stepMismatch` with "the two
nearest valid values are 12 and 13" — and `b-form` has always ignored it. Adopting the whole `ValidityState`
would newly reject fractional input in every consumer `number` field that has ever worked (Symbio alone:
`scrapPercent`, `moisturePercent`, `latePenaltyPercent`, `organicMatterPercent`). A silent breaking change
dressed as a bug fix.

So the adopted set is a fixed whitelist: **`badInput` always**, plus
**`rangeUnderflow` / `rangeOverflow` / `stepMismatch` only for the decimal-mode types**, where those flags
are `b-input`'s own and carry none of `type="number"`'s implicit-`step` legacy. Everything else is excluded
with its reason, each exclusion pinned by a check that fails if it is ever adopted silently
([[TASK-134]] owns revisiting them).

Two secondary decisions, both recorded in code:

- **Order, not special-casing, settles duplicate reporting.** The control is consulted only *after* the
  schema rules, so a field carrying both a `max` rule and a `max` attribute reports once — with the rule's
  wording — and no existing message changes.
- **The data contract on failure was undecided and now is not.** A rejected field keeps its collected value
  in `data` (a `'12,5'` that failed a `max` rule is still `0.125` — a consumer echoing it back needs it),
  **except** a `badInput` field, which is `null`ed: there is no number there, and the raw string was the
  thing that corrupted.

## What else this found

- **`b-input` resurrected a cleared field's value.** It restored `this._value || this.attr('value')`, so
  `''` fell through to the schema-declared value — and re-renders arrive from ordinary things, `b-form`
  dropping the `error` attribute among them. The old text sprang back into the box, `required` did not fire,
  and the form saved the value the user had just deleted. `_value` is now `string | null`.
- **A stale `error` attribute masks a control's own flags** — both `FormControlComponent._syncValidity` and
  `b-input.syncFormState` return early on it — so a *second* Save click on unchanged junk found a masked
  control and passed the form. `validate()` now clears errors before reading validity, and collects values
  before clearing (since clearing re-renders).
- **The playground harness had been hiding results.** `verify.mjs` slept a fixed 1s, so when the suite grew
  past it `backport-smoke` **stopped reporting entirely** — and a suite that never ran leaves no FAIL lines
  to grep, so it read as green. Fixing it by waiting for the summary lines was still wrong: the per-check
  lines arrive *after* their own summary, so the first attempt reported "0 failing" over a 223/224 suite.
- Three defects filed rather than fixed here: [[TASK-132]], [[TASK-133]], [[TASK-134]].

## Acceptance criteria

- [x] A control's `badInput` fails `validate()` through the schema path, with the error on that field via
      the existing `_applyErrors()` route and the control's own message.
- [x] `12.5` in a plain `type="number"` field **still passes** — guarded by a check whose premise (that the
      field is natively invalid) is asserted, not assumed, so it cannot go vacuous.
- [x] Decimal-mode `min`/`max`/`step` attributes report through `validate()`, so they no longer disagree
      with the equivalent rules.
- [x] A field with both a rule and an attribute for one constraint reports exactly one message.
- [x] Blank still belongs to `required`, an explicit `error` attribute is still the app's verdict, and the
      `%` suffix + 0-100 ⇄ 0-1 conversion stay keyed on the schema type.
- [x] Disabled fields are skipped (`willValidate`), matching native constraint validation.
- [x] The data contract on failure is decided and documented (`FormResult.data`, `API.md`).
- [x] Every exclusion is a falsifiable check, not a comment.
- [x] Playground verifier green — 226/226 backport-smoke, other five suites unchanged — and **every** new
      check falsified by reverting the specific site it covers, with the one survivor
      (collect-before-clear) reported and kept deliberately rather than quietly.
- [x] `verify.mjs` cannot silently drop a suite again, and exits non-zero.

## Why this is `review`, not `done`

The verification is complete on the framework side and incomplete on the side that reported it.
**Symbio's tax-rate form has not been re-checked end-to-end against the fix** — the before/after table above
was measured on the *component* path. STORY-052's rule applies here even though this task sits under
STORY-023: *"Verification finishes in the consumer that reported it… only the reporting surface proves the
complaint is answered."* The specific thing to disprove is the nastiest half of the report: that an **edit**
reports success while leaving the old percentage in place.

There is also no consumer-side task for this: the Symbio report was ad hoc, so unlike [[TASK-135]] there is
no origin ticket to point at or close. Worth filing there when the re-check happens.

## Human test plan

- [ ] **In Symbio**, open a tax rate with an existing percentage, type `abc` into the percent field and
      Save: the form must block with an error on that field, and the stored percentage must be unchanged.
      Then type `12,5` and Save: it must store `0.125`. The create path must 400 no longer.

## Out of scope

- The three defects this surfaced — [[TASK-132]], [[TASK-133]], [[TASK-134]].
- Widening the whitelist ([[TASK-134]], gated on decision D8).
- Reps' migration off its decimal fork ([[TASK-135]] item 1).

## Cross-links

- Shipped as: `Birko.Web.Components` `9402219`, `Birko.Web.Playground` `16a72d8`
- Depends on: [[TASK-135]] (the mode whose verdict this makes visible)
- `Birko.Web.Components/API.md` § *What `validate()` takes from the controls themselves* — the shipped
  flag table and the `data`-on-failure contract
