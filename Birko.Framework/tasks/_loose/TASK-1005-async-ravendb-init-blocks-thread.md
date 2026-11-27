---
id: TASK-1005
parent: null
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] `AsyncRavenDBStore` uses an async `EnsureReadyAsync`: maintenance `SendAsync`, `Task.Delay`, and the caller's
  token honoured. The sync store keeps the sync version.
- [ ] Both share one implementation of the retry decision.
- [ ] Test: the caller's token cancels the async init during the wait, and the wait holds no thread.
- [ ] The TASK-996 race test passes for the async store, and the live RavenDB suites pass.

## Out of scope

- Which exceptions are retried ([[TASK-1004]]). Do that one first, or both together.

## Human test plan

N/A — fully covered by automated tests.
