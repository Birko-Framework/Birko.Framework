---
id: TASK-049
parent: STORY-041
feature: FEATURE-016
status: done
priority: P2
assignee: ai
created: 2026-07-06
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# BMobileAppShell — better placement / demo in Birko.Web.Playground

## Context

`BMobileAppShell` (fixed top-bar + safe-area bottom-nav, driven by a declarative `Surface[]`
nav-model) shipped into `Birko.Web.Shell` from Reps (STORY-038 / origin TASK-038). The user flagged
during the 2026-07-06 review that it "needs better placement in the playground" — i.e. it should
have a clear, exercised demo in `Consumers/Birko.Web.Playground`, not be missing or buried.

The playground already exists (`Consumers/Birko.Web.Playground`: `src/`, `index.html`, `build.js`,
esbuild toolchain; also tracked as `EPIC-013-reference-consumers/TASK-038`). This task adds/relocates
a BMobileAppShell demo surface so a visitor can see the mobile shell switch between surfaces.

## Acceptance criteria

- [x] The playground has a dedicated, discoverable BMobileAppShell demo (its own gallery
      section / route), with a small `Surface[]` nav-model driving ≥3 surfaces. — `pg-mobile-shell` (a playground-local `BMobileAppShell` subclass) in the **Navigation** section; surfaces Home/Log/Stats.
- [x] The demo shows the fixed top-bar + bottom-nav and safe-area handling (viewport-sized frame). — rendered inside a 320×560 phone frame (`.pg-stage pg-mobile-shell` CSS).
- [x] Playground bundle builds green (esbuild) and the demo is linked from the playground index. — `node build.js` clean (app.js 673kb); reachable via the section switcher.
- [x] No new component code in `Birko.Web.Components`/`Shell` — this is showcase wiring only. — the subclass lives in the playground consumer (`src/app.ts`); zero framework edits.

## Out of scope

- Any change to `BMobileAppShell` itself (it shipped in STORY-038).
- The Xaml gallery showcase — that's TASK-050.

## Human test plan

- [x] Run the playground locally, open the BMobileAppShell demo, and confirm the bottom-nav switches
      surfaces and the top-bar stays fixed on scroll. — headless `node verify.mjs` (puppeteer): the Navigation
      section renders `pg-mobile-shell` non-empty with **zero page errors**; the playground's `backport-smoke`
      also asserts "mobile shell renders one nav item per surface" + "active item reflects hash /log".
- [x] Check it at a mobile viewport (safe-area insets respected, no horizontal scroll). — rendered in the
      phone frame; `BMobileAppShell` CSS carries `env(safe-area-inset-bottom)` (0 on desktop).

## Implementation plan

_Populated by `/tasks plan TASK-049` — leave empty until then._
