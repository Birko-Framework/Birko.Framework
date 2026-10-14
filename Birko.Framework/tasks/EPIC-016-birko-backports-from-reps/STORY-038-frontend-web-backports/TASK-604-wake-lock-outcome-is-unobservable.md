---
id: TASK-604
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
findings: [FIELD-042]
pr: null
github-issue: null
jira-key: null
---

# Nothing reports whether the screen wake lock was ever held, so "the screen went dark" cannot be diagnosed

> **Moved from WorkoutTracker TASK-165 on 2026-10-14.** It was filed there as Birko.Web work, where the framework's pick never saw it. Every TASK / STORY / FEATURE id in the body below is **WorkoutTracker's**. There, TASK-165 stays open as the Reps-side check, blocked on this task.


## Context

Reported on the **device pass of 2026-08-10**: *"the app did not hold the screen awake"*.

This task deliberately does **not** claim the wake lock is broken, because that could not be established.
`createWakeLockManager` (`Birko.Web.Core/src/device/wake-lock.ts`) reads correctly: it re-acquires on
`visibilitychange` — which is required, since the browser drops the lock whenever the page hides — and guards a
second in-flight request so a sentinel cannot leak. `session-page.ts:147` acquires it on resume and `:181`
releases it on unmount. Nothing in that is obviously wrong.

The problem is that **every failure path is silent by construction**:

```ts
const wl = (navigator as ...).wakeLock;
if (!wl) return;                      // unsupported — no signal
try { ... } catch { /* unsupported / denied — degrade silently */ }
```

So a lock that was never granted (older iOS, low-power mode, a denied request, a non-secure context) is
indistinguishable from one held successfully, both to the user and to anyone debugging. `held` exists on the
manager but nothing reads it. The result is a bug report that cannot be actioned: the observation is real, and
there is no way to tell which of five causes produced it.

Silently degrading is the right *runtime* behaviour — a workout should not be interrupted by a dialog about a
screen lock. Being un-inspectable is not.

## Change

Make the outcome observable without making it noisy:

- Surface the acquisition result from the manager (a callback, an event, or simply reading `held` at a known
  point) so a consumer can know whether the request succeeded, was denied, or was unsupported.
- In Reps, expose it where a device pass can read it — the existing `vh-debug` style diagnostic surface is the
  natural home, and it is already flag-gated so it costs nothing in normal use.
- Consider a one-time, low-key notice when a session starts and the lock could not be acquired, so the user
  knows to set their auto-lock manually rather than discovering it mid-set.

Note the installed-PWA storage split: a Home Screen app has its own storage, so a flag-gated diagnostic enabled
in Safari will **not** be enabled in the installed app. Whatever is built has to be reachable in the context
being tested.

## Acceptance criteria

- [ ] Whether the wake lock is held is inspectable at runtime on a real device.
- [ ] Unsupported / denied / granted are distinguishable from one another.
- [ ] Normal operation stays silent — no dialogs, no console noise in the happy path.
- [ ] The diagnostic is reachable from the **installed** app, not only a Safari tab.
- [ ] Framework change noted under STORY-009.

## Out of scope

- Any change to when the lock is acquired or released — the current policy is not in question until the
  measurement says otherwise.

## Human test plan

- [ ] On the phone, start a workout and confirm from the diagnostic whether the lock is held.
- [ ] If it is held and the screen still sleeps, re-file against the platform behaviour with that evidence.
- [ ] If it is not held, the reason is now visible — file the real cause.
