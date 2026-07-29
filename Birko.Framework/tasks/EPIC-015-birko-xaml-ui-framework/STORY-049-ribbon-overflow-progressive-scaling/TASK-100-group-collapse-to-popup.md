---
id: TASK-100
parent: STORY-049
feature: null
status: in-progress  # todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
priority: P2
assignee: ai
created: 2026-07-29
depends-on: [TASK-099]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Group-collapse-to-popup — the chunk button and its flyout

## Context

The last and most distinctive variant of the Office model, and the one that makes the whole scheme
lossless: when even `Small` doesn't fit, the **entire group** folds into a single button showing the
group's icon + label + a ▾ affordance. Clicking it opens the group's *full-size* (`Large`) layout in a
flyout. Nothing is lost, and the group keeps its identity and its position in the row — which is what
distinguishes this from a generic overflow menu that dumps every leftover command into one flat list.

Builds directly on TASK-099's degrade pass (which stops at `Small`) and TASK-098's model (which
supplies `RibbonGroup.Icon` for the chunk button and the variant floor that lets a group opt out of
collapsing). With this landed, the groups row needs **no scroll fallback at any width** — if a
groups-row scroller survived TASK-099 as a last resort, remove it here.

Avalonia has no native ribbon, so the flyout is ours to build (`Flyout` / `Popup` over the existing
token-styled surfaces). Note for the future: **WPF ships `System.Windows.Controls.Ribbon` with all of
this built in** — EPIC-015's WPF addendum already flags the ribbon as a place WPF is easier, so a
future WPF skin should lean on the native control rather than port this code.

**Accessibility is the reason this is a task and not a detail.** Office can afford to hide commands
partly because KeyTips (Alt+H,V,V) reach everything regardless of layout. We are not building KeyTips
here (out of scope, separate shell-wide story), which makes it *more* important that a collapsed group
is reachable by ordinary keyboard navigation and announced correctly — otherwise narrowing the window
silently removes commands from keyboard and screen-reader users specifically. The web panel already
has arrow-key navigation and Escape-to-tab-row handling to extend (`b-ribbon.ts:562-580`).

## Pulled forward into TASK-099 (2026-07-29)

**Most of this task shipped early, deliberately.** TASK-099 could not remove the interim groups-row
scroller without a `Popup` variant to degrade into, and could not keep the scroller either: a
`ScrollViewer` measures its content with infinite width, so the scaling pass saw no constraint, and
feeding it the viewport instead let the scroll chevrons' hysteresis feed back into the width being scaled
against — the same window width then resolved differently depending on drag direction. Removing the
scroller fixed determinism with no other change, and that required `Popup`. The reviewer chose this over
the alternatives (accept a clipping window, or accept non-deterministic scaling).

**Landed with TASK-099:** the chunk button and its flyout in both skins, invoke-dismisses-flyout, the
group `Icon` on the chunk face, and a **compact** chunk that drops the group name at the extreme (an
addition to the original scope — a labelled chunk cannot be narrower than its group's name, which left a
clipping window). Web accessibility is done: `aria-expanded`/`aria-haspopup`, `Escape` closes and returns
focus, keyboard reachable.

**What remains is the Avalonia accessibility half** — it leans on `Flyout` defaults that have not been
verified, and has no automation-peer assertions. That is what the unchecked criteria below cover.

## Acceptance criteria

- [x] A group that cannot fit at `Small` renders as **one** chunk button: group icon + group label +
      a ▾ affordance, in the group's original position in the row.
- [x] Clicking (or `Enter`/`Space` on) the chunk button opens a flyout containing that group's items at
      `Large`, laid out as the group would render when uncollapsed.
- [x] Invoking an item from the flyout runs **the same handler** as the uncollapsed item
      (`RibbonItem.Run` / the `item-click` event with the same `tabId`/`groupId`/`itemId`) and dismisses
      the flyout.
- [ ] The flyout dismisses on `Escape` and on click/focus outside, returning focus to the chunk button.
      **Web: done. Avalonia: unverified** — it relies on `Flyout`'s own light-dismiss behaviour.
