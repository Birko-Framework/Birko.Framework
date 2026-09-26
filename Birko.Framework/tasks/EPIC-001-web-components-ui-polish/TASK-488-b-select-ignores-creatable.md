---
id: TASK-488
parent: EPIC-001
feature: FEATURE-001
status: done
priority: P1
assignee: ai
created: 2026-09-25
depends-on: [TASK-494]
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-select` ignored `creatable`, so a `b-form` single select could not take a new value

> **Filed after the code was written (2026-09-25)**, from a bug report pasted into the session. The
> acceptance list below is the report's own, not a transcript of the implementation. The boxes stay
> unticked until `/tasks close` checks each one against the diff.

## Context

A `b-form` field declared as

```ts
{ name: 'menuGroup', type: 'select', options, searchable: true, creatable: true }
```

rendered a `b-select` that only let the user pick an existing option. Found in Symbio's admin, under
Presentation → Navigation → "Add navigation item" → *Menu group*
(`Symbio.UI/src/modules/presentation/navigation/navigation-schemas.ts:12`). A site owner could not create
a menu group such as `footer-2`, although Symbio's API accepts any group name.

**Mechanism.** `b-form.ts` declares `FormField.creatable` and, in `_fieldAttrs`, emits a `creatable`
attribute for both `select` and `multi-select`. `b-multi-select` implements it. `b-select` did not: its
`observedAttributes` had no `creatable`, so the attribute was ignored without any error. This is the rule 51
shape: an attribute that does nothing while looking like a feature.

**Why this is not a mapping to `allow-free-text`.** `b-select`'s nearest existing mode behaves differently.
It makes the typed text the value, commits it on Enter **and on every close**, including Escape and an
outside click, and shows no create row. `creatable` means "pick from the list, or explicitly add to it",
where Escape and clicking away cancel. Mapping one to the other would have made Escape commit the value.

## Implementation plan

1. `b-select`: observe `creatable` and `label-create`. `creatable` implies the typed combo, because a native
   `<select>` has nowhere to type.
2. Show a create row (`.option.option-create`) when the query names no option. Existing options are matched
   with the same fold the filter uses (`foldForSearch`), so `header` selects `Header` instead of creating
   a near-duplicate. When the create row shows, it replaces "No matches".
3. Enter with a non-empty query selects the option that query names, or creates the value. Enter with an
   empty query falls through, so the enclosing `b-form` still submits. Escape and an outside click
   cancel through the existing `_closeDropdown` path.
4. Committing adds `{ value, label: value }` to the options, selects it, and reports it through `change`.
   **It fires no `create` event.** `b-multi-select`'s `create` bubbles and is composed, with
   `detail.name` = the typed text. Symbio's building, products and tasks list pages listen for *any*
   `create` on their shadow root and call `createTag({ name: e.detail.name })`. A `b-select` event whose
   `name` was the field name would have them create a tag called after the field.
5. `_labelFor(value)`: a value outside the options displays as itself under `creatable` or
   `allow-free-text`. Otherwise a form reopened on a created value renders blank, because the schema's
   options never contain it.
6. Accessibility: a polite `role="status"` live region announces the create row and "Created “…”".
7. i18n: add the keys `bwc.select.create` / `bwc.select.created` (`{value}`) to `locales/en.json`.
   `label-create` overrides the verb, as on `b-multi-select`.
8. Docs: add rows to `API.md`, a README section explaining when to use `creatable` versus
   `allow-free-text`, and a doc comment on `FormField.creatable`.
9. Regression suite: `Birko.Web.Playground/src/select-creatable-smoke.ts`, registered in `app.ts` and in
   `verify.mjs`'s `SUITES`.

## Acceptance criteria

- [x] In a `b-form`, a `type: 'select'` field with `searchable: true, creatable: true` accepts a typed new
      value. The value is submitted with the form, and it shows as selected when the form is reopened with
      that value.
- [x] The same works for a standalone `<b-select searchable creatable>`.
- [x] Existing selects without `creatable` behave exactly as before: no create option, no free text.
- [x] Keyboard: type, then Enter (or choose the create option) commits it; Escape cancels. The accessible
      name and the announcement are OK. **Re-ticked 2026-09-26 after [[TASK-494]]**, which the owner's
      screen-reader pass confirmed. Before that, it was **unticked on 2026-09-26:** keyboard and announcement hold, but the
      screen-reader step showed the field has no accessible name (it was read as "header"). Wrongly ticked
      on 2026-09-25 from the DOM, not from the accessibility tree. Framework-wide cause → [[TASK-494]].
- [x] Tests cover `b-select` alone and the `b-form` → `b-select` wiring (the wiring is what was broken), and
      the suite is proven able to fail against the pre-fix `b-select`.
- [x] Other `creatable: true` single-select fields across consumers are listed (below), so they can be
      re-checked.
- [x] i18n: every new label is a key.

### Consumer sweep (2026-09-25, `creatable` across `C:\Source\Birko`, `node_modules` excluded)

| Where | Kind | Effect of this fix |
|---|---|---|
| `Symbio.UI/src/modules/presentation/navigation/navigation-schemas.ts:12` | `b-form` `select` | the reported field; now works |
| `Symbio.UI/src/modules/presentation/navigation/navigation-page.ts:54` | standalone `<b-select … searchable creatable>`, the menu-group **filter** | now accepts a new group. That looks intended: "Add" pre-fills the form from the filter (lines 429, 445). It is still a visible change to re-check |
| `Symbio.UI/src/modules/{building,tasks,products}/list/list-schemas.ts` | `multi-select` `tags` | unaffected |

## Out of scope

- Symbio changes. Its schema is already correct, and it picks up the fix from source.
- `b-select`'s missing combobox/listbox ARIA and arrow-key option navigation. These gaps predate this fix
  → [[TASK-489]].
- `b-form` ignoring `b-multi-select`'s `create` event → [[TASK-490]].
- Symbio's locale files have no `bwc.select.*` keys, so Slovak users see the English fallback → Symbio
  `TASK-793`.

## Human test plan

- [x] On Symbio, Presentation → Navigation → pick a site → "Add navigation item". Type `footer-2` into
      *Menu group*. The "+ Create “footer-2”" row sits sensibly under any partial matches. Enter commits
      it, save succeeds, and reopening the item shows `footer-2`.
- [x] Same form, press Escape mid-typing: the previous group is restored and nothing is created.
- [x] With a screen reader (NVDA or Narrator), typing a new value announces the create row, and committing
      it announces "Created “…”". Whether it reads well is a judgement the smoke suite cannot make.
- [x] The menu-group **filter** on the same page (`navigation-page.ts:54`): creating a new group there
      behaves in a way the owner finds acceptable.

✅ **Steps 1, 2 and 4 were run by the user on Symbio on 2026-09-25 and passed** ("checked the creatable on
b-select and worked"). **The screen-reader step has not been run**, so the task is parked at `review`, not
`done`. Closing it needs only that pass.

## Progress log

- 2026-09-25 — Implemented. The diff is uncommitted in `Birko/Web` (`b-select.ts`, `b-form.ts`,
  `locales/en.json`, `API.md`, `README.md`) and in `Birko.Web.Playground` (`select-creatable-smoke.ts`,
  `app.ts`, `verify.mjs`).
- 2026-09-25 — `select-creatable-smoke` 29/29, full `verify.mjs` 9/9 suites with 0 failing checks, and
  `tsc --noEmit` clean. Mutation check: with `b-select.ts` stashed, the suite scores 14/29. The `b-form`
  emits-`creatable` check passes and the create-row / commit / reopen checks fail, which reproduces the
  report.
- 2026-09-25 — Committed: Web `26bf8e3` (fix), Playground `c7b1e68` (suite), Framework `11b8cdc3` (task).
- 2026-09-25 — Acceptance criteria ticked against the committed diff, with the evidence in the entries above.
  The owner ran human test steps 1, 2 and 4 on Symbio and they passed. → `review`; the screen-reader step
  is still pending.
- 2026-09-26 — Owner's screen-reader run: Menu group was announced as "header", not by its label. Every
  labelled b-* control is nameless ([[TASK-494]]). The accessible-name criterion is unticked; 488 now depends on 491.
- 2026-09-26 — Screen-reader step run by the owner on the Playground test page after [[TASK-494]]: the field is
  announced as "Menu group", and "Create “…”" / "Created “…”" are announced. All criteria and all four human
  steps are met → `done`.
