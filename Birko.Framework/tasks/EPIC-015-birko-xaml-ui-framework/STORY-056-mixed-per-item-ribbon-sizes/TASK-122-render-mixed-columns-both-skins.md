---
id: TASK-122
parent: STORY-056
feature: FEATURE-015
status: verify
priority: P2
assignee: ai
created: 2026-07-31
depends-on: [TASK-119, TASK-120]
blocks: [TASK-123]
pr: null
github-issue: null
jira-key: null
---

# Render mixed columns — the CSS grid and the Avalonia panel

## Context

The easy half, and half-built already.

`b-ribbon.ts`'s `size-medium` / `size-small` rules use `grid-auto-flow: column` with
`grid-template-rows: repeat(3, auto)` — that *is* Office's "three stacked per column", and it is what
makes those variants **narrower** rather than merely smaller. A mixed group is one large column
followed by a 3-row column, which that grid can already express. The work is mostly threading the
per-item size to the item and letting a large item span the rows.

The XAML side needs the equivalent in the Avalonia panel. `Ribbon.cs:899` currently branches
`size == RibbonGroupSize.Large ? … : …` when building a group's items — one decision for the whole
group. That becomes per-item (or per-template-slot, per TASK-119).

`_renderItem(tabId, groupId, item)` takes no size today; it will need one.

## Approach

Web: give the item its size as a class alongside the group's, and let a large item span all three grid
rows. The existing `size-*` group classes stay — they are the uniform case and every current consumer
relies on them.

XAML: mirror the column structure rather than the CSS. Two known Avalonia gotchas from STORY-049 apply
directly here:

- **Measuring an `IsVisible = false` control yields zero.** A panel that hides alternatives before
  measuring them under-degrades and clips its last group. This bit the ribbon once already, and the
  tests stayed green because they asserted the *decision* rather than whether the row *fits*.
- **State must survive `Rebuild()`.** Four of the five defects review found in that story were state
  wiped by a re-render — an imperative CSS class removed by a synchronous morph, a discarded scroll
  offset, flyout wiring applied only in `onUpdated`. Anything imperative added here must be
  re-established on rebuild.

And the web equivalent: bind listeners in `onUpdated`, not `onMount` — `BaseComponent._afterRender()`
aborts and replaces its listener `AbortController` on every render, so an `onMount` registration is
detached by the first re-render.

**Port the ARIA discipline too.** The accessibility round on STORY-049 found more than the layout round
did, and every defect was in a control whose behaviour was already correct: no ribbon button had an
accessible name, because Avalonia derives one from `Content` only when it is a *string* and a ribbon
item's content is a panel. A mixed-size item is still a panel. `AutomationProperties.Name` is not
automatic — and a non-empty accessible name is not the criterion, since `Content.ToString()` satisfies
that; assert the name is a string a user would recognise.

## Acceptance criteria

- [x] A group renders one large item beside a column of three small ones, on **both** skins
- [x] The Clipboard example (large Paste + stacked Cut/Copy/Format Painter) renders correctly in the
      playground and the Avalonia gallery
- [x] Uniform-size groups are **byte-identical** to today — no visual diff, asserted by the screenshot
      baseline where one exists
- [x] Mixed groups degrade through the ladder and collapse to `popup` losslessly
- [x] No measurement is taken from a control with `IsVisible = false`
- [x] Every item has a recognisable accessible name at every size, including icon-only `small`
- [x] Keyboard arrow navigation reaches every item in a mixed group, in visual order
- [x] Focus survives `Rebuild()`; imperative state is re-established after a re-render
- [x] Web listeners bound in `onUpdated`
- [x] Playground verifier green; `Birko.Xaml.Avalonia.Tests` green

## Evidence