- [x] A group with a variant **floor** above `Popup` (TASK-098) never collapses, even if that forces
      another group to collapse instead.
- [ ] **Web: done. Avalonia: unverified.** **Keyboard reachable:** the chunk button is in the tab order in place of the group's items, and
      the flyout's items are arrow-navigable — consistent with the existing panel keyboard handling
      (`b-ribbon.ts:562-580`).
- [ ] **Web: done (`aria-expanded` + `aria-haspopup`). Avalonia: not started.** **Screen-reader correct:** the chunk button exposes the group's name and its expanded/collapsed
      state, and the flyout is associated with it (web: `aria-expanded` + `aria-haspopup` and the
      flyout labelled by the chunk button; Avalonia: the equivalent automation peer properties).
- [x] With this landed, the groups row has **no scroll fallback at any width** in either skin.
- [x] Widening promotes a collapsed group back to `Small`/`Medium`/`Large`, and an open flyout closes
      when its group is promoted (it must not linger over a now-expanded group).
- [ ] Avalonia coverage in `RibbonTests.cs`: measure at a width that forces collapse, assert one chunk
      button per collapsed group, open it and assert the items are present and their handler runs.
      **Partly done** — collapse, the chunk button, its icon and the compact form are covered; *opening the
      flyout and running an item from it* is not, which is the same gap as the accessibility criteria above.
- [x] Web side verified by building + headless-running `Birko.Web.Playground`.
      `ribbon-scaling-smoke` covers the chunk button, `aria-expanded`, the flyout's contents, dismissal on
      invoke, and the compact form (36 checks).

## Out of scope

- **KeyTips / Alt-key accelerators** — the Office mechanism that makes every command reachable by
  keystroke regardless of layout. Worth having; it's a shell-wide keyboard story, not ribbon overflow.
- **Simplified Ribbon** (Office 2016+ single-row mode with a `···` overflow menu) — a different ribbon
  *mode*.
- **Dialog launcher arrows** (the ↘ in an Office group corner opening a full dialog) — unrelated Office
  feature with no Birko analogue.
- Animating the collapse/promote transition.
- WPF (deferred, and it gets this from the native `Ribbon` control anyway).

## Human test plan

- [ ] `Birko.Xaml.Gallery` — narrow the window until the lowest-priority group collapses. Expected: one
      labelled button with a ▾, in the same position the group occupied.
- [ ] Click it. Expected: a flyout with that group's commands at full size. Click a command. Expected:
      it runs (observable in the gallery) and the flyout closes.
- [ ] Reopen the flyout and press `Escape`. Expected: it closes and focus returns to the chunk button.
- [ ] With the flyout **open**, drag the window wider until the group would be promoted. Expected: the
      flyout closes rather than hovering over the now-expanded group.
- [ ] Keyboard-only pass: from the tab strip, `Tab`/arrow into the groups row and reach the collapsed
      group's commands **without touching the mouse**. This is the criterion that matters most — if it
      fails, narrowing the window removes commands from keyboard users specifically.
- [ ] Screen reader (Narrator on Windows) — focus the chunk button. Expected: the group's name and its
      collapsed/expandable state are announced, not just "button".
- [ ] `Birko.Web.Playground` — repeat the click, `Escape`, keyboard-only and screen-reader passes in the
      browser, and compare the flyout's rendering against the Avalonia gallery for parity.

_Reviewer confirmed 2026-07-29 (web only): a collapsed group's flyout opens and is **fully visible** — no
clipping. Nothing else in this plan is signed off; every step below is written around the Avalonia gallery
or bundles an Escape / keyboard / screen-reader pass that has not been run, so none of them can be ticked
off the back of that one observation. It took a
fix first — the flyout was `position: absolute` inside two `overflow: hidden` ancestors and was cut off on
the right and the bottom; it is now a popover in the top layer. Note the reviewer found this in the
**Playground**, and I initially "fixed" it in Avalonia, where native popups cannot be clipped at all —
that change is reverted._

## Implementation plan

_Populated by `/tasks plan TASK-100` — leave empty until then._
