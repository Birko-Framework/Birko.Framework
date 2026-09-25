---
id: TASK-491
parent: EPIC-001
feature: null
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

# `b-multi-select` hard-coded its English labels, so no consumer could translate a `b-form` multi-select

> **Filed after the code was written (2026-09-25)**, from a consumer's human test (Symbio TASK-793,
> item 3: "a *Tags* field's search, remove and create labels are Slovak"). The acceptance list is that
> test's own requirement, not a transcript of the implementation.

## Context

`b-multi-select` rendered five strings from `this.attr('label-…', '<English>')` only: *Search...*,
*No matches*, *Remove {option}* (the chip button's accessible name), *Create “{value}”* and *Options*
(the dropdown's accessible name when the field has no label). The only way to translate them was a
`label-*` attribute — and **`b-form` gives a consumer no way to pass one per field**. Every multi-select a
consumer builds through `b-form` (`type: 'multi-select'`; Symbio has 22, including every *Tags* field) was
therefore English in every locale.

`b-select` (`bwc.select.create`, TASK-488) and `b-date-picker` (`bwc.datetime.today`) already resolve
their own text through `t()`; `b-multi-select` was the control that did not.

## Change (Web fea5338)

- One helper, `_text(attr, key, fallback)`: an explicit `label-*` attribute, then the `bwc.multiSelect.*`
  key, then English — the documented resolution order for every control.
- Keys `bwc.multiSelect.{noMatches,search,remove,options}` added to `Birko.Web.Components/locales/en.json`.
- The create row: with an explicit `label-create` it keeps its old shape (prefix + English quotes);
  otherwise it renders **the same `bwc.select.create` template b-select renders**, so a locale also owns
  the quotation marks (`„…“` in Slovak) and both controls read identically.

## Acceptance criteria

- [x] Under a non-English locale that defines the keys, a `b-form` multi-select renders its search
      placeholder, no-matches row, chip remove label and create row in that locale, with no attribute set.
      → measured headless (Playwright) against Symbio's admin build, sk vs en: `Hľadať...` / `Search...`,
      `Žiadne zhody` / `No matches`, `Odobrať Alpha` / `Remove Alpha`, `+ Vytvoriť „…“` / `+ Create “…”`;
      no page errors.
- [x] An explicit `label-*` attribute still wins over the key (resolution order unchanged).
- [x] With no keys defined, the English fallbacks are byte-identical to before (`Search...`,
      `No matches`, `Remove`, `Options`); the create row reads `Create “{value}”` as b-select's does.
- [ ] Playground smoke covers the key path and is proven able to fail.

## Out of scope

- The create *behaviour* of a `b-form` multi-select — [[TASK-490]].
- Keyboard navigation — [[TASK-489]].

## Human test plan

- [ ] In Symbio's admin with the language set to SK, open any *Tags* field: the search box, "no matches",
      the create row and the chip's remove button (screen reader / hover) are Slovak.
