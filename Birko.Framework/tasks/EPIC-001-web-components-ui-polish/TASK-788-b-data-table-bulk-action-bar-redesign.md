---
id: TASK-788
parent: EPIC-001
status: verify
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

- [x] **Placement decided and built, with a clickable preview first.** Options to weigh:
  - a sticky bar at the **top** of the table that replaces the toolbar or header while rows are selected (the Gmail
    and Google Drive pattern);
  - a floating bar anchored to the bottom of the **viewport**, centred (the Notion and Linear pattern);
  - kept in the footer, restyled.

  It is always visible while rows are selected, whatever the scroll position, and does not cover the last rows or the
  pagination.
- [x] **Look:**
  - a clear selection-mode state: an accent background or border, and the count prominent ("12 selected");
  - **Clear selection** (×) on the bar;
  - "Select all N matching" when the whole page is selected and more rows exist, *if* the table's data source can
    support it. Otherwise record why not; Symbio's bulk endpoints take id lists.
  - buttons in the `b-button` style, with an optional icon, a primary variant and a danger variant.
- [x] **Many actions:** past a breakpoint or count, extra actions go into a "More" overflow menu. Mobile width works.
- [x] **Accessibility:**
  - the bar is a labelled region (toolbar role);
  - the count change is announced in a polite live region;
  - keyboard: Tab reaches the bar's actions, and Escape clears the selection.
- [x] **Compatible:** `bulkActions` (`{ id, label, icon?, variant?, confirm?, confirmMessage? }`) and the `bulk-action`
      event keep working. New options are additive.
- [x] Tokens, dark theme, Playground page and smoke. The smoke covers selection, clear, overflow, and the bar visible
      after scrolling.
- [x] CHANGELOG entry and package version bump.

## Decisions (2026-10-29, the user, on a clickable preview)

Preview: https://claude.ai/artifact/8Rq7YCNv6cKhjrhgSqHdgb

1. **Placement: B, floating**, chosen over C (the footer, restyled) for the look; built anchored to the table, not the
   window: just above the sticky footer, centred on the table, so it never covers the pagination or leaves the table.
2. **"Select all N matching": yes, opt-in** (`selectAllMatching`). Client-paged it selects every id; server-paged it is
   a flag and `bulk-action` carries `allMatching` with `filters` and `params`. Off by default, so Symbio (id lists) is unchanged.
3. **As many actions as fit**, measured; the rest in More; `danger` always last.
4. **`b-confirm-dialog` replaces `window.confirm()`.**
5. **`variant: 'primary'`** on a bulk action, new and additive.
6. **Phone width, from the user walking it in Chrome at 390px:** no overlap of More and ×; with "Select all" or long
   translations the bar wraps to two rows; the footer is inset like the cells; "24 items" stays on one line.

## Resolution (2026-10-29)

Birko.Web `2804501` (joins unpushed v0.1.22), Playground `7a3ef7b`. Playground `bulk-bar-smoke` 41/41 in Chromium and
Firefox; planted defects each fail it (no fit, unescaped labels, `window.confirm`, no Escape, no allMatching flag, the bar
in the footer, no wrap, a fixed room, the page-size option). Walked in Chrome at a 390px phone frame, with Slovak labels.
verify.mjs 0 failing; device-fix 140/140, keyboard 222/222, entry-points 427/427, locale-keys 22/22, a11y-name 43.
Also fixed on the way: labels were inserted as raw HTML; `b-pagination`'s page-size box was empty for a size not in its
defaults; `undo-delete-smoke` missed the toast container inside another suite's open modal (since TASK-786).

**Symbio:** `.bulk-btn[data-bulk]` still selects an inline action; one in More is `[hidden]`. Told symbio-9e the commit
and the new `bwc.dataTable.*` keys.

## Out of scope

- What a consumer's action opens. Symbio's "add to categories" chooser is Symbio's.
- Server-side "select all matching" endpoints in consumers.

## Human test plan

1. Playground: select rows and scroll. The bar stays in view and the count is prominent; × clears the selection.
2. Give the bar six or more actions: the extra ones overflow into More, and it works at phone width.
3. In Symbio: Products → select products → Add to categories. The bar reads as the obvious next step.

## Human test, round 1 (2026-10-29, the user)

**Failed, fixed:** ticking 2 rows, × , then another row: the 2 stayed ticked on screen, and the table grew laggy.
Each re-render added another set of listeners to the checkboxes, row menus and editable cells b-table keeps (the
morph), so a click ran its handler many times; and × changed only the attribute of a box the user had clicked.
Pre-existing, so every selectable list page in every consumer degraded with use. Birko.Web `a4bb32c`, Playground
`1927205`: one AbortController per wiring, ticks set from the selection. bulk-bar-smoke 44/44 (5 rounds of real
clicks; red before). Awaiting the user's re-test of section C.

## Human test, round 2 (2026-10-29, the user)

Section C (Playground, desktop) passed on Birko.Web a4bb32c: × clears the ticks, the count follows them, no lag.
Phone (D) and Symbio (E) still to run.
