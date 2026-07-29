---
id: TASK-097
parent: STORY-049
feature: null
status: review  # todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
priority: P1
assignee: ai
created: 2026-07-29
depends-on: []
blocks: [TASK-099]
pr: null
github-issue: null
jira-key: null
---

# Make the existing ribbon overflow reachable (interim fix, both skins)

## Context

Field report 2026-07-29: on a narrow window the ribbon shows fewer items and there is no way to
reach the rest. This task closes the **unreachable-command defect** with the cheapest correct fix,
ahead of the Office-style scaling work (TASK-098 → TASK-100) which is a design change with a much
longer tail.

The two implementations fail differently:

**`Birko.Xaml.Avalonia/Controls/Ribbon.cs` — no overflow handling at all.**
- `Ribbon.cs:47` + `:79-83` — the tab strip is a horizontal `StackPanel` placed in a `*` Grid column.
  Tabs past the right edge are clipped with no scroll.
- `Ribbon.cs:92` — the groups row is a horizontal `StackPanel` with `Margin(8)`, same problem.

There is no `ScrollViewer` anywhere in the control. The tree is built imperatively in `Rebuild()`,
which has no width constraint to reason about — so nothing measures available space today.

**`Birko.Web.Components/src/nav/b-ribbon.ts` — tabs handled, panel not.**
- The tab strip already has working scroll arrows (`_setupTabScroll`, `b-ribbon.ts:698-717`).
- `.ribbon-panel-inner` (`b-ribbon.ts:150-153`) is `overflow-x: auto` **with `scrollbar-width: none`
  and no arrow buttons**. Overflowing groups are scrollable in theory and invisible in practice: no
  scrollbar, no chevrons, no hint that more exists. A mouse without a horizontal wheel cannot reach
  them.
- Secondary bug in the part that works: `_setupTabScroll` binds `scroll` but never `resize` /
  `ResizeObserver`, and `updateArrows` otherwise runs once per render (called from `onUpdated`).
  Shrink the window without triggering a re-render and the right arrow stays hidden while the tabs
  overflow.

**This fix is deliberately interim on the groups row.** STORY-049 decided that the ribbon body must
**scale, not scroll** (spatial memory is the ribbon's whole point; `»` overflow is a *toolbar*
pattern). TASK-099 therefore **removes the groups-row scroll container again** once the degrade pass
lands. It is still worth landing on its own: it makes commands reachable now, and 098–100 have a much
longer tail. The tab-strip scrolling added here is **permanent** — tabs are the documented exception
and do scroll, as in Office Web / Fluent.

Reuse, don't reinvent: the web side already has the `.ribbon-scroll-btn` class + `updateArrows`
pattern; extract it so the tab strip and the panel share one implementation rather than growing a
second copy.

## Acceptance criteria

- [x] **Avalonia tab strip** scrolls horizontally when the tabs exceed the available width, with a
      visible affordance, and the collapse chevron at `Ribbon.cs:68-77` stays pinned and never scrolls
      out of reach.
- [x] **Avalonia groups row** scrolls horizontally when the active tab's groups exceed the available
      width, so every group is reachable at a narrow window width.
- [x] Neither Avalonia scroller shows a **vertical** scrollbar, and the ribbon's height is unchanged
      at a wide width (no layout shift introduced for the common case).
