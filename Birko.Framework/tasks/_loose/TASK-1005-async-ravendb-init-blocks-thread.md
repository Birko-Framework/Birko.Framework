---
id: TASK-1005
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-27
depends-on: []
blocks: []
related: [TASK-996, TASK-998, TASK-1004]
findings: []
pr: null
github-issue: null
jira-key: null
---

# AsyncRavenDBStore's init blocks a thread for up to 30 s

## Context

Found at the 2026-11-27 `/specs regen` of `backend-store-implementations`; the user asked for it to be filed.

`AsyncRavenDBStore.InitCoreAsync` makes sure the database exists through `RavenDatabaseBootstrap.EnsureReady`
([[TASK-996]]). `EnsureReady` is synchronous: it calls the maintenance operations synchronously and waits between
attempts with `System.Threading.Thread.Sleep` (`Birko.Data.RavenDB/Stores/RavenDatabaseBootstrap.cs:66`), for up to
30 s.

So an async store's first call holds a thread-pool thread for the whole wait and ignores the caller's cancellation
token. `RavenQueryRetry` already has an async path (`RunAsync`) next to its sync one.

## Acceptance criteria

- [x] `AsyncRavenDBStore` uses an async `EnsureReadyAsync`: maintenance `SendAsync` and `Task.Delay` ~~, and the caller's
  token honoured~~ (withdrawn, see the Evidence). The sync store keeps the sync version.
- [x] Both share one implementation of the retry decision.
- [x] ~~Test: the caller's token cancels the async init during the wait,~~ and the wait holds no thread. The token half
  is withdrawn: see the Evidence.
- [x] The TASK-996 race test passes for the async store, and the live RavenDB suites pass.

## Evidence 2026-11-27

**Fix:** `RavenDatabaseBootstrap.EnsureReadyAsync` is the async walk: `SendAsync` for the listing, the create and the
statistics read, and `Task.Delay` between attempts. `AsyncRavenDBStore.InitCoreAsync` awaits it. The sync store keeps the
sync walk, and both share the retry decision (`IsConcurrentCreateRefusal`) and the spacing.

**The token criterion is withdrawn, as it conflicts with a recorded decision.** `AbstractAsyncStore.EnsureInitializedAsync`
runs `InitCoreAsync` under `CancellationToken.None` on purpose (SH-M425, TASK-324). One-time setup is shared, so no
single caller's token may stop it, and the caller's own cancellation is checked after init. Two related measurements:
- Wrapping each request in `WaitAsync(ct)` changed nothing, because the token reaching init is `None`. It was removed.
- The Raven client's request executor itself retries a disabled database for about 30 s inside `SendAsync`. It does
  that asynchronously.

**Test** (`Task1004DatabaseBootstrapLiveTests.An_async_store_waiting_for_a_disabled_database_does_not_hold_the_callers_thread`):
on a disabled database (`ToggleDatabasesStateOperation`), `CountAsync()` must return a pending task within 2 s.

| | `CountAsync()` returned its task after | Task state |
|---|---|---|
| Before | 31.3 s | already completed |
| After | 0 ms | pending |

After the fix the task fails with `DatabaseDisabledException` about 31 s later, as designed.

## Out of scope

- Which exceptions are retried ([[TASK-1004]]). Do that one first, or both together.

## Human test plan

N/A — fully covered by automated tests.
