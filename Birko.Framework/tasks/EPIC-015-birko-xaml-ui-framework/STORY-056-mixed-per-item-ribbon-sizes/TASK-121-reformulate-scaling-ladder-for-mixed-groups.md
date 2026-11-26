---
id: TASK-121
parent: STORY-056
feature: FEATURE-015
status: done
priority: P1
assignee: ai
created: 2026-07-31
depends-on: [TASK-119, TASK-120]
blocks: [TASK-123]
pr: null
github-issue: null
jira-key: null
---

# Reformulate the degrade ladder for mixed-size groups

## Context

**The hard task in the story.** Everything else is expressible with what already exists; this is not.

Today both implementations measure each group at four discrete variants:

- C# — `RibbonGroupMetrics.Widths` is `IReadOnlyDictionary<RibbonGroupSize, double>`
  (`Birko.Xaml.Core/Ribbon/RibbonScaling.cs:14`), with `Ladder` = `Large, Medium, Small, Popup` and a
  `widthOf` that walks down the ladder when an entry is missing.
- TS — `RibbonGroupMetrics.widths` is `Partial<Record<RibbonGroupSize, number>>`
  (`ribbon-scaling.ts:28`), with the same fallback walk in `widthOf()`.

Widths are supplied by the renderer, because the renderer is the only thing that can measure — that
separation is what keeps the policy testable, and it must survive.

With per-item sizes, **the key no longer names a configuration.** A six-item group has many mixed
layouts, and `Widths[Medium]` does not say which.

## The invariant that must not be lost

> The chosen configuration depends **only on the arguments**, never on the currently-applied layout.

`ribbon-scaling.ts:15` states this and STORY-049 proved it empirically: while the groups row still had
a scroller, the chevrons' hysteresis fed back into the width being scaled against, and the same window
width resolved differently depending on drag direction. Removing the feedback fixed it with no other
change. A mixed-size ladder that measures the *current* layout to decide the *next* one reintroduces
exactly that bug, and it will present as flicker at one specific window width — which is miserable to
diagnose after the fact.

The related lesson from the same story: **the scaling decision must not read the applied layout even
indirectly.** Deriving the band from a height-derived tick count is what made a 90px chart mis-scale;
same shape of mistake, different component.

## Approach

Start from TASK-119's sketched replacement for `Widths`. Whatever the key becomes, keep:

- **Renderer measures, policy decides.** The policy takes numbers in and returns a configuration; it
  never touches the DOM or the visual tree.
- **An ordered, finite candidate list.** The pass picks from it. If TASK-119 chose per-item degrade
  order, the ordering must still be total and precomputed — not searched at layout time.
- **`minSize` as a preference, not a guarantee.** STORY-049 settled this: breach it least-important-
  first rather than let the row overflow, because unreachable commands are worse than a group being
  less legible than its author wanted. Mixed sizes do not change that trade.
- **`Popup` as the terminal rung.** A group collapsing to one chunk button with a full-size flyout is
  lossless — it keeps its identity and position.

Port the C# and TS in lockstep and diff the two by their **test table**, not by reading them.

## Acceptance criteria

- [x] The metrics type expresses mixed configurations in both C# and TS, with the same shape
- [x] `widthOf`'s fallback behaviour has a defined analogue — a missing measurement must not silently
      resolve to a wrong configuration
- [x] The pass is a pure function of its arguments; asserted by a test that runs it twice from
      different starting layouts and gets identical output
- [x] A boundary sweep — widths stepped up and then back down across a degrade threshold — resolves
      **identically in both directions** (the direct regression test for the STORY-049 oscillation)
- [x] `minSize` is still breached least-important-first rather than overflowing the row
- [x] `Popup` remains the terminal rung and stays lossless
- [x] The mirrored C# unit tests and the playground `ribbon-scaling-smoke` assert the **same numeric
      table**, updated together in this change
- [x] Uniform-size groups resolve to exactly the configurations they resolve to today — asserted
      against the existing table, unchanged rows

## Evidence

Per FEATURE-015 D14 the metrics keep their shape: `Widths[step]` (C#) / `widths[step]` (TS) is the width of that step's
**resolved layout** (`RibbonItemSizing.SizesAt` / `ribbonItemSizes`), so a group still has at most four widths and
`Resolve` / `resolveRibbonSizes` are unchanged; documented on both metrics types. **The fallback analogue:** a missing
width still reads as the nearest roomier step's, which stays an over-estimate because layouts are **monotone** (an
item never gets roomier at a tighter step); the anchor rule is monotone by construction and a declared layout that
breaks it is refused in both skins, so a missing measurement cannot make the row fit by accident.
- C# `RibbonItemSizingTests` (Framework main), mirrored by Playground 7ed05b3 `ribbon-scaling-smoke` 79/79: the monotony
  refusal; a mixed group (Clipboard, widths 200 / 140 / 90 / 40) beside a uniform one degrading through 9 widths
  least-important-first; the pass is pure whatever ran before; a sweep 60 → 260 → 60 px resolves identically both ways;
  a Small floor holds at 100 px while the other group gives everything and gives way at 99 px instead of
  overflowing; Popup terminal. The existing uniform table (`RibbonScalingTests` / its smoke rows) is unchanged and
  green. `Birko.Xaml.Core.Tests` 121/121. Web `task/TASK-120` be4eeff (v0.5.6 with TASK-120 and 122).
- The human drag test waits for TASK-122, which first draws mixed columns.

## Out of scope

- Rendering the result ([[TASK-122]]). This task decides *what* layout; that one draws it.
- Panel height ([[TASK-123]]).
- The tab strip. Tabs are the deliberate exception and do scroll; the body resizes and never scrolls.

## Human test plan

- [x] Drag the playground window slowly across a degrade boundary in both directions and confirm no
      flicker or oscillation. Automated tests cover the pure function, but the failure this guards
      appeared as visible thrash at one specific width, and a human dragging is still the fastest way
      to notice a boundary the table did not sample.

**Merged 2026-11-25** on the user's word ("merge") into Birko.Web main d6bc75c with TASK-120 / 121 / 122 together, tagged v0.5.6 (one branch, one tag); Framework halves on main (latest 062fd053). On main: Components `tsc` clean, `ribbon-mixed-check` 13/13, `ribbon-scaling-smoke` 79/79, keyboard-check 249/249, ribbon-a11y 22/22, category-ribbon 19/19, contextual-ribbon 13/13, verify 0 failing. Stays at verify until the drag test above is run (Playground or gallery, slowly across a step, both ways).

**Human test passed 2026-11-26** (the user: "121 and 122 i confirm to work"): the Playground ribbon and the Avalonia gallery dragged slowly across the steps both ways, no flicker or oscillation. Done.
