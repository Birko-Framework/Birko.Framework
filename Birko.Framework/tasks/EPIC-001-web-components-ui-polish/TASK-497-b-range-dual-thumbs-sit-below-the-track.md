---
id: TASK-497
parent: EPIC-001
feature: FEATURE-001
status: review
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

### Second offset, found while fixing (2026-09-26)

With the thumbs centred, the screenshot showed `sm` and `lg` thumbs stopping **short of both ends**. The
same shared sheet's size variants (`:host([size="sm"|"lg"]) input { padding: … }`) pad every input by
7px / 14px across and outrank `b-range`'s `padding: 0`. The thumb travels the padded content box while the
track and fill are drawn at full width, so it stopped short at the ends and drifted off the fill between
them. Single mode at `sm`/`lg` too. It is the same defect shape (a shared input rule that does not fit a
range slider), so it is fixed here: `:host([size]) input[type="range"] { padding: 0 }`.

## Implementation plan

1. `.range-slider--dual input[type=range]`: `top: 50%; transform: translateY(-50%)` instead of `top: 0`.
2. Check the vertical orientation's dual rules for the same shape.
3. Playground smoke `range-geometry-smoke`: for single and range modes at `size` sm/md/lg (horizontal),
   and vertical range mode, each input's centre coincides with the track's centre within 1px. Prove it
   fails before the fix.

## Acceptance criteria

- [x] In range mode, both thumbs are centred on the track at every `size`, like single mode.
- [x] The inputs keep their shared min-height (touch target unchanged).
- [x] Vertical orientation is checked and, if affected, fixed the same way.
- [x] Smoke proven able to fail; `verify.mjs` and `device-fix-check.mjs` stay green.

## Out of scope

- Thumb styling.

## Human test plan

- [ ] Look at a range-mode `b-range` in the Playground (light and dark): the dots sit on the line. Drag each
      thumb to check it still grabs.

## Progress log

- 2026-09-26 — `.range-slider--dual input[type=range]`: `top: 50%; transform: translateY(-50%)` (was `top: 0`),
  plus `:host([size]) input[type=range] { padding: 0 }`. Vertical orientation measured unaffected: its own,
  more specific rules already centre with `left: 50%; translateX(-50%)`.
- 2026-09-26 — `Birko.Web.Playground` `range-geometry-smoke` 26/26; 19/26 on the pre-fix `b-range`
  (thumbs 1.8 / 6.1 / 8.8px low at sm / md / lg; travel 14px / 28px short at sm / lg). `verify.mjs` 0 failing,
  `device-fix-check` 68/68, `a11y-name-check` 23/23; a mouse drag still grabs a range thumb (to → 50).
  ⚠ The first "after" screenshot still showed the offset. It came from the throwaway `sr-creatable.js`
  bundle, which embeds its own copy of the components and had not been rebuilt, so it was a stale picture.
  The measured smoke was right. Screenshots now come from `app.js`. The owner's visual check is pending → `review`.
