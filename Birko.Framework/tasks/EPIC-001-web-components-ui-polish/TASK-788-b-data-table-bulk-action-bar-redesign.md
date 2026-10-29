---
id: TASK-788
parent: EPIC-001
status: todo
priority: P2
assignee: ai
created: 2026-10-29
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# b-data-table's bulk action bar needs a redesign — look and placement

## Context

Asked by the user on 2026-10-29 while walking Symbio TASK-500 (bulk add and remove of product categories). The flow
worked; the verdict was "we need to redesign the bulk action bar", and asked whether that is framework or Symbio: **the
bar's look and placement** → framework.

**Today** (`Birko.Web.Components/src/data/b-data-table.ts`, `.bulk-bar` styles ~152–170, render ~331–356, handlers
~416–430):
- It renders **inside the table's footer**, beside pagination: `position: sticky; bottom: 0`, so it sits at the foot of
  the grid, which may be far from where the user selected rows and from the header checkbox.
- It shows a small grey `{count} {selected}` and plain bordered text buttons (`.bulk-btn`, optional `.danger`).
- It has no primary emphasis, no icons by default, no way to clear the selection from the bar, no overflow for many
  actions, and no visual change marking that the table is now in selection mode.

It is shared: **every** list page in every consumer that passes `bulkActions` gets this bar. In Symbio that includes
products (publish, unpublish, add to categories, remove from categories) and other admin lists.

## Acceptance criteria

- [ ] **Placement decided and built, with a clickable preview first.** Options to weigh:
  - a sticky bar at the **top** of the table that replaces the toolbar or header while rows are selected (the Gmail
    and Google Drive pattern);
  - a floating bar anchored to the bottom of the **viewport**, centred (the Notion and Linear pattern);
  - kept in the footer, restyled.

  It is always visible while rows are selected, whatever the scroll position, and does not cover the last rows or the
  pagination.
- [ ] **Look:**
  - a clear selection-mode state: an accent background or border, and the count prominent ("12 selected");
  - **Clear selection** (×) on the bar;
  - "Select all N matching" when the whole page is selected and more rows exist, *if* the table's data source can
    support it. Otherwise record why not; Symbio's bulk endpoints take id lists.
  - buttons in the `b-button` style, with an optional icon, a primary variant and a danger variant.
- [ ] **Many actions:** past a breakpoint or count, extra actions go into a "More" overflow menu. Mobile width works.
- [ ] **Accessibility:**
  - the bar is a labelled region (toolbar role);
  - the count change is announced in a polite live region;
  - keyboard: Tab reaches the bar's actions, and Escape clears the selection.
- [ ] **Compatible:** `bulkActions` (`{ id, label, icon?, variant?, confirm?, confirmMessage? }`) and the `bulk-action`
      event keep working. New options are additive.
- [ ] Tokens, dark theme, Playground page and smoke. The smoke covers selection, clear, overflow, and the bar visible
      after scrolling.
- [ ] CHANGELOG entry and package version bump.

## Out of scope

- What a consumer's action opens. Symbio's "add to categories" chooser is Symbio's.
- Server-side "select all matching" endpoints in consumers.

## Human test plan

1. Playground: select rows and scroll. The bar stays in view and the count is prominent; × clears the selection.
2. Give the bar six or more actions: the extra ones overflow into More, and it works at phone width.
3. In Symbio: Products → select products → Add to categories. The bar reads as the obvious next step.
