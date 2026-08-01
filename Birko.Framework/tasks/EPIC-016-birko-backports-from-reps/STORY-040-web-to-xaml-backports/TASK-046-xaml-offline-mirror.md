---
id: TASK-046
parent: STORY-040
feature: FEATURE-016
status: done
priority: P3
assignee: ai
created: 2026-07-06
depends-on: []
blocks: [TASK-047]
pr: null
github-issue: null
jira-key: null
---

# Xaml offline read-through mirror (MirrorStore / readThrough concept)

## Context

`Birko.Web.Core` gained `MirrorStore` / `readThrough` from Reps (STORY-038 / origin TASK-036):
network-first reads with offline fallback, bridging GET reads to `IndexedDbStore`, evicting on
404/410 and trusting the mirror while writes are queued. The Web→Xaml review found the Xaml family
has **zero** offline concept — only the `ICrudDataSource<T>` port in `Birko.Xaml.Core`, which is
exactly the seam a Xaml mirror would sit behind.

The *concept* (network-first-with-offline-fallback around a CRUD port) is platform-neutral; the web
*implementation* (IndexedDB) is not — a Xaml mirror would back onto a local store (e.g. a
`Birko.Data` file/SQLite store). Note `Birko.Data.Sync` exists at framework level but is server/
store-to-store sync, not a client offline read/write mirror — do not conflate them.

**Precondition:** only worthwhile if Avalonia targets mobile/occasionally-connected desktop
(`net8.0`-only today). Lower priority accordingly.

## Acceptance criteria

- [x] A read-through mirror in the Xaml layer that decorates `ICrudDataSource<T>`: network-first,
      fall back to a local mirror when the source is unreachable, evict on not-found. — `Data/MirrorDataSource.cs`.
- [x] The neutral contract lives in `Birko.Xaml.Core` (Avalonia-free); the local-store backing is pluggable. — both remote + mirror are `ICrudDataSource<T>`; `idOf` selector supplied by the caller.
- [x] "Trust the mirror while writes are pending" semantics documented (ties into any future Xaml write-queue). — writes pass through remote→mirror; the outbox/offline-write path is noted as out of scope (TASK-047 precondition).
- [x] Tests covering: online read passes through; offline read serves mirror; not-found evicts. — `MirrorDataSourceTests` (4 tests) + `Status` transitions.
- [x] `Recent Updates` entry added.

## Out of scope

- A write-queue / outbox (`ActionQueue` equivalent) — if/when built, TASK-047 (sync indicator) binds to it.
- IndexedDB — web-only; the Xaml backing is a local `Birko.Data` store.

## Human test plan

- [x] Read online then offline → cached reads still resolve; server-side delete evicts the mirror on next read.
      — covered by `MirrorDataSourceTests` (offline fallback + not-found eviction) against a togglable fake remote.

## Implementation plan

_Populated by `/tasks plan TASK-046` — leave empty until then._
