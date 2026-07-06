---
id: STORY-038
parent: EPIC-016
status: done
created: 2026-07-06
---

# Frontend Birko.Web backports (shipped)

## User story

As a **Birko.Web consumer building an offline-first mobile PWA**, I want the service-worker
scaffold, read-through cache, sync indicator, mobile shell and device utilities in the framework,
so that the offline write-queue (`ActionQueue`) that Birko already ships is actually usable
end-to-end without hand-rolling the shell and cache around it.

## Behaviour

- A parametrized service worker + build helper precaches the app shell and never caches `/api/*`.
- A `MirrorStore` / `readThrough` helper bridges GET reads to `IndexedDbStore` (network-first, fall
  back to mirror when offline, evict on 404/410, trust mirror while writes are queued).
- A `<b-sync-status>` chip binds to an `ActionQueue` and renders offline / syncing / synced.
- A `BMobileAppShell` provides fixed top-bar + safe-area bottom-nav driven by a declarative
  `Surface[]` nav-model.
- Wake-lock and iOS-safe audio-cue utilities encapsulate the device gotchas everyone gets wrong.

## Completed ledger (migrated from Reps EPIC-002 / STORY-009)

| Landed capability | Landing site | Origin task |
|---|---|---|
| PWA service worker + precache/versioning | `Birko.Web.Core/pwa/service-worker.template.js` | `Consumers/WorkoutTracker/tasks/EPIC-002-birko-backports/STORY-009-frontend-web-backports/TASK-035-pwa-service-worker-scaffold.md` |
| `MirrorStore` / `readThrough` | `Birko.Web.Core/mirror-store.ts` | `…/STORY-009…/TASK-036-offline-read-through-mirror-store.md` |
| `<b-sync-status>` chip | `Birko.Web.Components` | `…/STORY-009…/TASK-037-sync-status-chip-component.md` |
| `BMobileAppShell` (top-bar + bottom-nav, `Surface[]`) | `Birko.Web.Shell` | `…/STORY-009…/TASK-038-mobile-app-shell-bottom-nav.md` |
| Screen Wake Lock manager | `Birko.Web.Core` | `…/STORY-009…/TASK-039-wake-lock-manager-util.md` |
| iOS-safe audio cue + priming | `Birko.Web.Core/audio-cue.ts` | `…/STORY-009…/TASK-040-ios-safe-audio-cue-util.md` |
| `Formatter.duration()` + Intl passthrough | `Birko.Web.Core` | `…/STORY-009…/TASK-041-formatter-duration-and-intl-options.md` |

## Follow-ups spawned by the backport review

- The generic capabilities here have **no Xaml analogue yet** (Birko.Xaml.* has no mobile shell,
  no offline mirror, no device layer, no formatter) → **STORY-040** ports the ones that fit.
- `BMobileAppShell` should be exercised in the reference surfaces, not just shipped → **STORY-041**.
