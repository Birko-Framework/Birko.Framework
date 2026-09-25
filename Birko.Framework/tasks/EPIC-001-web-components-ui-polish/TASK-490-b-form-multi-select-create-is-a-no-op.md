---
id: TASK-490
parent: EPIC-001
feature: FEATURE-001
status: todo
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

## Acceptance criteria

- [ ] A `creatable` `multi-select` in `b-form` either commits the typed value with no page code, or the
      create row is not offered unless something handles it. It is never a visible no-op.
- [ ] A consumer can still substitute a server-created option (Symbio's tag flow keeps working, with
      that page code migrated if the API changes).
- [ ] If `create`'s detail shape changes, a `CHANGELOG.md` breaking-change entry covers it, and Symbio's
      three listeners are migrated or filed.
- [ ] Playground smoke covers `b-form` → `b-multi-select` create, both with and without a page listener,
      and is proven able to fail.

## Out of scope

- The single select, which [[TASK-488]] fixed.
- Keyboard navigation in `b-multi-select` (see [[TASK-489]]'s verdict).

## Human test plan

- [ ] On Symbio products/list, create a new tag from the form's *Tags* field. It is created once, appears
      as a chip with its server colour, and saves.
