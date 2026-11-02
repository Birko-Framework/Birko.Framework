---
id: TASK-803
parent: EPIC-001
feature: null
status: todo
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

- [ ] `buildCategoryRibbon` gives a module with **two or more** visible pages a `split` item: the button keeps
      today's link to the first page, its menu lists every visible page (label, icon), grouped by
      `ModuleOption.group` with separators. A module with one visible page stays a plain link button.
- [ ] `actionOnly` options appear in the menu and emit `item-click` as today; navigable ones are navigated by
      `b-app-shell` (`item-click` with a `parentId` whose choice id maps to a route), without the consumer writing
      the routing.
- [ ] `ModuleManifest.description` / `descriptionKey` and `ModuleOption.description` / `descriptionKey` (optional),
      resolved through the label resolver and passed to `RibbonItem.description` (tooltip).
- [ ] The active module's pages group stays (an option the builder takes, default on). Symbio ruled for this layout
      (prototype variant B, 2026-11-02): split ▾ on every module **and** the active module's pages group. Existing callers compile and render as before apart from the split ▾.
- [ ] `b-ribbon` renders a split button's main part as a link (`<a href>`) when the item has `href` and no `action`,
      like a plain command: today it is always a `<button>` (`b-ribbon.ts` `_renderItem`, the `item.split` branch), so
      turning a module button into a split button loses middle-click / open in new tab. Found by the prototype
      2026-11-02.
- [ ] Menu item ids are stable and unique across the tab (`<moduleId>:<optionId>`), so key tips and tests can
      address them.
- [ ] Playground: a category ribbon smoke — split part navigates to the first page, a menu choice navigates to its
      page, an action choice emits `item-click`, keyboard (↓ opens the menu, Enter picks). Shell README and
      CHANGELOG entry; version bump.

## Out of scope

- Contextual tabs from the base pages — TASK-804.
- Symbio's own adoption — Symbio TASK-1027.

## Human test plan

- [ ] In the Playground shell, hover a category tab, open a module's ▾ and pick its third page. Expected: that page
      opens, the module becomes active, the ribbon stays where it was (pinned or not).

## Implementation plan

_Populated by `/tasks plan TASK-803` — leave empty until then._
