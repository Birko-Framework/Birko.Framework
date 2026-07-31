---
id: TASK-122
parent: STORY-056
feature: null
status: todo
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

- [ ] A group renders one large item beside a column of three small ones, on **both** skins
- [ ] The Clipboard example (large Paste + stacked Cut/Copy/Format Painter) renders correctly in the
      playground and the Avalonia gallery
- [ ] Uniform-size groups are **byte-identical** to today — no visual diff, asserted by the screenshot
      baseline where one exists
- [ ] Mixed groups degrade through the ladder and collapse to `popup` losslessly
- [ ] No measurement is taken from a control with `IsVisible = false`
- [ ] Every item has a recognisable accessible name at every size, including icon-only `small`
- [ ] Keyboard arrow navigation reaches every item in a mixed group, in visual order
- [ ] Focus survives `Rebuild()`; imperative state is re-established after a re-render
- [ ] Web listeners bound in `onUpdated`
- [ ] Playground verifier green; `Birko.Xaml.Avalonia.Tests` green

## Out of scope

- Deciding *which* configuration to render ([[TASK-121]]).
- Panel height ([[TASK-123]]).
- New tokens — they land in [[TASK-120]]. If this task needs one that does not exist, add it there
  and regenerate; never hand-author into generated CSS or AXAML.

## Human test plan

- [ ] Look at a mixed group in the playground **and** the Avalonia gallery side by side at the same
      window width. The two skins are meant to be one design; a proportion that is subtly wrong on one
      of them is exactly what no assertion catches and what a glance catches immediately.
- [ ] Tab and arrow through a mixed group with the mouse untouched, on the XAML skin. The
      accessibility defects in STORY-049 were all in controls whose behaviour was already correct and
      tested, and two of them faked "Tab is broken" reports before anyone suspected styling.
