---
id: TASK-816
parent: EPIC-001
feature: null
status: verify  # built on branches, human test pending
priority: P2
assignee: ai
created: 2026-11-05
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-ribbon: a tab whose one command is a split button has no panel, so hovering it does nothing

Found by the user in Symbio (2026-11-05): of five category tabs (Služby, Obchod, Prevádzka, Infraštruktúra,
Administrácia), Služby and Administrácia did not react to hover, pinned or unpinned.

## Context

`_isPanelless` drops a tab's panel when its only command is a link, since clicking the tab already goes there. It
tested only `href` and `action`. Since TASK-803, `buildCategoryRibbon` makes a module with two or more pages a split
button: it keeps the link to its first page and adds a ▾ menu of every page, and that menu lives only in the panel. A
category holding one such module was judged panelless, so hovering its tab previewed nothing and the module's pages
menu could not be reached.

Measured on Symbio (`localhost:3000`, sk): Administrácia holds one item, `users`, a split button with 6 pages,
`_isPanelless` true. Služby holds a single module in the user's tenant (the measured admin tenant has more there).
Playground: `category-ribbon-smoke` with the demo's Users module given a second page: hovering Admin left the Business
panel up (29/31 on Birko.Web main, the two new checks red).

## Acceptance criteria

- [x] Reproduced first: a smoke check red on main.
- [x] A one-item tab is panelless only when that item is a plain link: no ▾ menu, toggle, gallery or field.
- [x] A category with one one-page module keeps no panel (checked).
- [ ] Human test in Symbio.

## Out of scope

- `noPanel` (explicit) and the context-action rule: unchanged.

## Human test plan

- [ ] Symbio, a Symbio bundle built against `task/TASK-816`: hover Administrácia — the panel shows Users with its ▾;
      the ▾ lists the user pages. Same for Služby. Pinned and unpinned.

## Built (2026-11-05), on branches, not merged

- Birko.Web `task/TASK-816` (worktree `C:\Source\Birko\Web-TASK-816`): `_isPanelless` checks the item is a plain link.
- Playground `task/TASK-816` (worktree `C:\Source\Birko\Consumers\Birko.Web.Playground-TASK-816`):
  `category-ribbon-smoke` section 4, three checks. verify.mjs 0 failing (category-ribbon-smoke 31/31);
  category-ribbon-check 19/19, keyboard-check 225/225, ribbon-a11y-check 22/22, contextual-ribbon-check 7/7.
  `tsc --noEmit` on Birko.Web.Components clean.
