---
id: TASK-100
parent: STORY-049
feature: null
status: review  # todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
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

**The Avalonia accessibility half is now done too** (2026-07-29). Three things, each verified rather than
assumed: a `RibbonChunkButton` peer exposing `IExpandCollapseProvider` with state that tracks the flyout;
parked variants taken out of the tab order (they were off-screen but still focusable, so Tab walked through
invisible controls); and `Escape` closing the flyout, which a `Flyout` does **not** do by itself — the test
failed when written, which is why the criterion had stayed unticked. Both fixes are mutation-checked.

## Acceptance criteria

- [x] A group that cannot fit at `Small` renders as **one** chunk button: group icon + group label +
      a ▾ affordance, in the group's original position in the row.
- [x] Clicking (or `Enter`/`Space` on) the chunk button opens a flyout containing that group's items at
      `Large`, laid out as the group would render when uncollapsed.
- [x] Invoking an item from the flyout runs **the same handler** as the uncollapsed item
      (`RibbonItem.Run` / the `item-click` event with the same `tabId`/`groupId`/`itemId`) and dismisses
      the flyout.
- [x] The flyout dismisses on `Escape` and on click/focus outside, returning focus to the chunk button.
      Avalonia needed explicit handling: a `Flyout` does **not** dismiss on `Escape` by itself — the test
      failed when first written, which is exactly why this criterion had stayed unticked.
- [x] A group with a variant **floor** above `Popup` (TASK-098) never collapses, even if that forces
      another group to collapse instead.
- [x] **Keyboard reachable:** the chunk button is in the tab order in place of the group's items, and
      the flyout's items are arrow-navigable — consistent with the existing panel keyboard handling
      (`b-ribbon.ts:562-580`).
- [x] **Screen-reader correct** (web: `aria-expanded`/`aria-haspopup`; Avalonia: `RibbonChunkButton`'s
      `IExpandCollapseProvider` peer + `AutomationProperties.Name`)**:** the chunk button exposes the group's name and its expanded/collapsed
      state, and the flyout is associated with it (web: `aria-expanded` + `aria-haspopup` and the
      flyout labelled by the chunk button; Avalonia: the equivalent automation peer properties).
- [x] With this landed, the groups row has **no scroll fallback at any width** in either skin.
- [x] Widening promotes a collapsed group back to `Small`/`Medium`/`Large`, and an open flyout closes
      when its group is promoted (it must not linger over a now-expanded group).
- [x] Avalonia coverage in `RibbonTests.cs`: measure at a width that forces collapse, assert one chunk
      button per collapsed group, open it and assert the items are present and their handler runs.
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
- [~] Keyboard-only pass: from the tab strip, `Tab`/arrow into the groups row and reach the collapsed
      group's commands **without touching the mouse**. This is the criterion that matters most — if it
      fails, narrowing the window removes commands from keyboard users specifically.
      **Partly confirmed 2026-07-30:** Tab reaches the tab strip, activates a tab, walks into the groups row
      and moves between commands, and reaches a collapsed group's chunk button (verified with Narrator on
      "Export"). Deliberately **not ticked**: opening that chunk with `Enter` and reaching the commands
      *inside its flyout* by keyboard alone has not been reported, and that is the half the criterion is
      actually about. The three defects fixed on the way (invisible focus, focus lost on tab activation, an
      unnameable command) are each covered by a mutation-checked test.
- [x] Screen reader (Narrator on Windows) — focus the chunk button. Expected: the group's name and its
      collapsed/expandable state are announced, not just "button". **Confirmed 2026-07-30** — "Export, group,
      collapsed", and the commands announce their own labels ("Cut button", "Paste button"). Took four
      rounds and found the story's worst defect: no ribbon button had an accessible name at all.
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

## Review log

