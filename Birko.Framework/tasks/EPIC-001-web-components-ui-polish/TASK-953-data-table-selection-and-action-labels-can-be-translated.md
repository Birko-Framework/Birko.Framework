---
id: TASK-953
parent: EPIC-001
feature: null
status: verify
priority: P2
assignee: ai
created: 2026-11-21
depends-on: []
blocks: []
findings: [FIELD-054]
pr: null
github-issue: null
jira-key: null
---

# `b-data-table`'s selection and row-action labels can be translated

## Context

Reported by the Symbio session, 2026-11-21, right after [[TASK-952]], from the same Czech and Slovak test (Symbio
TASK-1110). Every label in `b-data-table.ts` goes through `_l(…, 'bwc.dataTable.…', default)` except the select-all
checkbox (`labels.selectAll ?? 'Select all rows'`) and the select-row checkbox (`labels.selectRow ?? 'Select row'`).
Symbio builds table configs in many places, so passing labels per table is not an option. Found while fixing: the
row-action `⋮` button (`labels.actions ?? 'Actions'`, from [[TASK-940]]) has the same gap, and its `data-id` and label
went into the markup unescaped, so a row id containing `"` broke out of `data-id`.

## Change (Birko.Web v0.4.20)

- The three labels go through `_l`: config label > `bwc.dataTable.selectAll` / `selectRow` / `actions` > English.
- The three keys added to `locales/en.json`; API.md's data-table label list and the CHANGELOG updated.
- The row-action button's `data-id` and label are escaped.

## Evidence

Playground `i18n-message-smoke`: the English and config-label checks pass before and after; the three translation
checks and the quoted-id check fail on the branch before the fix and pass with it (33/33 twice). verify 0 failing
(backport 400/400); locale-keys 23/23; locale-switch 47/47; contextual-ribbon 13/13; keyboard 225/225; a11y-name OK;
rerender-leak PASS; Components `tsc` clean.

## Acceptance criteria

- [x] With no translation and no config label, the three labels are the English that shipped.
- [x] `bwc.dataTable.selectAll`, `selectRow` and `actions` translate them; a config label still wins.
- [x] A row id containing a quote stays one `data-id` value.

## Out of scope

- Shipping Czech or Slovak translations with Birko.Web.

## Human test plan

1. In Symbio, with the three keys in its cs and sk bundles, open a selectable table with row actions in Czech, then
   Slovak: the accessibility tree names the select-all and row checkboxes and the `⋮` button in that language.
