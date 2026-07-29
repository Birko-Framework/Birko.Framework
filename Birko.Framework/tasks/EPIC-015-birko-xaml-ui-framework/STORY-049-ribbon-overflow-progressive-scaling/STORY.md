---
id: STORY-049
parent: EPIC-015
status: in-progress
created: 2026-07-29
---

# Office-style ribbon overflow — progressive group scaling + group-to-popup collapse

## User story

As a user of a Birko ribbon app on a narrow window or a small screen, I want every command in the
active tab to stay reachable, so that shrinking the window degrades how commands are *drawn* rather
than silently removing them from the UI.

## The defect this starts from

Reported 2026-07-29: on a small screen the ribbon shows fewer items and there is no way to get to the
rest. Confirmed in both implementations, and they fail differently — which matters, because the web
one looks like it works.

**`Birko.Xaml.Avalonia/Controls/Ribbon.cs` — no overflow handling at all.**

| Site | What it is | Consequence |
|---|---|---|
| `Ribbon.cs:47` + `:79-83` | tab strip is a horizontal `StackPanel` in a `*` Grid column | tabs past the right edge are clipped, no scroll, unreachable |
| `Ribbon.cs:92` | groups row is a horizontal `StackPanel`, `Margin(8)` | groups past the right edge are clipped, unreachable |

No `ScrollViewer`, no size variants, no popup fallback. The control is built imperatively in
`Rebuild()`, so nothing measures available width today.

**`Birko.Web.Components/src/nav/b-ribbon.ts` — the tab strip is handled, the panel is not.**

- The tab strip has real scroll arrows (`_setupTabScroll`, `b-ribbon.ts:698-717`) — this part is fine.
- `.ribbon-panel-inner` (`b-ribbon.ts:150-153`) is `overflow-x: auto` **with `scrollbar-width: none`
  and no arrow buttons**. Overflowing groups are technically scrollable but have **zero visible
  affordance**: no scrollbar, no chevrons, nothing indicating more content. A mouse without a
  horizontal wheel cannot reach them. Same unreachable-command defect, just hidden behind a scroll
  container that happens to exist.
- Secondary bug in the part that *does* work: `_setupTabScroll` binds `scroll` but never `resize` /
  `ResizeObserver`, and `updateArrows` otherwise runs once per render. Shrink the window without
  triggering a re-render and the right arrow stays hidden while the tabs overflow.

## The Office model (what we're adopting)

Office's answer is that **the ribbon never scrolls its body — it resizes it.** Four mechanisms, in
order of application:

1. **Progressive group scaling.** Each group declares an ordered list of size variants; the ribbon
   picks the largest set that fits the current width. The Windows Ribbon Framework names them
   explicitly (`SizeDefinition`):

   | Variant | Rendering |
   |---|---|
   | `Large` | 32px icon, label underneath, one item per column |
   | `Medium` | 16px icon + label to its right, 3 items stacked per column |
   | `Small` | 16px icon only, no label (tooltip carries the name), 3 per column |
   | `Popup` | the **whole group** becomes one button: group icon + group label + ▾ |

2. **Group-collapse-to-popup.** That last variant is the mechanism we're missing entirely. When even
   icon-only doesn't fit, the group folds into a single chunk button whose click opens the group's
   *full-size* layout in a flyout. Nothing is lost, and the group keeps its identity and position.

3. **Authored degradation order, not uniform shrink.** RibbonX exposes this as `scalingPriority` /
   `autoScale`: the author declares which group degrades first. Typically the rightmost / least-used
   group collapses first while the hero group (Clipboard, Font) keeps large icons longest. **This is
   the part a naive implementation gets wrong** — shrinking everything at the same rate turns the
   ribbon into an unreadable row of anonymous icons instead of keeping the primary group legible.

4. **Below the minimum, tabs-only.** The body hides entirely — which both our ribbons already have
   (`IsCollapsed` in Avalonia, the `expanded` attribute + the <48rem hamburger dialog on web).

## Decision — scaling, not scrolling (and why)

**Do not solve the panel by adding a horizontal scrollbar or scroll arrows to the groups row**, even
though that is the cheaper fix and even though it is what the panel accidentally half-does today.
The ribbon's value proposition is spatial memory — "Cut is top-left of Clipboard." A scroll offset
destroys that, and offscreen commands stop being discoverable, which is the exact failure the ribbon
was invented to fix over Office 2003's toolbars. The `»` overflow chevron *is* an Office pattern, but
it belongs to **toolbars** (Office 2003, Fluent `CommandBar` / `OverflowSet`), not the ribbon body.

**Tabs are a different problem and get the opposite answer.** Classic Office never scrolls tabs
(there are ~8 short ones, and contextual tabs *replace* rather than add), but Office Web and Fluent
do use `‹ ›` chevrons for the tab strip. So the web tab-strip scroll arrows stay, and Avalonia gets
the same treatment for tabs — while the groups row gets scaling in both.