- **2026-07-29/30 — the keyboard-only pass found three defects, and the first was hiding the other two.**
  The reviewer reported Tab appearing stuck on the page's theme buttons. It was not stuck: the **Avalonia
  skin has no focus visual on `Button` at all** (only `Inputs.axaml` styles `:focus`, and `--b-focus-ring`
  was never emitted to AXAML), so focus was moving invisibly across the whole ribbon. Reachable but
  invisible is a WCAG 2.4.7 failure in its own right; the ribbon's buttons now draw a focus ring with
  **constant border thickness** (only the brush changes, so focus cannot reflow the scaled row) and a
  hardcoded fallback colour, because the test proved the ring vanished entirely when no theme was merged.
  With focus visible, two real navigation defects appeared immediately:
  - **Activating a tab by keyboard threw focus out of the ribbon.** `Rebuild()` discards the whole tree, so
    the focused tab button was destroyed and focus fell back to the window root — the next Tab restarted
    from the top of the page and the groups that tab had just opened were unreachable. Focus is now
    restored to the newly selected tab.
  - **Tab landed on an invisible chevron.** The reserved-but-inactive scroll chevron was hidden with
    `Opacity = 0` + `IsHitTestVisible = false`, which hides a control from the *mouse* only. This is the
    **third instance of one mistake in this story** — off-screen, transparent and non-hit-testable all
    leave a control in the tab order, and only `IsEnabled = false` removes it (the parked size variants and
    the flyout alternatives had the same bug).

  All three are mutation-checked. Note that the *automated* keyboard test added earlier
  (`Tab_walks_through_the_ribbon_and_skips_the_parked_variants`) was green throughout: it asserted the
  traversal order, which was always correct, while the thing that made the ribbon unusable by keyboard was
  that you could not see where you were and that activating a tab lost your place. Same species as the rest
  of this story — the mechanism was tested, the outcome was not.

- **2026-07-30 — no ribbon button had an accessible name at all, which was the real defect here.** With the
  chunk button announcing correctly, the reviewer listened to the *commands* and heard a generic control type
  with no command name — no way to tell which command focus was on. Avalonia derives a button's name from its
  content only when that content is a **string**, and a ribbon item's content is a panel holding an icon and
  a label, so the peer fell back to `Content.ToString()`: assistive tech was handed
  **`"Avalonia.Controls.StackPanel"`** at `Medium`/`Large` and the bare glyph **`"●"`** at `Small`. An unnamed
  command is unusable, not merely under-described — so the two rounds of chunk-button announcement work above
  were polish on top of a broken foundation.

  Every focusable button in the ribbon is now named through one helper (items in all four variants, tab
  buttons, collapse chevron, pin, hamburger, and the tab scroll chevrons, whose `◂`/`▸` announced as nothing
  useful either), and each decorative glyph is marked `AccessibilityView.Raw` so an emoji is not read out
  beside the name it decorates.

  **The test for this was vacuous when first written, in a way worth remembering:** it asked for a *non-empty*
  name, which `Content.ToString()` satisfies, so it passed with the fix removed. It now requires the name to
  be one of the model's own strings, and carries a non-vacuity guard because an over-strict reachability
  filter made "no unnamed buttons" trivially true of an empty set. Mutation-checked in all four variants.

- **2026-07-30 — Narrator said "button" and the group's name, and nothing about the state.** The
  `IExpandCollapseProvider` peer was correct all along (the Win32 bridge does expose the pattern — checked
  rather than assumed); it simply was not *spoken*. Narrator voices expand/collapse state for the control
  types where it expects one — combo box, tree item, menu item — and a plain `Button` is not one of them
  however many patterns it advertises. Fixed by putting the state where a screen reader always looks:
  **`LocalizedControlType`** now returns "collapsed group" / "expanded group" (dynamic, because the fixed
  wording would keep saying "collapsed" with the flyout open), and **`HelpText`** carries the affordance
  ("Press Enter to show this group's commands"). The pattern stays — it is the correct UIA contract and
  other tools read it.

  The instructive part: the existing automation test asserted the peer's `ExpandCollapseState` and was green
  throughout, because the peer was right. Nothing tested what a screen reader is actually **given**. That is
  now the *fourth* defect in this story of exactly one shape — mechanism tested, outcome not.

  Still unverified by me: whether Narrator now speaks it. There is no headless substitute, so the human step
  stands.

  Scoping note: the focus ring is on the **ribbon's** buttons only. `Buttons.axaml` having no focus visual
  affects every button in every consumer, which is a broader visual change deserving its own task.

## Implementation plan

_Populated by `/tasks plan TASK-100` — leave empty until then._
