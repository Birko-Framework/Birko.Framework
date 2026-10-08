---
id: TASK-140
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: verify
picked-by: fix-next
priority: P1
assignee: ai
created: 2026-08-06
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [FIELD-003]
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

- [x] `resolveModuleFromHash` resolves against the **declared** routes (`buildModuleRoutes`'s
      `RouteEntry[]`, or the manifests it derives them from) rather than by segment position, so an
      option whose `route` does not read `/{moduleId}/{optionId}` resolves correctly
- [x] A hash matching **no** declared route does **not** write a fabricated module id — the store is
      left with an explicit "no module" value, and the decision is observable to the caller (return
      shape says unresolved; do not rely on the caller to notice)
- [x] Whether the previous `activeModuleId` is **cleared or preserved** on an unmatched route is
      decided explicitly and documented in the function's doc comment, with the reasoning — the two
      behaviours are both defensible and the SSE gating above makes the choice consequential
- [x] A way for a module to claim a route outside its own `/{moduleId}/…` subtree exists **or** is
      recorded as a deliberate "no" with reasoning (the `alsoMatches` counterpart; do not add it
      speculatively if the resolver fix alone covers the real cases)
- [x] `entityId` (segment 2 today) keeps working for the conventional shape, and its behaviour under
      a declared multi-segment route is defined rather than incidental
- [x] Back-compat: every currently-correct resolution still resolves identically. Verified against
      Symbio's real manifest shape, not only a synthetic one
- [x] Smoke coverage in `Birko.Web.Playground`'s `backport-smoke.ts` beside the existing
      `M266 resolveModuleFromHash …` checks (`:490-493`), covering: a conventional route, a
      non-conventional declared route, an unmatched top-level route, and the store state after each
- [x] Every new check is **red-verified** by reverting the fix, and any check that passes either way
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

Planned and executed inline by fix-next; see Progress log and Outcome.

## Progress log

- step 2 — picked at the user's request (2026-10-08) after TASK-537; the only open P1 framework defect outside EPIC-019, live in Symbio (fabricated `activeModuleId` silently breaks SSE live-refresh gating)
- step 3 — verified: holds. `route-builder.ts:16-29` splits by position and writes `parts[0]` unconditionally. Measured against Symbio: all 156 declared options in 24 modules are conventional `/{moduleId}/{optionId}` (so defect 2 is unreached there today), and its 11 non-module routes each write a fabricated id (defect 1, live). Symbio's `dashboard-grid.ts:53` already carries a workaround comment for `activeModuleId="dashboard"`
- step 4 — layer: local (Birko.Web.Shell, in the `Birko\Web` checkout)
- step 5 — fix in `Web/Birko.Web.Shell/src/modules/route-builder.ts` (+ `ModuleResolution` export in `modules/index.ts`); checks in `Consumers/Birko.Web.Playground/src/backport-smoke.ts`; `node verify.mjs`: backport-smoke 319/319, 0 failing checks across all suites
- step 6 — reverted route-builder.ts to HEAD: 5/8 checks failed; fix-dependent = "unmatched /settings is unresolved, store cleared" (old: activeModuleId=settings), "non-conventional declared route resolves to its module" (old: sales/leads/7), "longest declared route wins" (old: option stock, entity archive), "query string is not part of the option" (old: stock?tab=2), "nothing loaded yet resolves nothing and clears the stale id"; back-compat assertions (pass either way, labelled so) = M266 parses module/option/entity, M266 updates store, bare module id. Separately, old-vs-new over Symbio's real manifests (extracted from 24 `*Module.cs`): 468 hashes (156 routes × plain / +entity / +entity+segment) identical, 0 differ
- step 7 — no spec area: Birko.Web.* is on `docs/specs/.map.yml:81`'s uncovered list. Docs: Birko.Web.Shell README API entry rewritten
- step 8 — handed to /tasks close --unattended; outcome in status:, commit in git log

## Outcome

**What was fixed.** `resolveModuleFromHash` took the first hash segment as the module id and wrote it to the module
store whether or not such a module existed. In Symbio, every non-module page (`/settings`, `/dashboard`, `/profile`, …)
therefore set `activeModuleId` to a module that does not exist — the ribbon highlighted nothing, and SSE live-refresh,
which gates on `activeModuleId`, stopped matching the module the user came from. It now matches the hash against the
option routes the modules actually declare; an unmatched hash returns `resolved: false` and clears the active module
to `null`.

**Proof.** Reverting fails 5 of the 8 resolver checks — every new behaviour — and the failure lines show the old values
(`/settings` → `activeModuleId=settings`). 3 checks are labelled back-compat and pass either way. Against Symbio's real
manifests, 468 of 468 module resolutions are identical before and after.

**Judgement calls.**
- *Clear, not preserve, on an unmatched route* (criterion 3). On `/settings` the user is in no module; a preserved id
  would keep active-module-scoped permission checks and SSE refresh acting for a page that is not shown, and is the
  same "store claims a state that isn't true" defect in a milder form. Documented in the function's doc comment.
- *No ownership field* (criterion 4, the `alsoMatches` counterpart). Once the resolver reads declared routes, an option
  that declares `/settings` owns it — a second field would be the two-sources-for-one-answer hazard this task names.
- *Read the table from `store.modules`, not a new parameter.* The store already holds the manifests, so the signature
  and both consumers' call sites stay unchanged. Cost: nothing resolves before modules load — both consumers resolve
  again after loading (DraCode awaits `loadModules()` first; Symbio re-runs the router on `_rebuildRoutes`).
- *`entityId` is exactly the one segment after the matched route*, deeper segments left to the page; a query string is
  stripped (the old resolver folded `?tab=2` into the option id).
- *Bare module id* (`/inventory`) still resolves the module with no option — kept for back-compat.

**Why `verify`, not `done`.** The Human test plan needs a running Symbio with the rebuilt UI (console check, a live SSE
round-trip, the ribbon on each non-module route). No unit check exercises the real event stream, which is the
consequence that made this P1.

**Flagged, not fixed.** Nothing new. Symbio's `dashboard-grid.ts:53` comment describes `activeModuleId="dashboard"`,
which no longer happens; the workaround it explains stays correct (it resolves against the widget's module). That is
Symbio's file — its agent picks up the comment when it rebuilds against this change.
