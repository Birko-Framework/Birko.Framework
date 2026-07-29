---
id: TASK-099
parent: STORY-049
feature: null
status: todo  # todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
priority: P2
assignee: ai
created: 2026-07-29
depends-on: [TASK-097, TASK-098]
blocks: [TASK-100]
pr: null
github-issue: null
jira-key: null
---

# The degrade pass — measure and scale groups Large → Medium → Small in priority order

## Context

This is the core of STORY-049: the ribbon body stops clipping (and stops scrolling) and starts
**resizing**, the way Office does. TASK-097 made overflow reachable via scrolling as an interim
measure; TASK-098 added the size-variant + scaling-priority model. This task consumes the model and
**removes the interim groups-row scroller** — the decision on record (STORY-049 § Decision) is that a
scrolling ribbon body destroys the spatial memory the ribbon exists to provide.

Three variants here; `Popup` is TASK-100.

| Variant | Rendering |
|---|---|
| `Large` | 32px icon, label underneath, one item per column — today's only rendering |
| `Medium` | 16px icon + label to its right, 3 items stacked per column |
| `Small` | 16px icon only, no label (tooltip carries the name), 3 per column |

**Degrade in author-declared priority order, not uniformly.** A low-priority group must reach `Small`
before a high-priority one leaves `Large`. Uniform shrinking is the failure mode that turns the ribbon
into a row of anonymous icons instead of keeping the primary group legible — it is the single most
likely way to implement this and get a worse result than the bug.

**Where the logic goes.** The degrade decision should be a **pure function** over (group scaling
priorities + variant floors, each group's measured width per variant, available width) → chosen
variant per group. Put it in `Birko.Xaml.Core` (Avalonia-free, EPIC-015 constraint #1) and mirror it
on the TS side. That is what makes the behaviour testable without a renderer in both suites, and it
keeps the two implementations from drifting into two different algorithms via copy-paste.

**Avalonia needs a measuring panel.** `Ribbon.Rebuild()` (`Ribbon.cs:41-103`) is imperative tree
construction with no width constraint available — the degrade pass cannot live there. Introduce a
custom `Panel` with `MeasureOverride` / `ArrangeOverride` that measures each group at each candidate
variant and applies the chosen set. `RibbonTests.cs` already measures at an explicit size
(`Show<T>`, `RibbonTests.cs:42-50`), so this is directly testable headless.

**Web needs a `ResizeObserver` plus a measure pass, and the decision must survive re-render.**
`b-ribbon`'s render is a template-string rebuild; `b-ribbon.ts:355-361` documents this exact hazard
for `_hoverTabId` (an imperatively-set value was lost when `expand()` flipped an attribute and
triggered `update()`). The chosen variant set must be derived in `render()` from stored state the same
way `panelTabId` is, not stamped on afterwards.

**Stability is a real requirement, not a nicety.** The chosen variant set must be a deterministic
function of available width — the same width always yields the same set, independent of which
direction the resize came from. A hysteresis-free implementation that measures *after* applying a
variant can oscillate at a boundary (shrink → fits → grow → doesn't fit → shrink), which reads as
flickering. Measure candidates against the constraint; don't feed the applied layout back in.

## Acceptance criteria

- [ ] Narrowing the available width degrades the active tab's groups `Large → Medium → Small`, and
      **no command is clipped or hidden** at any width down to the point where `Popup` would be needed
      (TASK-100 handles below that; until it lands, the last resort may remain a scroller **on the
      groups row only** — remove it in TASK-100).
- [ ] Groups degrade in **priority order**: with two groups of differing `ScalingPriority`, the
      lower-priority one reaches `Small` before the higher-priority one leaves `Large`. Covered by a
      test, not just by inspection.
- [ ] A group's variant **floor** from TASK-098 is honoured — a floored group never degrades past it,
      even when that means another group degrades further.
- [ ] Widening promotes groups back up to `Large`.
- [ ] **Stable layout:** the same available width always produces the same variant set regardless of
      resize direction. Covered by a test that walks widths down then back up and compares.
- [ ] The **groups row no longer has a horizontal scrollbar or scroll chevrons** in either skin
      (the TASK-097 interim scroller is removed from the groups row). The **tab strip keeps its
      scrolling** — tabs are the documented exception.
- [ ] The degrade decision lives in a **pure, renderer-free function** in `Birko.Xaml.Core`, unit-tested
      directly, with the TS side mirroring the same algorithm.
- [ ] `Medium` and `Small` visually match their web counterparts (icon sizes, stacking, spacing) using
      the TASK-098 tokens — no hand-authored one-side-only values.
- [ ] `Small` items carry a tooltip with the item label, since the label is not rendered.
- [ ] Avalonia coverage in `RibbonTests.cs`: measure at several widths, assert the expected variant per
      group at each.
- [ ] Web side verified by building + headless-running `Birko.Web.Playground`.

## Out of scope

- **`Popup` / group-collapse-to-popup** — TASK-100. This task stops at `Small`.
- Per-item size overrides (TASK-098 ruled these out).
- Tab-strip behaviour — unchanged from TASK-097; tabs scroll, deliberately.
- Any change to which commands a tab contains, or to permission gating.
- KeyTips.

## Human test plan

- [ ] `Birko.Xaml.Gallery` — open a ribbon tab with 3+ groups of differing scaling priority and drag
      the window slowly from wide to narrow. Expected: groups step down `Large→Medium→Small`, the
      lowest-priority group visibly degrades first, and the hero group keeps big icons longest.
- [ ] Drag slowly **back out** to wide. Expected: variants promote back up, and the transitions happen
      at the same widths as on the way in — no flicker or oscillation while hovering a boundary width.
      (This is the stability criterion; it is much easier to see by hand than to assert.)
- [ ] At the narrowest width before popup: expected **no horizontal scrollbar and no chevrons** on the
      groups row — that is the whole point of the change.
- [ ] At `Small`: hover an icon-only item. Expected a tooltip with the command's label.
- [ ] `Birko.Web.Playground` — same slow narrow/widen pass in the browser; compare the `Medium` and
      `Small` renderings side by side against the Avalonia gallery for visual parity.
- [ ] Symbio UI — check a real, densely populated ribbon tab at laptop width (~1366px) reads better
      than it did before, not just differently.

## Implementation plan

_Populated by `/tasks plan TASK-099` — leave empty until then._
