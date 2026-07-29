---
id: TASK-099
parent: STORY-049
feature: null
status: review  # todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
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
| `Large` | 32px icon, label underneath, one item per column |
| `Medium` | 16px icon + label to its right, 3 items stacked per column |
| `Small` | 16px icon only, no label (tooltip carries the name), 3 per column |

**Found during TASK-098 — the two skins do not currently render the same variant, so this task builds
more than it chooses between.** The Avalonia `Ribbon.BuildItem` stacks an 18px icon above a centred
wrapping label with `MinWidth = 52` — that is **`Large`**. The web `.ribbon-item` is
`inline-flex; align-items: center` with a `--b-icon-base` (16px) icon and the label to its right — that
is **`Medium`**. So the web side has no `Large` to degrade *from*, and Avalonia has no `Medium` to
degrade *to*; each skin needs one rendering built rather than just re-parameterised. Budget for it, and
settle the "which is the ribbon's default look" question explicitly — the answer decides whether the web
ribbon gets visually *taller* at wide widths (`Large` as default, Office-like) or Avalonia gets *denser*
(`Medium` as default, matching today's web). Both enums' docs already name the gap.

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

- [x] Narrowing the available width degrades the active tab's groups `Large → Medium → Small`, and
      **no command is clipped or hidden** at any width down to the point where `Popup` would be needed
      (TASK-100 handles below that; until it lands, the last resort may remain a scroller **on the
      groups row only** — remove it in TASK-100).
- [x] Groups degrade in **priority order**: with two groups of differing `ScalingPriority`, the
      lower-priority one reaches `Small` before the higher-priority one leaves `Large`. Covered by a
      test, not just by inspection.
- [x] A group's variant **floor** from TASK-098 is honoured — a floored group never degrades past it,
      even when that means another group degrades further.
- [x] Widening promotes groups back up to `Large`.
- [x] **Stable layout:** the same available width always produces the same variant set regardless of
      resize direction. Covered by a test that walks widths down then back up and compares.
- [x] The **groups row no longer has a horizontal scrollbar or scroll chevrons** in either skin
      (the TASK-097 interim scroller is removed from the groups row). The **tab strip keeps its
      scrolling** — tabs are the documented exception.
- [x] The degrade decision lives in a **pure, renderer-free function** in `Birko.Xaml.Core`, unit-tested
      directly, with the TS side mirroring the same algorithm.
- [x] `Medium` and `Small` visually match their web counterparts (icon sizes, stacking, spacing) using
      the TASK-098 tokens — no hand-authored one-side-only values.
- [x] `Small` items carry a tooltip with the item label, since the label is not rendered.
- [x] Avalonia coverage in `RibbonTests.cs`: measure at several widths, assert the expected variant per
      group at each.
- [x] Web side verified by building + headless-running `Birko.Web.Playground`.

## Out of scope

- **`Popup` / group-collapse-to-popup** — TASK-100. This task stops at `Small`.
- Per-item size overrides (TASK-098 ruled these out).
- Tab-strip behaviour — unchanged from TASK-097; tabs scroll, deliberately.
- Any change to which commands a tab contains, or to permission gating.
- KeyTips.

## Human test plan

- [x] `Birko.Xaml.Gallery` — open a ribbon tab with 3+ groups of differing scaling priority and drag
      the window slowly from wide to narrow. Expected: groups step down `Large→Medium→Small`, the
      lowest-priority group visibly degrades first, and the hero group keeps big icons longest.
- [x] Drag slowly **back out** to wide. Expected: variants promote back up, and the transitions happen
      at the same widths as on the way in — no flicker or oscillation while hovering a boundary width.
      (This is the stability criterion; it is much easier to see by hand than to assert.)
- [x] At the narrowest width before popup: expected **no horizontal scrollbar and no chevrons** on the
      groups row — that is the whole point of the change.
- [x] At `Small`: hover an icon-only item. Expected a tooltip with the command's label.
- [x] `Birko.Web.Playground` — same slow narrow/widen pass in the browser; compare the `Medium` and
      `Small` renderings side by side against the Avalonia gallery for visual parity.
- [x] Symbio UI — check a real, densely populated ribbon tab at laptop width (~1366px) reads better
      than it did before, not just differently.

## Implementation plan

Landed 2026-07-29 across seven repos. **TASK-100's `Popup` variant was pulled forward into this task** —
see its file for why, and for what of it remains.

**Shared policy** — `Birko.Xaml.Core/Ribbon/RibbonScaling.cs`: given each group's width per variant, its
priority and its floor, choose a variant per group so the row fits, degrading the least important first,
one step at a time. Renderer-free, so the *policy* cannot drift between the skins even though the
*rendering* is forked. `Birko.Web.Components/src/nav/ribbon-scaling.ts` mirrors it; the playground smoke
asserts the same numeric table as `RibbonScalingTests`.

**Avalonia** — `RibbonGroupsPanel` (new) builds every group at every variant, measures them, and applies
the chosen set. `BuildItem`/`BuildGroup` gained the `Medium`, `Small` and `Popup` renderings (only `Large`
existed). `PreferredGroupSize` (default `Medium`) plus public `ResolvedGroupSizes`.

**Web** — variant CSS off the TASK-098 tokens, an off-screen probe per variant for measurement, the
resolved set stored and re-emitted by `render()`, `preferred-group-size` attribute. The panel scroller is
gone.

**Five things this task got wrong first, all instructive:**

1. **The floor clamp was inverted** — the starting variant is the *roomier* of preferred and the floor,
   since `MinSize` caps how far down a group may go. Eight of twelve policy tests failed instantly.
2. **The interim scroller and the scaling pass are incompatible.** A `ScrollViewer` measures content with
   *infinite* width, so the panel saw no constraint. Feeding it the viewport worked but lagged, and the
   chevrons' hysteresis then fed back into the width being scaled against — the same window width resolved
   differently depending on drag direction. Removing the scroller made the determinism test pass with no
   other change, which is why TASK-100 had to come forward.
3. **Measuring an `IsVisible = false` control yields zero** (Avalonia short-circuits `MeasureCore`), so
   every unchosen variant looked free, the pass under-degraded, and the row clipped its last group. Now
   parked off-screen instead of hidden.
4. **A hard floor could cost reachability** — a hero group floored at `Small` kept its width and pushed the
   last group off the edge. Floors are now preferences, breached least-important-first.
5. **A double gap on the web** — `.ribbon-group + .ribbon-group` added a full group-gap of `padding-left`
   on top of the flex `gap`, which the single-group probe never saw. A ~120px under-estimate over six
   groups, so the row clipped however tight the variants got.

**Verification** — `Birko.Xaml.Core.Tests` 60, `Birko.Xaml.Avalonia.Tests` 164,
`Birko.DesignTokens.Tests` 42, playground `ribbon-scaling-smoke` 36 + `ribbon-overflow-smoke` 16 with 0
failures across all six smokes. Measured floors on the six-group demo: 509px labelled chunks, 166px
compact.

## Review log

- **2026-07-29 — reviewer confirmed the behaviour works as described** in `Birko.Xaml.Gallery`, after
  three rounds: groups hidden rather than narrowed (the invisible-measure bug), Clipboard refusing to
  collapse and pushing Export off (hard floors), then icons clipping at the extreme (compact chunk
  carrying spare padding). Two follow-ups were filed rather than folded in: **TASK-102** (no narrow
  fallback — the reviewer looked for `b-ribbon`'s hamburger and the XAML ribbon has never had one) and
  **TASK-101** (pinned / temporary-reveal). Still owed before `done`: the manual pass on the *final*
  build, and the side-by-side visual-parity check against the Playground.
