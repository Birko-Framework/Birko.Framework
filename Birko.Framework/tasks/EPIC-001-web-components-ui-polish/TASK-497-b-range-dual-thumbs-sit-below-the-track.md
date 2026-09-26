---
id: TASK-497
parent: EPIC-001
feature: FEATURE-001
status: in-progress
priority: P2
assignee: ai
created: 2026-09-26
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# `b-range` in range mode draws its thumbs below the track

## Context

Reported by the owner on 2026-09-26 ("the dots on slider UI are a bit off"). The screenshot shows
single-thumb mode centred on the track, while in `mode="range"` both thumbs sit visibly **below** it.

**Measured** (Chromium, 14px root): the `.range-slider` box is 21px tall, and each `input[type=range]` is
**33.25px**. `formControlSheet`'s `input, select, textarea { min-height: var(--b-control-min-height,
2.375rem) }` outranks nothing here (the local rule sets `height`, not `min-height`), so the min-height wins.

- **Single mode:** `.range-slider` is a flex row with `align-items: center`, so the oversized input is
  centred and the thumb lands on the track.
- **Range mode:** `.range-slider--dual input[type=range] { position: absolute; top: 0 }`. The 33px box
  hangs down from the top of the 21px slider, so each thumb centre sits ~6px below the track centre.

`size="sm"`/`"lg"` and `pointer: coarse` change the min-height, so the offset changes with them. That is
why the fix must centre the inputs, not assume a height. Shrinking them (`min-height: 0`) was rejected: it
would give up the touch-target floor the shared sheet exists to provide.

## Implementation plan

1. `.range-slider--dual input[type=range]`: `top: 50%; transform: translateY(-50%)` instead of `top: 0`.
2. Check the vertical orientation's dual rules for the same shape.
3. Playground smoke `range-geometry-smoke`: for single and range modes at `size` sm/md/lg (horizontal),
   and vertical range mode, each input's centre coincides with the track's centre within 1px. Prove it
   fails before the fix.

## Acceptance criteria

- [ ] In range mode, both thumbs are centred on the track at every `size`, like single mode.
- [ ] The inputs keep their shared min-height (touch target unchanged).
- [ ] Vertical orientation is checked and, if affected, fixed the same way.
- [ ] Smoke proven able to fail; `verify.mjs` and `device-fix-check.mjs` stay green.

## Out of scope

- Thumb styling.

## Human test plan

- [ ] Look at a range-mode `b-range` in the Playground (light and dark): the dots sit on the line. Drag each
      thumb to check it still grabs.
