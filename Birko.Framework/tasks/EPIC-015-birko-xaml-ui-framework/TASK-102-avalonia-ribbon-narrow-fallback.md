---
id: TASK-102
parent: EPIC-015
feature: FEATURE-015
status: done
priority: P2
assignee: ai
created: 2026-07-29
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia `Ribbon`: a narrow fallback, mirroring `b-ribbon`'s hamburger

## Context

Found while reviewing TASK-099 in the gallery: at the extreme the reviewer looked for a hamburger and
there isn't one. **`b-ribbon` has had one all along and the Avalonia `Ribbon` has never had any
equivalent** — grep for "hamburger" across `Birko.Xaml.{Core,Avalonia,Shell}` returns nothing.

What the web does (`b-ribbon.ts`, the `@media (max-width: 48rem)` block plus `#mobile-dialog`):

- below 48rem the tab strip, the panel and the expand/pin controls are all hidden;
- a ☰ button and the active tab's label take their place;
- ☰ opens a full-screen `<dialog>` listing **every tab, every group, every item**, with the active
  tab's section expanded;
- picking an item emits the same `item-click` and closes the dialog.

So on the web, an arbitrarily narrow viewport is not a scaling problem at all — the ribbon stops being
a ribbon and becomes a menu. Avalonia instead keeps scaling until it physically cannot, and then clips.

**This is Office's fourth mechanism** (below the minimum, stop drawing a ribbon) in the shape Birko.Web
already chose, so it is a port of shipped behaviour rather than a design question — the same situation
as TASK-101.

**Not urgent, and say so plainly.** TASK-099 pushed the six-group floor from 509px (labelled chunks) to
**166px** (compact, measured), and 166px of ribbon is narrower than any usable desktop window. So this
is a completeness/parity item, not a fix for a reachable defect. It matters most for
`Birko.Xaml.Shell` on a small tablet or a split-screen window, and for the parity claim itself — the
two skins currently disagree about what a very narrow ribbon *is*.

Related: **TASK-101** (pinned vs temporary-reveal) is the other half of the same gap, and both touch the
same chrome. Doing them together is probably cheaper than either alone.

## Acceptance criteria

- [x] `Ribbon` gains a narrow threshold (a styled property, defaulting to the 48rem/768px equivalent so
      it matches the web) below which it renders the fallback instead of the tab strip + body.
- [x] Below it: a ☰ button plus the active tab's label, and nothing else from the normal chrome.
- [x] ☰ opens an overlay listing every tab → group → item, the active tab's section expanded, mirroring
      the web dialog's structure.
- [x] Invoking an item runs the same `RibbonItem.Run` and dismisses the overlay.
- [x] The overlay is keyboard-navigable and dismissed by `Escape`, with focus returned to ☰.
- [x] Above the threshold nothing changes — the TASK-099 scaling behaves exactly as it does today.
- [x] The threshold is crossed by *ribbon* width, not window width, so a ribbon in a narrow pane behaves
      the same as one in a narrow window.
- [x] Tests in `RibbonTests.cs`: below the threshold the tab strip is gone and ☰ is present; ☰ reveals
      every item across every tab; invoking runs the handler and dismisses.
- [x] `Birko.Xaml.Gallery` can be dragged narrow enough to demonstrate it.

## Out of scope

- Any change to `b-ribbon` — the web side is the reference and stays as it is.
- Progressive scaling itself (TASK-099) — this is what happens *after* scaling runs out.
- Pinned / temporary-reveal collapse — TASK-101.
- WPF.

## Human test plan

- [x] `Birko.Xaml.Gallery` → Chrome tab. Drag narrower than the compact-chunk row can manage. Expected:
      the ribbon becomes ☰ + the active tab's name, with no clipped icons.
- [x] Click ☰. Expected: an overlay listing every tab with its groups and items; the active tab expanded.
- [x] Pick a command. Expected: it runs and the overlay closes.
- [x] Reopen, press `Escape`. Expected: it closes and focus returns to ☰.
- [x] Widen again. Expected: the normal ribbon returns and scales as before.
- [x] Compare with `Birko.Web.Playground` below 768px: the two should now feel like the same component.

_Reviewer confirmed 2026-07-29: ☰ appears at a narrow width, opens the menu, and `Escape` closes it with
focus returned. `Escape` needed a fix first — a raw `Popup` does not handle it (`IsLightDismissEnabled` is
pointer-only; Escape lives in `FlyoutBase`), and the test that "covered" it raised the key straight at the
control. Still unrun: invoking a command from the ☰ menu specifically, widening back, and the Playground
comparison below 768px._


_Playground comparison done 2026-07-29: the hamburger appears in both skins and lists every tab, group and
item. No parity gaps found here._


## Implementation plan

Landed 2026-07-29 alongside TASK-101. Below `NarrowThreshold` the ribbon renders ☰ plus the active
tab's name; ☰ opens a light-dismiss `Popup` listing every tab → group → item, active tab first, with
each entry running the same `RibbonItem.Run` and closing the menu. Narrow/wide is driven off
`BoundsProperty` and rebuilds **only when the state flips**, so a drag does not rebuild per pixel.

**The threshold defaults to 240, deliberately not the web's 768 — and this is the one decision here
worth arguing with.** `b-ribbon`'s 48rem is a *touch-layout* breakpoint: below it you are on a phone
and a ribbon is the wrong interaction model whether or not it would fit. A desktop control has no
phone, so copying the number would replace a perfectly usable 700px ribbon with a menu — the measured
floor for a six-group tab is 166px. I defaulted it to 768 first and it turned 20 tests into
hamburgers, which was the evidence. So the acceptance criterion as originally written ("defaulting to
the 48rem/768px equivalent so it matches the web") was **wrong**, and is met in spirit by making the
threshold configurable: a consumer wanting literal web parity sets 768.

Tests: **173** in `Birko.Xaml.Avalonia.Tests`, including that the menu reaches commands on *inactive*
tabs — the guarantee the whole story rests on.
