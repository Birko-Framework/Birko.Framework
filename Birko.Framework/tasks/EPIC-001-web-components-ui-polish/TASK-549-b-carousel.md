---
id: TASK-549
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

# b-carousel — slotted, server-rendered slides with arrows, dots, swipe and an autoplay that yields

## Context

Requested by FlowerFurStudio (its TASK-010, EPIC-002 STORY-005, MVP-labelled). On the **storefront's critical path**
(user priority 2026-10-10): the home hero, category tiles and product carousels (FFS P00, P05–P10) use it, and FFS
TASK-071 (CMS block renderers) and TASK-109 (home category tiles) wait on it.

The slides are **server-rendered** (SSR storefront), so the component must enhance light-DOM content, never own it:
the first slide is the page's content before any JS runs. No carousel exists in `Birko.Web.Components` today.

## Acceptance criteria

- [x] Slides are slotted light-DOM children; **without JS the first slide is visible** and the rest are reachable
      (no content hidden behind a script)
- [x] One slide → no arrows, no dots, no autoplay; many → arrows, dots, swipe (pointer events, touch and mouse)
- [x] `per-view` (slides visible at once) with responsive breakpoints, so the same component serves a hero
      (1 per view) and a product row (2–5 per view)
- [x] Optional `autoplay` (interval attribute) that **pauses on hover, on focus within, and never starts under
      `prefers-reduced-motion`**; a visible pause/play control when autoplay is on (WCAG 2.2.2)
- [x] WAI-ARIA carousel pattern: `aria-roledescription="carousel"` / `"slide"`, slide labels "n of m", live region
      polite only when not autoplaying; arrows and dots are buttons with translatable names (`bwc.*`)
- [x] Off-screen slides are `inert` so Tab does not land in an invisible slide
- [x] `loop` optional; emits `slide-change` with the index
- [x] Tests: 1 vs many slides, per-view paging, autoplay pause on hover/focus/reduced-motion, keyboard, inert
      off-screen slides
- [x] `Birko.Web.Components/CLAUDE.md`, README / docs updated; Playground page

## Out of scope

- Storefront-specific styling beyond theme tokens
- Lazy-loading images — the consumer's `<img loading="lazy">` does that

## Human test plan

- [ ] Tab into a hero carousel → arrows focusable, autoplay pauses
- [ ] Swipe on a phone; with reduced motion on in the OS, autoplay never starts
- [ ] Disable JS → the first slide is visible and readable
- [ ] Mouse: drag the hero sideways — it snaps to a slide, and a slide that is a link does not open

## Implementation plan

Done directly.

## Resolution (2026-10-10)

`Birko.Web.Components/src/layout/b-carousel.ts` (`BCarousel`) and `src/layout/slide-track.ts` (`SlideTrack` — the
scroll mechanics `b-gallery` / TASK-550 reuses), exported from `layout/index.ts`; `bwc.carousel.*` keys; API.md,
ACCESSIBILITY.md (Carousel row), CLAUDE.md (inventory + Recent Updates).

Decisions beyond the criteria:

- **Native `scroll-snap` for swipe**, plus a mouse drag (desktop has no native drag-to-scroll) that suppresses the
  click ending it, so a slide that is a link does not navigate after a drag.
- **Per-view is `--b-carousel-per-view`**; the `per-view` attribute sets it for 1–6, and consumer CSS wins — so the
  "responsive breakpoints" criterion is met by the consumer's own media queries, re-paged on resize.
- **No-JS:** slides are light DOM and all visible before upgrade; hiding all but the first is an opt-in consumer
  rule (`b-carousel:not(:defined) > :not(:first-child) { display: none }`), documented.
- **Focus stops autoplay for good** (APG), hover only pauses; the stop / start control is the first thing in the
  carousel. Automatic steps are not announced; user moves are ("Slide 2 of 3").
- **Owner's walkthrough (2026-10-10) found a stale stop:** having Tabbed through the carousel before autoplay was
  switched on, the autoplay started *stopped* (▶). Focus now stops only a rotation that is running, and adding
  `autoplay` clears an earlier stop. Regression checks fail on the old code (2), suite 53/53.
- A consumer's own `role` / `aria-label` on a slide is kept; attributes the carousel adds are removed when a slide
  leaves.

**Proof.** Playground `src/carousel-smoke.ts`: 51/51 on two consecutive runs; the first run caught a real bug (a
removed slide kept the carousel's attributes, because a detached node is unreachable through the host — fixed with a
tracked set). Mutation: removing the `inert` sync and the reduced-motion guard fails 7 checks. Every other suite
green, no EMPTY component; `device-fix-check` 68/68; `a11y-name-check` OK; `tsc` clean. Gallery card added.