## Behaviour

- Narrowing the window degrades the active tab's groups `Large → Medium → Small → Popup` and never
  clips or hides a command.
- Groups degrade in **author-declared priority order** — a low-priority group reaches `Small` before
  a high-priority one leaves `Large`.
- A group collapsed to `Popup` renders as one labelled button; clicking it opens a flyout containing
  that group's items at `Large`, and invoking an item from the flyout runs the same handler and
  dismisses the flyout.
- Widening the window promotes groups back up, and the layout is **stable** — the same width always
  yields the same variant set (no oscillation at a boundary, no dependence on which direction the
  resize came from).
- The Avalonia tab strip scrolls horizontally with visible affordance when tabs overflow; the web tab
  strip's existing arrows also update on **resize**, not only on scroll and re-render.
- The groups row has **no** horizontal scrollbar in either implementation once scaling lands.
- Keyboard and screen-reader users reach items inside a collapsed group's flyout — a `Popup` group is
  in the tab order and its flyout is arrow-navigable, matching the existing panel keyboard handling
  (`b-ribbon.ts:562-580`).

## Model change — must land in both, or parity breaks

Neither model can express any of this today: `RibbonGroup` / `RibbonItem`
(`Birko.Xaml.Core/Ribbon/RibbonModels.cs:4-21`) and the TS interfaces (`b-ribbon.ts:22-38`) carry no
size and no priority. Both need the same two additions — a size-variant floor/preference and a
scaling priority — and per the family's XAML↔web parity rule they must be designed **once** and
added to both in the same story, not retrofitted on one side. `RibbonGroup` also needs an `Icon` for
the `Popup` variant's chunk button, which neither model has.

New `--b-ribbon-*` tokens are likely (variant icon sizes, chunk-button width, popup padding) and go
through `Birko.DesignTokens/tokens.json` → generated CSS + AXAML, never hand-authored on one side.
`AxamlParityTests` / `CssParityTests` already gate that.

## Implementation notes worth knowing before picking this up

- **Avalonia needs a measuring panel.** `Rebuild()` is imperative tree construction with no width
  awareness. The degrade pass belongs in a custom `Panel` with `MeasureOverride` /`ArrangeOverride`
  that tries variant sets against the available width — not in `Rebuild()`, which has no constraint
  to reason about.
- **Web needs a `ResizeObserver`** on the panel plus a measure pass; the existing render is a
  template-string re-render, so the variant decision must survive `update()` the way `_hoverTabId`
  already has to (`b-ribbon.ts:359-361` documents that exact hazard).
- Avalonia has no native ribbon, so the flyout is ours to build. Note for later: **WPF ships
  `System.Windows.Controls.Ribbon` with all of this built in** — EPIC-015's WPF addendum already
  flags the ribbon as a place WPF is easier, so a future WPF skin should lean on the native control
  rather than port this.
- The two implementations should not share an algorithm by copy-paste. If the degrade decision can be
  expressed as a pure function over (group priorities, measured widths, available width) it belongs in
  `Birko.Xaml.Core` for the .NET side, with the TS side mirroring it — and that shared decision is
  what makes the behaviour testable without a renderer in both suites.

## Tasks

| Task | What | Order |
|---|---|---|
| TASK-097 | Make current overflow reachable — Avalonia tab-strip + groups `ScrollViewer`, web panel affordance, web resize observer | **first** (P1 — closes the defect) |
| TASK-098 | Model + tokens: size variant + scaling priority + group icon in `RibbonModels.cs` **and** `b-ribbon.ts` | parallel with 097; gates 099 + 100 |
| TASK-099 | The degrade pass: `Large→Medium→Small` measure-and-scale, priority-ordered, groups-row scrollbar removed | after 097 + 098 |
| TASK-100 | Group-collapse-to-popup: chunk button + flyout, keyboard/ARIA reachable | after 099 |

TASK-097 is worth landing on its own even though TASK-099 supersedes its groups-row scroller — it
closes an unreachable-command defect now, and 098–100 are a design change with a much longer tail.
TASK-098 touches only models and tokens, so it can land in parallel with 097 without conflict.

## Out of scope

- **KeyTips / Alt-key accelerators.** Office can afford to hide commands partly because Alt+H,V,V
  reaches everything regardless of layout. Worth having, but it's a separate keyboard-access story for
  the whole shell, not ribbon overflow.
- **Simplified Ribbon** (Office 2016+ single-row mode with a `···` overflow menu). A different
  ribbon *mode*, not an overflow mechanism.
- **The <48rem mobile hamburger dialog** on web — already works (`b-ribbon.ts:272-279`) and is
  unaffected. No Avalonia equivalent is in scope here.
- **Dialog launcher arrows** (the ↘ in an Office group corner opening a full dialog) — unrelated
  Office feature, no Birko analogue.
- **WPF.** Deferred with the rest of the WPF skin, and it gets this from the native control anyway.
