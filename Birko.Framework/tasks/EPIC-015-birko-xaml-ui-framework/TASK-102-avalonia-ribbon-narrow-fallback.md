---
id: TASK-102
parent: EPIC-015
feature: null
status: todo  # todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
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

- [ ] `Ribbon` gains a narrow threshold (a styled property, defaulting to the 48rem/768px equivalent so
      it matches the web) below which it renders the fallback instead of the tab strip + body.
- [ ] Below it: a ☰ button plus the active tab's label, and nothing else from the normal chrome.
- [ ] ☰ opens an overlay listing every tab → group → item, the active tab's section expanded, mirroring
      the web dialog's structure.
- [ ] Invoking an item runs the same `RibbonItem.Run` and dismisses the overlay.
- [ ] The overlay is keyboard-navigable and dismissed by `Escape`, with focus returned to ☰.
- [ ] Above the threshold nothing changes — the TASK-099 scaling behaves exactly as it does today.
- [ ] The threshold is crossed by *ribbon* width, not window width, so a ribbon in a narrow pane behaves
      the same as one in a narrow window.
- [ ] Tests in `RibbonTests.cs`: below the threshold the tab strip is gone and ☰ is present; ☰ reveals
      every item across every tab; invoking runs the handler and dismisses.
- [ ] `Birko.Xaml.Gallery` can be dragged narrow enough to demonstrate it.

## Out of scope

- Any change to `b-ribbon` — the web side is the reference and stays as it is.
- Progressive scaling itself (TASK-099) — this is what happens *after* scaling runs out.
- Pinned / temporary-reveal collapse — TASK-101.
- WPF.

## Human test plan

- [ ] `Birko.Xaml.Gallery` → Chrome tab. Drag narrower than the compact-chunk row can manage. Expected:
      the ribbon becomes ☰ + the active tab's name, with no clipped icons.
- [ ] Click ☰. Expected: an overlay listing every tab with its groups and items; the active tab expanded.
- [ ] Pick a command. Expected: it runs and the overlay closes.
- [ ] Reopen, press `Escape`. Expected: it closes and focus returns to ☰.
- [ ] Widen again. Expected: the normal ribbon returns and scales as before.
- [ ] Compare with `Birko.Web.Playground` below 768px: the two should now feel like the same component.

## Implementation plan

_Populated by `/tasks plan TASK-102` — leave empty until then._
