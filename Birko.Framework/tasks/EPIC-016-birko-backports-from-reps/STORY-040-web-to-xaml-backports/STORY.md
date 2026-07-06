---
id: STORY-040
parent: EPIC-016
status: done
created: 2026-07-06
---

# Web → Xaml UI / offline / device backports

## User story

As a **Birko.Xaml / Avalonia consumer**, I want the generic UI, offline and device capabilities that
landed in `Birko.Web.*` from Reps to have Xaml analogues where they make sense, so that the Xaml UI
family isn't permanently behind the web family for the same app shapes.

## Behaviour

- A mobile app-shell (fixed top-bar + bottom-nav) exists for Xaml.Shell, driven by the existing
  `ModuleDefinition` nav-model (the Xaml twin of the web `Surface[]`).
- `Birko.Xaml.Core` gains a `Formatter` (duration + culture-aware date/number/currency) beside its
  translation-only `I18n`.
- Optional device / offline capabilities (wake-lock, offline read-through mirror, sync indicator,
  audio cue) get Xaml homes where a mobile/occasionally-connected Avalonia target justifies them.

## Backport-review basis (2026-07-06 frontend Web→Xaml review)

The Xaml family (`Birko.Xaml.Core` / `.Shell` / `.Avalonia`) has **no** mobile shell, offline
mirror, sync/action-queue, device layer, or formatter today. Per-item verdicts:

| Web backport | Xaml verdict | Task |
|---|---|---|
| BMobileAppShell (038) | **Strongest candidate** — Xaml has only desktop sidebar+ribbon shells; `Surface[]` maps 1:1 onto `ModuleDefinition` | TASK-043 |
| Formatter.duration (041) | **Clean candidate** — `I18n` is translation-only, no formatter | TASK-044 |
| Wake Lock (039) | Candidate — device capability, needs `IWakeLock` (Core) + Avalonia impl | TASK-045 |
| MirrorStore/readThrough (036) | Candidate (concept) — sits behind `ICrudDataSource<T>`; IndexedDB impl is web-only | TASK-046 |
| `<b-sync-status>` (037) | Candidate **contingent on 046** — nothing to bind to until a Xaml queue/mirror exists | TASK-047 |
| Audio cue (040) | Weakest — only "beep + vibrate" ports; Web-Audio/iOS-priming is web-specific | TASK-048 |
| PWA service worker (035) | **Web-only** — no Xaml analogue (native binary, no precache layer) — no task |

**Precondition** for the offline/device trio (045/046/047/048): `Birko.Xaml.Avalonia` currently
targets `net8.0` only (no Android/iOS TFMs). These are worth doing only once/if Avalonia targets
mobile or occasionally-connected desktop — hence lower priority.
