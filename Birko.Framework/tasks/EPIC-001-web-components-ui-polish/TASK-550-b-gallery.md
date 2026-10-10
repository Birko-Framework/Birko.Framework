---
id: TASK-550
parent: EPIC-001
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
created: 2026-10-10
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# b-gallery — product image gallery with thumbnails, swipe and an accessible zoom

## Context

Requested by FlowerFurStudio (its TASK-011, EPIC-002 STORY-005, MVP-labelled). On the **storefront's critical path**
(user priority 2026-10-10): the product gallery (FFS P04, P05) uses it, and FFS TASK-071 too.

Images come from Symbio's `ProductDetail.Images` (primary first, each with an optional `VariantGuid`), rendered
server-side as `<img>` with alt text. The consumer switches the shown image when a variant is picked, so the
gallery needs a way to be told which image to show.

Shares swipe and slide mechanics with `b-carousel` (TASK-549) — extract the common part rather than copy it, if
TASK-549 lands first.

## Acceptance criteria

- [ ] Images are slotted SSR `<img>` elements with alt text; **without JS the primary image is visible**
- [ ] One image → no controls; many → main image + thumbnails (or dots on narrow screens), arrows, swipe
- [ ] Thumbnails are buttons with the image's alt as accessible name; the current one is marked (`aria-current`)
- [ ] Zoom: activating the main image opens a full-view dialog (reuse `b-modal` / native `<dialog>`), focus is
      trapped, **Escape closes it and focus returns to the trigger**; pinch / click-to-zoom inside it
- [ ] `show(index)` method and an `index` attribute so a consumer can switch image on variant change; emits
      `image-change`
- [ ] Zoom uses a larger source when the slotted `<img>` carries one (`data-full` or `srcset`), else the same image
- [ ] Translatable names for arrows, zoom and close (`bwc.*`)
- [ ] Tests: 1 vs many, thumbnail selection, `show(index)`, zoom open/Escape/focus return, keyboard
- [ ] `Birko.Web.Components/CLAUDE.md`, README / docs updated; Playground page

## Out of scope

- Video slides
- Choosing images per variant — the consumer calls `show(index)`

## Human test plan

- [ ] Swipe through images on a phone; pinch inside the zoom
- [ ] Keyboard only: open zoom with Enter, close with Escape, focus is back on the image

## Implementation plan

_Populated by `/tasks plan TASK-550` — leave empty until then._
