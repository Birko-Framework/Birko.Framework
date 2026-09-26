---
id: TASK-490
parent: EPIC-001
feature: FEATURE-001
status: review
priority: P2
assignee: ai
created: 2026-09-25
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# A `creatable` multi-select in `b-form` shows a create row that does nothing unless the page wires it

## Context

Found while fixing [[TASK-488]]. `b-form` emits `creatable` for `type: 'multi-select'` fields
(`Birko.Web.Components/src/inputs/b-form.ts`, `_fieldAttrs`). `b-multi-select` then renders a
"+ Create “…”" row. Clicking it **only emits** a bubbling, composed `create` event with `detail.name` = the
typed text (`b-multi-select.ts`, `_wireCreateOption`). It does not add or select anything; that is left to
whoever listens. `b-form` wires only `change` (`_wireFieldEvents`). So unless the host page listens
for `create` and calls `form.addFieldOption(...)`, the row is visible, clickable and inert. That is the
same no-op shape [[TASK-488]] fixed on the single select.

Symbio works around this three times, near-identically: `modules/{building,products,tasks}/list/list-page.ts`
each put `shadowRoot.addEventListener('create', …)` on the page, call `createTag({ name: e.detail.name })`,
then `form.addFieldOption('tags', opt, true)`. Those listeners filter on nothing but a truthy
`detail.name`. So **any** bubbling `create` from any control on those pages would mint a tag. That is why
[[TASK-488]] deliberately gave `b-select` no `create` event.

Two things need a decision, not just code:

1. **Does `b-form` commit a created multi-select value by default?** One option: add `{ value: name, label:
   name }` and select it, as `b-select` now does. A consumer that needs a server-minted id (Symbio's tags)
   then needs a way to veto or replace it: a cancelable event, an async hook on the field, or setting
   `setFieldOptions` afterwards.
2. **Should `create`'s detail be disambiguated?** Every other `b-*` event uses `name` for the field name.
   `b-multi-select`'s `create` uses it for the typed text. Changing the detail shape is breaking for the
   three Symbio listeners. Per `CLAUDE-maintenance.md` § Breaking changes in a shared project, it would
   need a `CHANGELOG.md` entry naming the member, both shapes and the migration.

## Decisions (owner, 2026-09-26)

1. **Commit by default, and the page can veto.** The create row adds `{ value: typed, label: typed }` and
   selects it, unless a `create` listener calls `preventDefault()`.
2. **Rename, accepting the break**, for a consistent API: `detail` becomes `{ name: <field>, value: <typed> }`.
   The owner asked whether Symbio's listeners are hard to migrate. They are not: three identical listeners,
   three lines each. Symbio had to change anyway, because decision 1 needs the `preventDefault()`.

## Acceptance criteria

- [x] A `creatable` `multi-select` in `b-form` either commits the typed value with no page code, or the
      create row is not offered unless something handles it. It is never a visible no-op.
- [x] A consumer can still substitute a server-created option (Symbio's tag flow keeps working, with
      that page code migrated if the API changes).
- [x] If `create`'s detail shape changes, a `CHANGELOG.md` breaking-change entry covers it, and Symbio's
      three listeners are migrated or filed.
- [x] `b-multi-select`'s container stops announcing a menu: `aria-haspopup="true"` is a menu, and its popup is a
      group of checkboxes (verdict recorded on [[TASK-489]]).
- [x] Playground smoke covers `b-form` → `b-multi-select` create, both with and without a page listener,
      and is proven able to fail.

## Out of scope

- The single select, which [[TASK-488]] fixed.
- Keyboard navigation in `b-multi-select` (see [[TASK-489]]'s verdict).

## Human test plan

- [ ] On Symbio products/list, create a new tag from the form's *Tags* field. It is created once, appears
      as a chip with its server colour, and saves.

## Progress log

- 2026-09-26 — `b-multi-select`: `create` is dispatched cancelable with `{ name: <field>, value }`; with no
  veto, `addOption(value, true)` commits it. `aria-haspopup="dialog"` and the popup is `role="dialog"`
  (it was `"true"`, a menu, over a `role="group"`). `b-form`'s `FormField.creatable` doc, `API.md` (the
  `create` row was missing entirely) and the README are updated. The TASK-488 comment in `b-select` that
  argued from the old `detail.name` meaning is corrected.
- 2026-09-26 — **Found while testing: `addOption` pushed into the caller's array.** `setOptions` keeps
  the reference, and in a `b-form` that is the schema's `options`, so one form's created value leaked
  into every other instance built from that schema. The smoke caught it: after the first case created
  "Green", later instances saw an exact match and offered no create row. It now copies. It is fixed here
  because decision 1 routes every create through `addOption`, which made the leak reachable by default.
- 2026-09-26 — `Birko.Web.Playground` `multi-select-create-smoke` 14/14 (5/14 against the pre-change
  control): detail shape, cancelable, commit with no veto, veto plus the listener's own option (one chip),
  a `b-form` with no page code, the caller's array not mutated, dialog semantics. `verify.mjs` 0 failing,
  `a11y-name-check` 23/23, `a11y-description-check` PASS, `tsc` clean.
- 2026-09-26 — `CHANGELOG.md` BREAKING entry: both shapes, the migration, and the fact that **no compiler
  flags it** (an untyped `detail`). A search of all checkouts under `C:\Source\Birko` found only Symbio's
  three listeners, migrated in a Symbio worktree on `main` (`e.detail.name === 'tags'` filter,
  `preventDefault()` before the first `await`, read `value`). Symbio UI `tsc --noEmit` clean. The manual
  Symbio tag step is pending → `review`.