**Web** (Birko.Web `task/TASK-120` e259f89, v0.5.6 with TASK-120 / 121): a group with an item `size` or `layouts` is drawn
*mixed*: `_renderGroup` renders `ribbonItemSizes(group, step, start)`, the group carries `mixed` and `data-step` instead of
a `size-*` class, and each command a `item-large|medium|small` class on its own root (a split button on both its frame
and its main part). CSS: three equal grid rows, a Large command `grid-row: 1 / -1` with the Large look; the Large split
stacking rules extended to it; the Small gap tightening counts `mixed[data-step=small]`. Any other group takes the old
code path and the same markup, byte for byte. `nearestInDirection` measures Left / Right from a mixed Large command's
top row, so Right from Paste is Cut (was Copy, its middle row). No listener added (rendering only); measurement
unchanged (each step through `_renderGroup`).
**Avalonia** (Framework main): `IsMixed` groups build each step with `MixedColumns` from `RibbonItemSizing.SizesAt` (a
Large command a column of its own across the star rows, stretched, content at the top; the rest three to a column, each
at its own size); `mixed-large` class and the same top-row rule in `NearestInDirection`. Variants still prebuilt and
parked off-screen (never `IsVisible = false`); names via `Describe`, unchanged; no imperative state added.
**Demos:** the Playground gallery ribbon and the Avalonia gallery's demo ribbon both show Word's Clipboard mixed (Paste
large split + Cut / Copy / Format Painter); Gallery c8c476d, Playground 25b5fe7.
- Playground `ribbon-mixed-check` 13/13: Paste spans the rows (85 of 85 px) with Cut / Copy / Format Painter stacked
  beside it at Medium; a uniform group keeps `size-medium` and no sized commands; names at every step; Right, Down,
  Down, Left walk Paste > Cut > Copy > Format Painter > Paste; Small step (400 px): Paste Medium with its label, the
  rest icons, named; Popup (330 px) lossless, flyout holds all four; a 900 → 200 → 900 px sweep resolves the same at
  every width. **Red** 5/11 with TASK-122's b-ribbon change set aside. verify 0 failing; keyboard-check 249/249;
  ribbon-a11y 22/22; category-ribbon 19/19; contextual-ribbon 13/13; a11y-name 43 OK; Components `tsc` clean.
- `RibbonMixedSizeTests` (Avalonia headless, 5): layout, uniform group unchanged, arrow order, names and a lossless
  collapse across a 1000 → 260 px sweep, no variant hidden from measurement. **Red** 2/5 (layout, arrows) with the
  `Ribbon.cs` change set aside. `Birko.Xaml.Avalonia.Tests` 447/447 (incl. `RibbonSizeTests`: no literal size);
  `Birko.Xaml.Core.Tests` 121/121; the gallery builds.

## Out of scope

- Deciding *which* configuration to render ([[TASK-121]]).
- Panel height ([[TASK-123]]).
- New tokens — they land in [[TASK-120]]. If this task needs one that does not exist, add it there
  and regenerate; never hand-author into generated CSS or AXAML.

## Human test plan

- [x] Look at a mixed group in the playground **and** the Avalonia gallery side by side at the same
      window width. The two skins are meant to be one design; a proportion that is subtly wrong on one
      of them is exactly what no assertion catches and what a glance catches immediately.
- [ ] Tab and arrow through a mixed group with the mouse untouched, on the XAML skin. The
      accessibility defects in STORY-049 were all in controls whose behaviour was already correct and
      tested, and two of them faked "Tab is broken" reports before anyone suspected styling.
  - _Run 2026-11-25 by the user with the agent:_ the Playground ribbon and the Avalonia gallery's demo ribbon, Clipboard
    narrowed to the Small step in both. Same arrangement ("Paste ▾" Medium on top, Cut / Copy icons under it, Format
    Painter starting the second column). One difference found and fixed: the gallery centred the Small icons in the
    column under the wider "Paste ▾"; b-ribbon puts them at the column start, as Office does. Framework 062fd053
    (`HorizontalAlignment.Left` in `MixedColumns`), test `At_the_Small_step_the_icons_sit_at_the_column_start_under_Paste`
    red 32 vs 12 px without it; `Birko.Xaml.Avalonia.Tests` 448/448.

**Merged 2026-11-25** on the user's word ("merge") into Birko.Web main d6bc75c with TASK-120 / 121 / 122 together, tagged v0.5.6 (one branch, one tag); Framework halves on main (latest 062fd053). On main: Components `tsc` clean, `ribbon-mixed-check` 13/13, `ribbon-scaling-smoke` 79/79, keyboard-check 249/249, ribbon-a11y 22/22, category-ribbon 19/19, contextual-ribbon 13/13, verify 0 failing. Stays at verify until the keyboard step above (Tab and arrows on the XAML skin, mouse untouched) is run.
