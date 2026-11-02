---
id: TASK-803
parent: EPIC-001
feature: null
status: in-progress
priority: P1
assignee: ai
created: 2026-11-02
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Shell's category ribbon: each module is a split button whose menu lists its pages

Asked for by Symbio (TASK-1027), to adopt the Office-like `b-ribbon` (FEATURE-019) in the Shell.

## Context

`buildCategoryRibbon` (`Birko.Web.Shell/src/modules/ribbon-builder.ts`) builds one tab per category with two
groups: `_modules` (one plain link button per module, to its first page) and the **active** module's pages. So a
page of another module is two clicks away (switch module, then pick the page), and the panel's second group changes
whenever the module does. FEATURE-019 shipped `RibbonItem.menu` / `split` (TASK-732) and `description` (TASK-738);
the builder uses neither.

`RibbonMenuItem` has no `href`: a menu choice only emits `item-click` with `parentId` = the module item and
`itemId` = the choice. So the Shell must navigate on it.

## Acceptance criteria

- [x] `buildCategoryRibbon` gives a module with **two or more** visible pages a `split` item: the button keeps
      today's link to the first page, its menu lists every visible page (label, icon), grouped by
      `ModuleOption.group` with separators. A module with one visible page stays a plain link button.
- [x] `actionOnly` options appear in the menu and emit `item-click` as today; navigable ones are navigated by
      `b-app-shell` (`item-click` with a `parentId` whose choice id maps to a route), without the consumer writing
      the routing.
- [x] `ModuleManifest.description` / `descriptionKey` and `ModuleOption.description` / `descriptionKey` (optional),
      resolved through the label resolver and passed to `RibbonItem.description` (tooltip).
- [x] The active module's pages group stays (an option the builder takes, default on). Symbio ruled for this layout
      (prototype variant B, 2026-11-02): split ▾ on every module **and** the active module's pages group. Existing callers compile and render as before apart from the split ▾.
- [x] `b-ribbon` renders a split button's main part as a link (`<a href>`) when the item has `href` and no `action`,
      like a plain command: today it is always a `<button>` (`b-ribbon.ts` `_renderItem`, the `item.split` branch), so
      turning a module button into a split button loses middle-click / open in new tab. Found by the prototype
      2026-11-02.
- [x] Menu item ids are stable and unique across the tab (`<moduleId>:<optionId>`), so key tips and tests can
      address them.
- [x] Playground: a category ribbon smoke — split part navigates to the first page, a menu choice navigates to its
      page, an action choice emits `item-click`, keyboard (↓ opens the menu, Enter picks). Shell README and
      CHANGELOG entry; version bump.

## Out of scope

- Contextual tabs from the base pages — TASK-804.
- Symbio's own adoption — Symbio TASK-1027.

## Human test plan

- [ ] In the Playground shell, hover a category tab, open a module's ▾ and pick its third page. Expected: that page
      opens, the module becomes active, the ribbon stays where it was (pinned or not).

## Decision (the user, 2026-11-02)

**Menu choices are real links**, not routed by `b-app-shell`. `b-app-shell` never sees the modules or their routes (the
consumer's subclass builds the tabs), so "the Shell navigates on `item-click`" would have needed a route table passed
around. Instead `RibbonMenuItem` gains an optional `href`: such a choice renders as `<a role="menuitem" href>`, navigates
by itself, opens in a new tab on a middle-click (the same reason the split main part becomes a link), and still emits
`item-click`. This replaces the second criterion's mechanism ("navigated by `b-app-shell`"); its outcome (navigable
choices navigate without the consumer writing the routing; `actionOnly` ones emit `item-click`) is unchanged.

## Implementation plan

Birko.Web worktree `Web.TASK-803` (branch `task/TASK-803`); Web main stays untouched for Symbio's dev server.

- `b-ribbon`: `RibbonMenuItem.href` → `<a>` menuitem in the panel menu and the phone list; Space on a link choice
  (`_menuKey`); split main part `<a>` when `href` and no `action`.
- `ribbon-builder.ts`: `buildCategoryRibbon(..., options?: CategoryRibbonOptions)` with `moduleMenus` and
  `activePagesGroup` (both default true); a module with ≥ 2 visible pages becomes `split` with a menu of
  `<moduleId>:<optionId>` choices, separators between `ModuleOption.group`s; `description` / `descriptionKey` on
  `ModuleManifest` and `ModuleOption`, resolved through the label resolver, on module and page items (also in
  `buildRibbon`).
- Playground (branch `task/TASK-803`): a "Category ribbon (Shell)" card driven by the hash, `category-ribbon-smoke`
  (builder output and render), `category-ribbon-check.mjs` (real mouse, keyboard, middle-click).
- Docs: Shell README, Components API.md and CLAUDE.md, CHANGELOG v0.4.1 (not breaking: existing callers compile; the
  visible change is the ▾), versions.

## Built (2026-11-02), on branches, not merged

Birko.Web `task/TASK-803` a897b0a (worktree `Web.TASK-803`, v0.4.1); Playground `task/TASK-803` 73f467d.

**Proof.** Playground `category-ribbon-smoke` 25/25 (builder output: split from two pages, plain for one, ids, links,
no link on an action, separators, descriptions and the key, pages group on/off, `moduleMenus: false`; render: split
main `<a>`, choice `<a role=menuitem>`, action `<button>`, a split with no link keeps a `<button>`, item-click with
`parentId`, a choice navigates, an action does not, phone list links). `category-ribbon-check.mjs` 14/14 with a real
mouse and keyboard (split part to the first page, ▾ and a pick of the third page, the module becomes active, a pinned
ribbon stays open, Alt+↓ / ↓ / Enter, Space, an action choice, middle-click on the split part and on a choice opens a
new tab, no page errors). Nine mutations, each caught. Shell and Components `tsc` clean; ribbon-a11y-check 22/22,
entry-points-check 427/427, locale-keys-check 23/23, tree-drag-check 17/17, subpath, a11y-name, a11y-description and
rerender-leak checks pass.

**Not green, and not this change:** `verify.mjs` (5 failing on the branch: ribbon-overflow, carousel drag, gallery
focus), `keyboard-check.mjs` (key-tip section) and `sortable-drag-check.mjs` (auto-scroll, 11/13) fail the same way on
**Playground main with Birko.Web main** (verify.mjs 26 failing there: focus checks across sortable, modal, tree-sort,
bulk-bar). They passed at TASK-789 (2026-10-30), so the browser environment on this machine changed;
ribbon-overflow-smoke passes alone (16/16). Measured 2026-11-02.

Pending: the human test in the Playground, then Symbio told, merge, tag v0.4.1 and push at the user's word.
