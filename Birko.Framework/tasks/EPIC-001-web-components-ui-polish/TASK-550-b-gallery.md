---
id: TASK-550
parent: EPIC-001
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: verify
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

- [x] Images are slotted SSR `<img>` elements with alt text; **without JS the primary image is visible**
- [x] One image → no controls; many → main image + thumbnails (or dots on narrow screens), arrows, swipe
- [x] Thumbnails are buttons with the image's alt as accessible name; the current one is marked (`aria-current`)
- [x] Zoom: activating the main image opens a full-view dialog (reuse `b-modal` / native `<dialog>`), focus is
      trapped, **Escape closes it and focus returns to the trigger**; pinch / click-to-zoom inside it
- [x] `show(index)` method and an `index` attribute so a consumer can switch image on variant change; emits
      `image-change`
- [x] Zoom uses a larger source when the slotted `<img>` carries one (`data-full` or `srcset`), else the same image
- [x] Translatable names for arrows, zoom and close (`bwc.*`)
- [x] Tests: 1 vs many, thumbnail selection, `show(index)`, zoom open/Escape/focus return, keyboard
- [x] `Birko.Web.Components/CLAUDE.md`, README / docs updated; Playground page

## Out of scope

- Video slides
- Choosing images per variant — the consumer calls `show(index)`

## Human test plan

- [ ] Swipe through images on a phone; pinch inside the zoom
- [x] Keyboard only: open zoom with Enter, close with Escape, focus is back on the zoom button — owner, 2026-10-10
- [x] Narrow the window: thumbnails turn into dots; the current one is highlighted — owner, 2026-10-10

**Walkthrough 2026-10-10 (owner, Edge):** after the 0px-width and 30rem→24rem fixes — thumbnails, arrows, drag,
narrow dots, zoom (open, navigate, magnify, Escape, focus return) and the new opt-in `zoom-close-outside` passed.
No-JS (Playground `nojs.html`): images stacked, primary first — passed. **Open:** phone swipe and pinch inside the zoom.

## Implementation plan

Done directly, on TASK-549's `SlideTrack`.

## Resolution (2026-10-10)

`Birko.Web.Components/src/layout/b-gallery.ts` (`BGallery`), exported from `layout/index.ts`; `bwc.gallery.*` keys.
The arrows / dots / viewport CSS b-carousel had moved into a new shared **`slideNavSheet`** (`shared-styles.css`
`@sheet slideNav`), used by both — the "extract the common part" this task asked for covers the CSS as well as the
scroll mechanics. API.md, ACCESSIBILITY.md (Gallery row), CLAUDE.md (inventory, shared-sheet table, Recent Updates).

Decisions beyond the criteria:

- **`index` is a start, then a command, never a leash** — read once at first render; later attribute changes
  navigate; a re-render does not snap back.
- **Thumbnails become dots by a container query** on the gallery's own width (24rem — first 30rem, which gave the
  owner's ~410px Playground card dots; container-query rem is the root size), not the viewport.
- **Zoom is a native modal `<dialog>`** named by the image's alt; click magnifies ×2 around the point, pinch is
  native; next / previous work inside the zoom and move the gallery too.
- Images out of view are `inert`; every image stays named through its thumbnail.
- **`zoom-close-outside` (opt-in, owner's request 2026-10-10):** the zoom is full-screen, so there is no backdrop;
  with the attribute a click on the empty space around the image closes it. Off by default so a stray tap beside a
  magnified image while panning does not close it. Playground card has the toggle.
- **Owner's walkthrough (2026-10-10): the gallery card showed no images** — measured 0px wide. The `.gallery` is a
  size container (for the thumbs-to-dots container query), and a size container takes no width from its content, so
  in a shrink-to-fit parent (the Playground's centred flex card; a consumer could do the same) it collapsed. The host
  is now `width: 100%` (consumer CSS still wins); b-carousel got the same so it never shrinks to one slide's content.
  Regression checks in both suites; without the fix the gallery measures 0px (gallery 40/40, carousel 54/54).
- **Without `data-full` the zoom takes the largest `srcset` candidate** — `currentSrc` is the one the browser
  picked for the small main image, the wrong thing to enlarge (follow-up commit; suite 39/39).
- Found on the way: the dot's `1.5rem` target measured 21px under the Playground's 14px root (below WCAG 2.5.8's
  24px) — now `max(1.5rem, 24px)` in the shared sheet, which fixes b-carousel's dots too.

**Proof.** Playground `src/gallery-smoke.ts`: 38/38 (39/39 with the srcset check) on three consecutive full runs; mutation (re-reading `index` on
every render) fails "an unrelated re-render does not snap back". The suite waits for `input-adornment-smoke` (new
`__inputAdornmentSmokeDone` flag): its modal and image decoding in the shared page delayed that suite's
ResizeObserver past its fixed 60ms settle (2 geometry failures, measured), and focus is read inside the dialog's
`close` event because other suites move focus concurrently. All suites green, carousel 51/51 after the sheet move;
`device-fix-check` 68/68; `a11y-name-check` OK; `tsc` clean. Gallery card added.
