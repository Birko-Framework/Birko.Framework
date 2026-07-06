---
id: STORY-041
parent: EPIC-016
status: done
created: 2026-07-06
---

# BMobileAppShell showcase / placement

## User story

As a **framework maintainer / consumer evaluating Birko**, I want `BMobileAppShell` exercised and
discoverable in the reference surfaces (Birko.Web.Playground and Birko.Xaml.Gallery), so that the
mobile shell is demonstrable — not just library code with no live example.

## Behaviour

- `BMobileAppShell` has a dedicated, well-placed demo in `Birko.Web.Playground` (top-bar +
  safe-area bottom-nav switching surfaces), rather than being absent or buried.
- Once the Xaml mobile shell exists (TASK-043), `Birko.Xaml.Gallery` showcases its equivalent.

## Notes / cross-refs

- The web playground is also tracked under `EPIC-013-reference-consumers` (TASK-038 Birko.Web
  playground) — this story adds the specific BMobileAppShell placement, coordinate there.
- The Xaml gallery is tracked under `EPIC-015-birko-xaml-ui-framework` (STORY-031 tier-0 gallery
  validation) — the Xaml showcase (TASK-050) depends on TASK-043 landing first.
- Driven by the user's note during the 2026-07-06 review: *"the bmobileappshell maybe needs better
  placement in the playground too and maybe in xaml.gallery."*
