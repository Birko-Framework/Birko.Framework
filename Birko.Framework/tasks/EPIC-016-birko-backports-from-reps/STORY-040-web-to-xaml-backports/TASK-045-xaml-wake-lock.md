---
id: TASK-045
parent: STORY-040
feature: FEATURE-016
status: done
priority: P3
assignee: ai
created: 2026-07-06
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Xaml wake-lock device abstraction (IWakeLock)

## Context

`Birko.Web.Core` gained a Screen Wake Lock manager from Reps (STORY-038 / origin TASK-039). The
Web→Xaml review found the Xaml family has **no device-capabilities layer at all**. Keeping the screen
awake is a real cross-platform capability (Avalonia mobile/desktop backends expose it), so it fits
the framework's Core-interface / Avalonia-implementation split.

The web file's `visibilitychange` re-acquire logic is browser-specific detail, not the concept — the
portable part is "acquire / release a wake lock, re-acquire on resume."

**Precondition:** most valuable once `Birko.Xaml.Avalonia` targets mobile (it is `net8.0`-only today).
Deliver the neutral abstraction now; the platform impl can be a desktop no-op until mobile TFMs exist.

## Acceptance criteria

- [x] `IWakeLock` (acquire/release, idempotent, safe to call when unsupported) defined in `Birko.Xaml.Core` (Avalonia-free). — `Device/IWakeLock.cs`.
- [x] An implementation in `Birko.Xaml.Avalonia` (desktop no-op / best-effort; structured so a mobile backend slots in). — `Device/AvaloniaWakeLock.cs` with `AcquireCore`/`ReleaseCore` hooks.
- [x] Re-acquire-on-resume semantics documented (the web `visibilitychange` equivalent) even if a no-op on desktop. — in the `IWakeLock` doc-comment.
- [x] Tests for the neutral abstraction (acquire→release state, no-throw when unsupported). — `WakeLockTests` (idempotent acquire/release, IsActive).
- [x] `Recent Updates` entry added.

## Out of scope

- A working mobile (Android/iOS) implementation — gated on Avalonia mobile TFMs (note in STORY-040).

## Human test plan

- [x] Desktop: confirm no-op doesn't throw. — covered by `WakeLockTests` (acquire/release/idempotent, no throw).
- [ ] **(deferred — needs a mobile TFM)** On a mobile Avalonia target, acquire during a timed activity and
      confirm the screen doesn't dim; release and confirm normal timeout resumes.

## Implementation plan

_Populated by `/tasks plan TASK-045` — leave empty until then._
