---
id: TASK-048
parent: STORY-040
feature: null
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

# Xaml audio-cue device util (beep + vibrate)

## Context

`Birko.Web.Core` gained an iOS-safe audio cue + priming util from Reps (STORY-038 / origin TASK-040).
The Web→Xaml review rated this the **weakest** candidate: most of what makes the web version a task —
the shared gesture-unlocked `AudioContext`, the `playback` audio-session opt-in, the "primed inside a
user gesture" dance — is Web Audio / iOS-Safari specific and does not transfer. The backportable
residue is just "play a short tone + optional vibration," which .NET/Avalonia implements very
differently.

Kept for completeness and to sit beside the other device utils; low priority. Gated on the same
Avalonia-mobile precondition as the rest of the device/offline trio.

## Acceptance criteria

- [x] An `IAudioCue` (play short tone, optional vibrate) in `Birko.Xaml.Core` (Avalonia-free contract). — `Device/IAudioCue.cs` + `AudioCueOptions`.
- [x] A best-effort implementation in `Birko.Xaml.Avalonia` (desktop tone; vibrate no-op until a mobile backend). — `Device/AvaloniaAudioCue.cs` (`Console.Beep` on Windows off the UI thread; `BeepCore` hook for mobile).
- [x] No attempt to replicate the Web-Audio priming/session model — documented as web-only. — in the `IAudioCue` doc-comment.
- [x] Tests for the neutral contract (no-throw when unsupported). — `AudioCueTests` (BeepAsync never throws).
- [x] `Recent Updates` entry added.

## Out of scope

- Web-Audio gesture-priming / audio-session semantics (web-only, not portable).
- A working mobile vibrate — gated on Avalonia mobile TFMs.

## Human test plan

- [x] No-op / best-effort paths don't throw. — `AudioCueTests` (BeepAsync with options + defaults, never throws).
- [ ] **(deferred — subjective / needs device)** On a supported target, confirm an audible tone (and vibration on mobile).

## Implementation plan

_Populated by `/tasks plan TASK-048` — leave empty until then._
