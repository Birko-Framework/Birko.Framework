---
id: TASK-526
parent: STORY-059
feature: null
status: done
priority: P3
assignee: ai
created: 2026-10-07
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Web.Core: layered runtime configuration (defaults < server endpoint < local overrides)

## Context

DraCode's `DraCode.KoboldLair.Client/src/services/config.ts` (157 lines) builds its runtime settings from three layers:
built-in defaults, then `/api/config` from the server, then a local-storage override, with change notification. Only the
setting names are DraCode's.

Consumer side: DraCode `EPIC-018 / STORY-039` (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams`).

## Acceptance criteria

- [x] A typed runtime-config helper merges defaults, an optional server endpoint and an optional local override, and notifies on change
- [x] Secrets are never written to local storage by the helper (tokens belong to the auth store)
- [x] Tests for merge order and change notification

## Out of scope

- App-specific settings

## Human test plan

N/A — covered by unit tests.

## Implementation plan

Drafted at pick, 2026-10-14.

1. `Birko.Web.Core/src/config/runtime-config.ts`: `new RuntimeConfig<T>({ defaults, storageKey?, fetchServer? |
   serverUrl?, secretKeys?, storage? })`. `get()` returns a merged snapshot (defaults < server < local); `load()`
   fetches the server layer (non-fatal, returns false, retryable); `set(partial)` writes the local layer; `reset(keys?)`
   clears it; `onChange(fn)`. Only keys the defaults declare, with the default's type, are taken from the server or
   storage. `secretKeys` are held in memory only, never written, and a stored copy is scrubbed on load.
2. Playground smoke with a fake Storage and a fake server fetch.

## Progress log

- 2026-10-14 — Done, released in **Birko.Web v0.1.8** (Web `5fcee13`, Playground `bd1de6d`). `RuntimeConfig` in
  `state/` (reachable from `birko-web-core/state`). Beyond the criteria: values from the server or storage are taken only
  for declared keys with the default's type, and a secret an older build stored is scrubbed on load (DraCode stores
  `authToken` in localStorage today). `runtime-config-smoke` 27/27, proven red on merge order, the type check and secret
  persistence (6 fail).
