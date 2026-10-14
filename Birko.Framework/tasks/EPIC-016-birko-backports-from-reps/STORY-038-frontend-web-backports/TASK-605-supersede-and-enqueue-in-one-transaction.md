---
id: TASK-605
parent: STORY-038
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P2
assignee: unassigned
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-043]
pr: null
github-issue: null
jira-key: null
---

# `ActionQueue` cannot supersede a queued write atomically, so a collapse can always miss one in flight

> **Moved from WorkoutTracker TASK-173 on 2026-10-14.** It was filed there as Birko.Web work, where the framework's pick never saw it. Every TASK / STORY / FEATURE id in the body below is **WorkoutTracker's**. There, TASK-173 stays open as the Reps-side check, blocked on this task.


## Context

Spawned out of **TASK-172**, which fixed the half that was losing data and hit the wall this task names.

`setWeekOffset` is the app's only *superseding* write (D13): re-picking a week must replace the queued intent,
never stack on it. Reps expresses that as two separate `ActionQueue` calls — read `getPending()`, then
`remove(id)` — around an `api.post` that does the enqueue. TASK-172 established that **which side of the post
the removal runs on changes which failure you get**, and that no ordering available to a *caller* avoids all of
them:

| ordering | offline | online |
|---|---|---|
| remove-then-post | **loses the change** — a window holds neither write (`min = 0`, 3/3 in both engines); a reload or tab eviction in it is silent data loss | fine |
| enqueue-then-remove | fine — outbox is monotonic | **resurrects the stale write** — `SyncManager.sync()` snapshots `getPending()` and *then* stamps `status:'syncing'` via `ActionQueue.update()`, which is a `put`; a `remove()` landing in between is undone by that re-insert, and the abandoned week replays |

TASK-172 ships the ordering picked per-path off `navigator.onLine` as a **hint**, which is sound because both
wrong answers degrade to a pre-existing window rather than a new one. But it is a mitigation, not a fix.

## What it still cannot reach

**A row already `syncing` is invisible to `getPending()`**, so in *either* order the collapse cannot see a
write that is mid-flight. The user re-picks, the in-flight write lands after their new one, and the plan sits on
a week they abandoned — the same last-write-wins exposure D13 recorded and accepted. Nothing in Reps can close
this, because the read and the delete are two transactions with the queue's own state machine moving between
them.

## Change

Give `ActionQueue` a supersede-on-enqueue primitive that performs **delete-matching + insert in one
`readwrite` transaction** on `actions`, so no interleaving is observable — and decide what it does about a
matching row already `syncing` (cancel-on-response, or refuse and let the caller collapse after the drain;
the choice is the substance of this task, not a detail).

Then thread it through `ApiClient` so a queueable write can declare what it supersedes, the way `pinned(...)`
declares `idPinned` today — a `supersedes` key on the meta, evaluated at enqueue time.

Framework-side, so it lands in `Birko.Web.Core` with its frontend tests in `Birko.Web.Playground`'s
`backport-smoke.ts` (the TASK-094 pattern — there is no in-framework JS unit runner). Reps then drops the
`navigator.onLine` branch in `setWeekOffset` and passes the declaration instead.

## Acceptance criteria

- [ ] `ActionQueue` can supersede-and-enqueue in one transaction; no interleaving leaves the outbox holding
      neither the old intent nor the new one, and no `update()` `put` can resurrect a superseded row
- [ ] The `syncing` case is decided **and written down**, not left to whichever branch happens to run
- [ ] Declared through `ApiClient` meta rather than by callers reaching into the queue
- [ ] `setWeekOffset` uses it and the `navigator.onLine` ordering branch is deleted
- [ ] Covered in `backport-smoke.ts`, and TASK-172's in-page sampler guard still passes in both lanes
- [ ] Red-verified: the resurrection case fails before the change

## Out of scope

- The offline empty-window itself — fixed in TASK-172.
- The broader multi-device last-write-wins question, which is D13's accepted exposure and becomes real only
  with multi-user (STORY-007).

## Human test plan

- [ ] On the phone, in real airplane mode: re-pick the week, reconnect, and immediately re-pick again while the
      sync chip is still draining. The plan must land on the last week chosen, never on the abandoned one.
