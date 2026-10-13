---
id: TASK-591
parent: EPIC-001
feature: FEATURE-001
priority: P3
status: todo
assignee: ai
created: 2026-10-13
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# The Playground cannot import a component by its own path: its alias map predates TASK-564

## Context

Found while doing TASK-579. `Birko.Web.Playground/birko-src.mjs` `birkoAliases()` maps `birko-web-components` to
`src/index.ts` and each category to `src/<category>/index.ts`. That is the pre-TASK-564 form the Components README now
tells consumers to replace: it resolves the barrels but not `birko-web-components/inputs/b-rating`. A smoke suite that
imported the component by its own path failed to build (`Could not resolve
".../src/inputs/index.ts/b-rating"`), so `rating-smoke.ts` imports the `inputs` barrel instead. `entry-points-check.mjs`
proves the documented alias with its own map, so the framework's own gallery is the one consumer still on the old one.

## Acceptance criteria

- [ ] `birkoAliases()` maps `birko-web-components` and `birko-web-core` to their `src` directories, the README's
      documented form, and drops the per-subpath entries that alias makes redundant
- [ ] `tsconfig.json` `paths` follow (`birko-web-components/*` → `src/*`), and a real `tsc` passes
- [ ] `rating-smoke.ts` (and any smoke that imports a barrel only to register one component) imports the component by
      its own path
- [ ] `node build.js`, `verify.mjs`, `theme-roundtrip-check.mjs` and `entry-points-check.mjs` all pass, and the bundle
      size is unchanged (same inputs)

## Out of scope

- Other consumers' alias maps (Presenter has switched; Symbio and Gameshow are their own tasks).

## Human test plan

N/A — the build and the existing checks cover it.
