---
id: TASK-549
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

# b-carousel — slotted, server-rendered slides with arrows, dots, swipe and an autoplay that yields

## Context

Requested by FlowerFurStudio (its TASK-010, EPIC-002 STORY-005, MVP-labelled). On the **storefront's critical path**
(user priority 2026-10-10): the home hero, category tiles and product carousels (FFS P00, P05–P10) use it, and FFS
TASK-071 (CMS block renderers) and TASK-109 (home category tiles) wait on it.

The slides are **server-rendered** (SSR storefront), so the component must enhance light-DOM content, never own it:
the first slide is the page's content before any JS runs. No carousel exists in `Birko.Web.Components` today.

## Acceptance criteria

- [ ] Slides are slotted light-DOM children; **without JS the first slide is visible** and the rest are reachable
      (no content hidden behind a script)
- [ ] One slide → no arrows, no dots, no autoplay; many → arrows, dots, swipe (pointer events, touch and mouse)
- [ ] `per-view` (slides visible at once) with responsive breakpoints, so the same component serves a hero
      (1 per view) and a product row (2–5 per view)
- [ ] Optional `autoplay` (interval attribute) that **pauses on hover, on focus within, and never starts under
      `prefers-reduced-motion`**; a visible pause/play control when autoplay is on (WCAG 2.2.2)
- [ ] WAI-ARIA carousel pattern: `aria-roledescription="carousel"` / `"slide"`, slide labels "n of m", live region
      polite only when not autoplaying; arrows and dots are buttons with translatable names (`bwc.*`)
- [ ] Off-screen slides are `inert` so Tab does not land in an invisible slide
- [ ] `loop` optional; emits `slide-change` with the index
- [ ] Tests: 1 vs many slides, per-view paging, autoplay pause on hover/focus/reduced-motion, keyboard, inert
      off-screen slides
- [ ] `Birko.Web.Components/CLAUDE.md`, README / docs updated; Playground page

## Out of scope

- Storefront-specific styling beyond theme tokens
- Lazy-loading images — the consumer's `<img loading="lazy">` does that

## Human test plan

- [ ] Tab into a hero carousel → arrows focusable, autoplay pauses
- [ ] Swipe on a phone; with reduced motion on in the OS, autoplay never starts
- [ ] Disable JS → the first slide is visible and readable

## Implementation plan

_Populated by `/tasks plan TASK-549` — leave empty until then._
