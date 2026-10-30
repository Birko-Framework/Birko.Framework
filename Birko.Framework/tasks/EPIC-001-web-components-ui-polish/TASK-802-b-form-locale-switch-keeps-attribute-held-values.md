---
id: TASK-802
parent: EPIC-001
status: done
priority: P1
assignee: ai
created: 2026-10-30
depends-on: [TASK-797]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# A locale switch clears a b-form's select, checkbox, switch and pickers

## Context

Found by Symbio's gate 3 for TASK-1010 on Birko.Web v0.2.0 (1c6de6d). Presentation → pages → blocks editor, a
ProductGrid block: `filterMode` is a required `select` whose option labels come from `t()`, set with
`setSchema(() => getBlockSchema('ProductGrid', …))` and `setValues({ filterMode: 'filter', … })`. After en → sk the
labels relabel and the modal stays open, but `filterMode` is `""` (Symbio `tests/ui-e2e/presentation-block-locale.spec.ts`).
A required select then blocks saving; an optional one silently saves `""`.

Measured in the Playground on v0.2.0: wider than the report, and not limited to factory schemas. A select with a
placeholder, an option group, a checkbox, a switch, a date and a time all lose their value; text, number, textarea,
multi-select and tags keep it.

**Cause:** those components keep their value in an attribute (`value`, `checked`) that b-form's rendered markup does
not carry. On a switch b-form's `softUpdate()` morphs its shadow DOM, and the morph removes every attribute the fresh
markup lacks (`base-component.ts` `_morphNode`). The TASK-797 note "the morph keeps values" was wrong for these.

## Fix

b-form's same-shape `softUpdate()` path snapshots each field's displayed value before the morph and, after it, writes
back each one that changed (`_restoreFields`, with `_settingValues` set, so no `change` fires). A field the morph left
alone is not written, so the one being typed in keeps its caret.

## Human test plan

1. Playground (`node serve.mjs`, port 5180), Lab → "Form relabels on a locale switch", left form: pick Blue and tick the
   checkbox, switch Locale → SK, then back. Blue stays selected and the box ticked both times (on v0.2.0 both cleared).
   The card's colour select gained a placeholder and a second option, and a checkbox was added, for this.
2. Symbio: run `tests/ui-e2e/presentation-block-locale.spec.ts` (the ProductGrid test) against v0.2.1, and walk it: open
   a ProductGrid block, set "Filter", switch to Slovak; the select still shows the filter mode and the block saves.

## Resolution (2026-10-30)

- Birko.Web **v0.2.1**: `b-form.ts` `_fieldSnapshot` / `_restoreFields`; CHANGELOG v0.2.1; Components CLAUDE.md's
  TASK-797 entry corrected.
- Playground: `locale-switch-check.mjs` gains a TASK-802 section: a form with every field type set by `setValues()`,
  factory and plain, en → sk → en; `getValues()` must be unchanged. 43/43. With the `_restoreFields` call removed the
  4 new checks fail (select with placeholder, option group, checkbox, switch, date, time lost).
- rerender-leak PASS; entry-points 427/427; Components tsc clean.

**Not covered, found on the way:** a `radio` field is absent from `getValues()` altogether (before any switch). Left
out of the new check; not this task.

**Released 2026-10-30:** Birko.Web **v0.2.1** (2bddcec), tagged and pushed.

**Human test:**
1. Playground: PASS (the user, 2026-10-30): Blue and the ticked box stay through SK and back.
2. Symbio: PASS: `presentation-block-locale.spec.ts` 3/3 on v0.2.1 (ProductGrid keeps `filterMode` and its
   categories), the 31 block-editor specs green. Symbio uses no `radio` field, so the gap noted above is not filed.

**Closed 2026-10-30 by the user.**
