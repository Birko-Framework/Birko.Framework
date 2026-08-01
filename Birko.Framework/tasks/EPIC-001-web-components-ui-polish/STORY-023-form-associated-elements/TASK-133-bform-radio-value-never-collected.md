---
id: TASK-133
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

# `b-form`: a `radio` field's value is never collected, and a `required` radio group can never validate

## Context

Found while filing [[TASK-132]] — I asserted in a draft that `b-form`'s radio branch omits `data-path` and
then went to verify it rather than ship the claim. It is true, and the consequence is worse than the
checkbox gap it was found next to.

`_fieldTag`'s `radio` case (`src/inputs/b-form.ts`) is the one branch that does **not** interpolate the
shared attribute string `a` into its markup:

```ts
case 'radio':
  return (a) => (field.options ?? []).map(o =>
    `<b-radio name="${field.name}" value="${o.value}" label="${o.label}" ${a.includes('disabled') ? 'disabled' : ''}></b-radio>`
  ).join('');
```

`a` is where `data-path` lives, so no `b-radio` carries one. Everything downstream resolves fields by
`this.$('[data-path="…"]')`, so for a radio field:

- `_getFieldElement` returns `null`, so `_getGroupValues` never assigns the key — **the field is absent from
  `getValues()` and from `validate().data` entirely**;
- `_validateField` therefore sees `value === undefined`, which is empty, so a `required` radio group reports
  "is required" **no matter what the user picked** — unsatisfiable;
- `_applyErrors` cannot set an `error` attribute on it, `_wireFieldEvents` never attaches a `change`
  listener (so no `change` event, no `onFieldChange` callback, no `validate-on="blur"`), `setFieldError` /
  `setFieldDisabled` / `focusField` / `setFieldOptions` are all no-ops on that path, and `_setFieldValue`
  cannot restore a value — so `setValues()` / `reset()` silently skip it too.

Measured (headless, playground probe, two-option required radio group with option A clicked):

```
radio count 2 / data-path present: ,          ← both empty
checked radio -> valid=false  err={"pick":"Pick is required"}  data={"agree":false,"on":false}
```

Note what `data` does *not* contain: `pick`. A non-required radio field is therefore **silent data
loss** — the form reports valid and the field never reaches the API.

### Why it survived

No consumer uses it. Swept all 16 local consumers: **zero** `type: 'radio'` fields in any `b-form` schema
(radio is used only as a standalone `<b-radio>` outside `b-form`, where it works — its own
form-association is correct and covered by `form-assoc-smoke`). So this is latent, not live, which sets
the priority — but it means the *first* consumer to reach for a radio field in a schema hits all of the
above at once, and the failure looks like a broken form rather than a framework bug.

### The design question this has to answer

A radio field renders **N elements for one logical field**, which is why it was written differently in the
first place. So `data-path` cannot simply be pasted in: `$('[data-path="pick"]')` must resolve to one
element, and the helpers must read the *group's* checked value rather than one member's. The options:

1. **Group wrapper** — emit the members inside one element carrying `data-path` (the `_renderField` div
   already exists per field; a `role="radiogroup"` wrapper would also fix an ARIA gap), and teach
   `_getFieldValue` / `_setFieldValue` a `radio` case that reads/writes the checked member.
2. **`data-path` on every member** + make the helpers group-aware (`$$` instead of `$`). Cheaper, but it
   breaks the "one element per path" invariant every other helper relies on, so each of the eight call
   sites has to be audited.
3. **Route radio through `b-option-group`** (single-select, one element, already `data-path`-clean and
   already handled by `_populateOptions`) and keep `type: 'radio'` as an alias. Deletes the special branch
   instead of fixing it — but changes the rendered markup and the look for anyone using it.

Option 1 keeps the invariant and fixes the ARIA gap in the same move; it is the expected answer, but the
choice belongs in this task's plan, with the `b-radio` sibling-unchecking behaviour
(`b-radio-change` + explicit `syncFormState()`, see `CLAUDE.md` § *Form-association convention*) checked
against whichever shape wins.

## Acceptance criteria

- [ ] A `radio` field's selected value appears in `getValues()` and in `validate().data`, under its
      dot-path, for both root-level and nested groups.
- [ ] A `required` radio group **passes** once a member is checked and fails while none is.
- [ ] `setValues()` and `reset()` select the right member; `reset()` with no default clears the group.
- [ ] A user click emits `b-form`'s `change` event with the field's path and value, and fires any
      `onFieldChange` callback registered for it.
- [ ] `setFieldError` / `focusField` / `setFieldDisabled` resolve the field and take effect;
      the error message renders where the other field types render theirs.
- [ ] `validate-on="blur"` validates the field on change, like every other type.
- [ ] Exactly **one** error message for the group, not one per member (the reason `b-radio` sets
      `supportsRequiredValidation = false` — do not undo that).
- [ ] Whichever shape is chosen, the "one element per `data-path`" invariant holds, or every helper that
      assumes it is updated in the same change and the new invariant is documented in `CLAUDE.md`.
- [ ] Playground checks covering the above, each falsified by reverting the fix; the existing
      `form-assoc-smoke` radio checks (standalone `b-radio` in a native form) stay green.

## Out of scope

- `b-radio` itself. Its form association, its group semantics and its
  `supportsRequiredValidation = false` are correct and covered; this is a `b-form` wiring defect.
- The `required`-on-a-toggle gap ([[TASK-132]]) — adjacent, different cause.
- Introducing a `radiogroup` ARIA role for standalone `b-radio` usage outside `b-form`. If option 1 is
  taken, the wrapper gets the role for free inside `b-form`; the standalone case is a separate question.

## Human test plan

- [ ] Render a `b-form` with a required radio field in the playground gallery, click each option in turn,
      and confirm by eye that the selection is visible, the error clears, and the label/spacing match the
      other field rows. The value-collection half is automated; the *layout* of a grouped field inside the
      form grid is a visual judgement, and option 1 changes the DOM around it.

## Cross-links

- Found while filing [[TASK-132]], itself from `Birko.Web.Components` `9402219`
- The branch: `Birko.Web.Components/src/inputs/b-form.ts` — `_fieldTag`, `case 'radio'`
- `Birko.Web.Components/CLAUDE.md` § *Form-association convention* — "`b-radio` needs no submission
  coordinator … validate the group in the page or `b-form`" (which is exactly what `b-form` cannot do today)
