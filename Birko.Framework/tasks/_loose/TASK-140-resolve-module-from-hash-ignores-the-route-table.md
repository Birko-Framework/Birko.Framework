---
id: TASK-140
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P1
assignee: ai
created: 2026-08-06
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: []
pr: null
github-issue: null
jira-key: null
---

# `resolveModuleFromHash` derives the module positionally and never consults the route table

## Context

Found while reviewing `Surface.alsoMatches` (`Birko.Web.Shell` `3f85a34`, driven by WorkoutTracker
TASK-150) and asking whether the desktop shells share the defect it fixed. They do, in a different
shape and with worse consequences.

`Birko.Web.Shell` has two functions in `src/modules/route-builder.ts` that disagree about how a hash
maps to a module:

- **`buildModuleRoutes(modules)`** (`:5`) flattens every `ModuleManifest.options[].route` into a
  `RouteEntry[]` of `{moduleId, optionId, route}` — an explicit, declared route table.
- **`resolveModuleFromHash(store, hash)`** (`:16`) **does not take that table as a parameter and
  never consults it.** It splits the hash positionally and assumes `route` is always literally
  `/{moduleId}/{optionId}`:

  ```ts
  const parts = hash.replace(/^\//, '').split('/').filter(Boolean);
  const moduleId = parts[0] ?? '';
  const optionId = parts[1] ?? '';
  store.set('activeModuleId', moduleId || null);
  store.set('activeOptionId', optionId || null);
  ```

Two defects follow:

1. **A route that belongs to no module writes a garbage module id into shared state.** The
   `store.set` calls are unconditional — there is no check that `moduleId` names a real module. Every
   caller downstream then reads a value that looks resolved and is not.
2. **A declared `route` that does not follow the `/{moduleId}/{optionId}` convention resolves wrong**,
   silently, because the declared route is available and ignored. This is exactly the
   "two fields that could each answer *where does this surface go* will eventually disagree" hazard
   that `Surface.alsoMatches` was deliberately shaped to avoid — already present here.

**This is live in Symbio, not latent.** `Symbio.UI` has eleven top-level non-module routes —
`/dashboard`, `/settings`, `/profile`, `/tags`, `/notifications/preferences`, `/no-tenant`,
`/2fa/setup` and the auth pages (`src/shared/router.ts:110-123`). Navigating to any of them:

- sets `activeModuleId` to e.g. `'settings'`;
- `getActiveTabId()` → `getCategoryForModule('settings', modules)` → `mod?.category ?? moduleId`
  (`src/shared/module-store.ts:51-54`), so the ribbon is handed a tab id matching no tab and
  **highlights nothing**;
- `router.onNavigate` bails at `if (!mod) return;` (`src/shared/router.ts:137`) — **after**
  `resolveModuleFromHash` already mutated the store, so the guard protects the breadcrumb and not
  the state;
- **SSE live-refresh silently stops matching.** `sse-client.ts` gates refreshes on
  `activeModuleId === event.moduleId` (`:90`, `:126`, `:244`, `:282`), so while the user sits on
  `/settings` the store claims a module that does not exist and events for the module they actually
  came from are dropped.

That last one is why this is P1 rather than a cosmetic highlight bug: the consequence outlives the
page the user is on, and there is no error anywhere in the chain.

**Why `alsoMatches` does not port directly.** The mobile shell matches a hash against a list of
declared routes, so a surface can simply claim more of them. The module model is positional, so there
is nothing to add a claim *to* until the resolver reads the route table at all. Fixing the resolver to
use `buildModuleRoutes`'s output is the prerequisite; only then does an ownership field make sense.

Not shared by the other two shells for a reason worth recording: `BAppShell.getActiveTabId()` is
`abstract` and `BSidebarAppShell.getActiveLeftSidebarItem()` is a virtual returning `''`, so the
framework does no route matching on those paths — the consumer returns an id and the shell forwards
it to `b-ribbon` / `b-sidebar`, which compare ids, not routes. `activeSurface()` and
`resolveModuleFromHash` are the only two places the framework itself decides what a hash means.

## Acceptance criteria

- [ ] `resolveModuleFromHash` resolves against the **declared** routes (`buildModuleRoutes`'s
      `RouteEntry[]`, or the manifests it derives them from) rather than by segment position, so an
      option whose `route` does not read `/{moduleId}/{optionId}` resolves correctly
- [ ] A hash matching **no** declared route does **not** write a fabricated module id — the store is
      left with an explicit "no module" value, and the decision is observable to the caller (return
      shape says unresolved; do not rely on the caller to notice)
- [ ] Whether the previous `activeModuleId` is **cleared or preserved** on an unmatched route is
      decided explicitly and documented in the function's doc comment, with the reasoning — the two
      behaviours are both defensible and the SSE gating above makes the choice consequential
- [ ] A way for a module to claim a route outside its own `/{moduleId}/…` subtree exists **or** is
      recorded as a deliberate "no" with reasoning (the `alsoMatches` counterpart; do not add it
      speculatively if the resolver fix alone covers the real cases)
- [ ] `entityId` (segment 2 today) keeps working for the conventional shape, and its behaviour under
      a declared multi-segment route is defined rather than incidental
- [ ] Back-compat: every currently-correct resolution still resolves identically. Verified against
      Symbio's real manifest shape, not only a synthetic one
- [ ] Smoke coverage in `Birko.Web.Playground`'s `backport-smoke.ts` beside the existing
      `M266 resolveModuleFromHash …` checks (`:490-493`), covering: a conventional route, a
      non-conventional declared route, an unmatched top-level route, and the store state after each
- [ ] Every new check is **red-verified** by reverting the fix, and any check that passes either way
      is either fixed to be falsifiable or labelled as a back-compat assertion

## Out of scope

- **`Surface.alsoMatches` itself** — shipped in `3f85a34`; this task does not change the mobile path.
- **`BAppShell` / `BSidebarAppShell` active-state resolution** — consumer-implemented by design (see
  Context). If the fix here suggests those should also be framework-resolved, that is an API-shape
  decision and gets its own task rather than riding along.
- **Symbio's own eleven non-module routes.** The framework fix is what stops the garbage write; if
  Symbio then wants `/settings` to highlight something specific, that is consumer work in the Symbio
  repo, tracked there.
- **The SSE gating logic** in `sse-client.ts` — consumer code, and correct given a truthful
  `activeModuleId`. Fix the input, not the reader.

## Human test plan

- [ ] In Symbio, navigate from a module page (e.g. `#/inventory/stock`) to `#/settings`, then check
      `moduleStore.get('activeModuleId')` in the console — it must not read `'settings'`
- [ ] With an SSE-backed list open, navigate to `#/settings` and back, and confirm live updates for
      the original module resume (this is the consequence the resolver defect hides, and no unit test
      exercises the real event stream)
- [ ] Confirm the ribbon's highlighted tab on every one of Symbio's non-module top-level routes is
      whatever the criterion-3 decision says it should be — deliberately blank, or the last module —
      and not accidentally blank for a different reason

## Implementation plan

_Populated by `/tasks plan TASK-140` — leave empty until then._