- [x] **Web `.ribbon-panel-inner`** gains a visible scroll affordance (chevron buttons matching the
      tab strip's) that appears only when the panel actually overflows.
- [x] The tab-strip and panel scroll logic share **one** extracted helper — no second copy of
      `updateArrows`.
- [x] Arrow visibility re-evaluates on **resize** (`ResizeObserver` on the scroll container), not only
      on `scroll` and re-render — for both the tab strip and the panel.
- [ ] Below the 48rem breakpoint nothing regresses: the hamburger dialog still takes over and no
      scroll buttons appear (the existing `!important` rule at `b-ribbon.ts:276`).
      **Not automated** — the media query keys off *viewport* width, which the smoke (which resizes the
      host element) cannot exercise. Reasoned safe from CSS order: the `@media (max-width: 48rem)` block
      is last in the sheet, so its `.ribbon-scroll-btn { display: none !important }` covers the two new
      buttons (same class) and its `.ribbon-panel { display: none }` beats the base rule's new
      `display: flex`. Left to the human test plan below.
- [x] Tests: `RibbonTests.cs` gains coverage that a narrow `Measure`/`Arrange` still yields reachable
      tabs and groups. The suite already measures at an explicit size (`Show<T>` at
      `RibbonTests.cs:42-50`), so shrink that to a width that forces overflow and assert the scroller
      exists and its extent exceeds its viewport.
- [x] Web side verified by building + headless-running `Birko.Web.Playground` (there is no web unit
      runner yet — that's TASK-052).

## Out of scope

- **Progressive group scaling** (`Large→Medium→Small`) — TASK-099.
- **Group-collapse-to-popup** — TASK-100.
- Any change to `RibbonModels.cs` / the TS `RibbonGroup` interface — TASK-098 owns the model.
- New `--b-ribbon-*` tokens — reuse what exists; TASK-098 adds tokens if the variants need them.
- The `<48rem` mobile hamburger dialog — already works, unaffected.

## Found during review (2026-07-29)

Two things the manual pass turned up, both fixed under this task:

- **The demo ribbons were too thin to review.** Both `Birko.Xaml.Gallery` and `Birko.Web.Playground`
  had 2 tabs / 2 groups, which never overflows at any sane width — so the test plan below had nothing
  to observe. Both now carry Office-sized data (8 tabs, 5–6 groups on the active tab).
- **The chevron strobed on an *unpinned* web ribbon and the click landed on a tab.** `visible` was
  applied only imperatively by `sync()`, but `update()` morphs synchronously and the template's `class`
  attribute overwrote it — so after every re-render the button was `display: none` until the next
  animation frame. While hidden, the flex row reflowed and a tab slid under the cursor. Unpinned makes
  it constant, because hover expand/collapse re-renders on nearly every mouse move across the strip.
  Fixed by making the overflow state real state (`_tabScroll` / `_panelScroll`) that `render()`
  re-emits — the same hazard `_hoverTabId` already documents in that file. Two new smoke checks assert
  the class survives an observed-attribute change synchronously; both fail before the fix.

Also filed from this review, **not** fixed here: **TASK-101** — the Avalonia `Ribbon` has no pinned
concept and a collapsed ribbon permanently re-expands when you click a tab, where Office (and
`b-ribbon`) reveal it temporarily as an overlay and re-collapse after a command.

## Human test plan

- [ ] `Birko.Xaml.Gallery` — open the ribbon showcase, drag the window narrow enough to cut off the
      last tab. Expected: the tab strip scrolls and every tab is reachable; the collapse chevron stays
      visible at the right edge.
- [ ] Same window, still narrow, on a tab with several groups: expected every group reachable by
      scrolling the groups row, and no vertical scrollbar appears.
- [ ] `Birko.Web.Playground` — narrow the browser to ~900px on a ribbon with enough groups to
      overflow. Expected: chevrons appear on the panel; clicking scrolls; they disappear again when
      widened. **Then narrow further without reloading** — the arrows must update on resize alone
      (this is the specific bug being fixed; a reload would mask it).
- [ ] Narrow past 48rem: expected the hamburger dialog takes over and **no** chevrons are visible.
- [ ] Symbio UI (real consumer, many ribbon tabs) — confirm nothing shifted at normal desktop width.

## Implementation plan

Landed 2026-07-29 on `task/TASK-097` in three repos (`Birko.Xaml.Avalonia`, `Birko.Xaml.Avalonia.Tests`,
`Birko.Web.Components`) plus a smoke in `Birko.Web.Playground`. Nothing committed.

**Avalonia** — `Ribbon.cs` gained `WrapScrollable(content, leftTip, rightTip)` + `ScrollChevron(glyph, tip)`.
A `ScrollBarVisibility.Hidden` (horizontal) / `Disabled` (vertical) `ScrollViewer` inside a
`Grid("Auto,*,Auto")` with a chevron in each outer column. Chevrons start `IsVisible = false` so their
`Auto` columns collapse to zero width — no layout cost when everything fits. Visibility syncs from
`ScrollChanged` **and** `LayoutUpdated`; because `Extent`/`Viewport` change on resize, that covers a
narrowing window with no rebuild. A click steps `max(48, Viewport.Width * 0.5)`, clamped to the extent
(matching `b-ribbon`'s `clientWidth * 0.5`). Applied to the tab strip (collapse chevron left outside, in
`strip`'s own `Auto` column) and to the groups row.

*Why `Hidden` and not `Auto`:* an `Auto` bar takes layout space when it appears, so the 34px tab strip and
the ribbon body would each grow ~12px at exactly the widths where space is already scarce — the ribbon's
height would change with the window width.

**Web** — `_setupTabScroll()` → `_setupScroll(track, leftBtn, rightBtn)` returning its sync closure, used
for both `.ribbon-tabs` and `.ribbon-panel-inner`. `.ribbon-panel` became `display: flex` so the new
`#panel-scroll-{left,right}` buttons flank the track; `.ribbon-panel-inner` gained `flex: 1; min-width: 0`.
A single `ResizeObserver` (`_observeScrollTracks`, re-observed each `onUpdated` because a re-render can
replace the elements) calls every registered sync; `_scrollSyncs` is reset each update so aborted-listener
closures don't accumulate. `_showTabContent` calls `_panelSync` after swapping the panel's `innerHTML`,
since different tabs have different group widths. Torn down in `onUnmount`.

**Verification**
- `Birko.Xaml.Avalonia.Tests` — 5 new `RibbonTests` (overflow at 320px on both tracks, chevron click
  scrolls + reveals the back chevron, no chevrons/no vertical bar at 900px, collapse chevron outside the
  tab scroller). Full suite **152/152**; `dotnet build` clean, 0 warnings.
- `Birko.Web.Playground` — new `ribbon-overflow-smoke.ts` (registered behind `?smoke=1`), **16/16**, and
  the rest of `verify.mjs` still green with no page errors.
- **Mutation-checked:** disabling `_observeScrollTracks` + the panel `_setupScroll` call makes exactly the
  6 relevant checks fail (panel affordance ×3, resize-only reveal/retract ×3) and no others — so the new
  assertions are not vacuous.
