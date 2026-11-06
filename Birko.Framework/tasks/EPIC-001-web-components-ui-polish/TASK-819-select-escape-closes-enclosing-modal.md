---
id: TASK-819
parent: EPIC-001
feature: null
status: done
priority: P1
assignee: ai
created: 2026-11-06
depends-on: []
blocks: []
related: [TASK-818, TASK-817]
findings: []
pr: "Birko.Web de43a95 (merge), a9e0941 v0.4.10"
github-issue: null
jira-key: null
---

# Escape in an open `b-select` / `b-multi-select` list inside a modal closes the modal too

## Context

Found by Symbio TASK-1068 (reported by symbio-32, 2026-11-06), the day v0.4.9 shipped `CountrySelectPO`
([[TASK-818]]). `eshop-shipping-methods.spec.ts:87` picked a country in a `multiple` `b-country-select` inside
Symbio's create dialog (a `b-modal`, native `<dialog>` with `showModal()`). The page object pressed Escape on the
picker's box to close the list, and the dialog closed with it, discarding the form: the next `#btn-save` click timed out.

The same thing happens to a person: Escape in an open list closes the list and the whole dialog with it. Both
selects closed their list on Escape but neither prevented nor stopped the key, so the `<dialog>` took it as `cancel`.
And `b-multi-select` handled Escape only on its box, so from its own search field or an option (where focus is while
the list is open) Escape did nothing to the list and closed the dialog. The date, time and sortable pickers already
stop an Escape that closes their panel.

## Acceptance criteria

- [x] With a list open, Escape closes the list and nothing else, from `b-select`'s search box and from
      `b-multi-select`'s box, search field and options; focus returns to the multi-select's box.
- [x] With no list open, Escape still reaches the dialog (closes the modal).
- [x] `CountrySelectPO`'s multiple pick inside a `b-modal` leaves the modal open.
- [x] Symbio's `eshop-shipping-methods.spec.ts` passes (symbio-32 reran it: 12/12 with customers.extra and customers.more, retries=0, Symbio UI rebuilt on Web main a9e0941, 2026-11-06).

## Human test plan

In Symbio, open a create dialog with a multiple country field (e-shop shipping method). Open the country list, type in
its search, press Escape: the list closes, the dialog stays. Press Escape again: the dialog closes. Same with a
single searchable select.

## Outcome

Playground `escape-in-modal-check.mjs` (real Playwright, real `b-modal`): 6/13 before the fix, reproducing the Symbio
failure in its `CountrySelectPO` row; 13/13 after. `country-select-po-check` 15/15, `keyboard-check` 225/225,
`verify.mjs` all suites green, `Birko.Web.Components` typecheck clean.

Closed 2026-11-06 on Symbio's rerun. The human test plan above was not run by a person; Symbio's Playwright specs drove the real
create dialog.
