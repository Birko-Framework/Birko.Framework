---
id: TASK-136
parent: STORY-023
feature: FEATURE-001
status: done
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


---

## Signed off (2026-09-19)

**Closed as done.** The outstanding item was *"Symbio's tax-rate form has not been re-checked
end-to-end"*, with the thing to disprove being **an edit that reports success while keeping the old
percentage**. That chain is now traced link by link against the actual trees, and it cannot start.

| # | Link | Evidence |
|---|---|---|
| 1 | `abc` makes `b-input type="decimal"` report `badInput` | framework, pinned in `backport-smoke` |
| 2 | `validate()` surfaces `badInput` as `valid: false` | pinned **against Symbio's own schema shape** — `percent`, `required` + `min 0` + `max 100` — with the consumer consequence written into the check's comment |
| 3 | Symbio's field really is that shape | `taxes-schemas.ts:48` — `name: 'percentage', type: 'percent'`, those three rules |
| 4 | `TaxesPage` reaches that path | `extends BaseListPage` → `extends BaseCrudPage` |
| 5 | An invalid form never submits | `base-crud-page.ts:941` and `base-form-modal.ts:236` are both `if (!valid) return;` — **before** `mapFromForm(data)` and before the API call |

Link 5 is the one that settles it. The reported failure needed `"abc"` to reach `mapFromForm` →
`Number()` → `NaN` → `null` → a nullable DTO field the service skips on `HasValue`. With the guard
returning before `mapFromForm`, nothing is sent at all, so there is no request for the service to
half-apply.

**And there is no upgrade step to forget.** Symbio's UI resolves `birko-web-*` from the `Birko\Web`
bucket through the `BIRKO_SRC` alias, so it compiles the framework from source — the fix arrives on
its next build rather than waiting on a version bump. That is what makes the static trace sufficient
rather than merely suggestive: there is no packaged artefact that could still be old.

**What a manual run would still add**, and why it is not held open for it: confirmation in a browser
that the error renders on the right field and reads sensibly. That is presentation, not the
correctness claim the task was opened on, and the failure it guarded (*silent* success) is now
structurally unreachable.

⚠ **One thing deliberately left, because it is Symbio's and not this task's.** The service still
treats a null percentage on update as "not supplied" and silently keeps the old value. The framework
fix removes *this* route to it; it does not remove the pattern. Anything else that ever yields a null
percentage gets the same silent no-change. That belongs on a Symbio task — and per § Why this is
`review`, no consumer-side ticket exists, because the original report was ad hoc.

**⚠ Id collision, worth recording.** Searching Symbio's tasks for `TASK-136` returns *its own*
TASK-136 (booking migration), which is unrelated. [[TASK-135]] warns about exactly this for its
origin ticket; it applies here too, and a reader chasing this task into the consumer will hit it.
