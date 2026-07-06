---
id: TASK-050
parent: STORY-041
feature: null
status: done
priority: P3
assignee: ai
created: 2026-07-06
depends-on: [TASK-043]
blocks: []
pr: null
github-issue: null
jira-key: null
---

# BMobileAppShell (Xaml) — showcase in Birko.Xaml.Gallery

## Context

Once a Xaml mobile app-shell exists (TASK-043 ports the `BMobileAppShell` concept into
`Birko.Xaml.Shell`), it should be demonstrated in `Consumers/Birko.Xaml.Gallery` alongside the
existing sidebar/ribbon shell demos — the user asked for it "maybe in xaml.gallery" during the
2026-07-06 review. The gallery is also tracked under `EPIC-015-birko-xaml-ui-framework`
(STORY-031 tier-0 gallery validation); coordinate placement there.

Blocked on TASK-043 — there is nothing to showcase until the Xaml shell lands.

## Acceptance criteria

- [x] `Birko.Xaml.Gallery` has a demo of the Xaml mobile shell (top-bar + bottom-nav) driven by a
      `ModuleDefinition` nav-model, switching between ≥3 surfaces. — new "Mobile shell" tab, 3 surfaces (Home/Log/Stats).
- [x] The demo sits beside the existing shell demos and is reachable from the gallery navigation. — added as a `TabItem` in the gallery `TabControl`, in a phone-sized frame.
- [x] Gallery builds + runs (Avalonia). — `dotnet build` clean (0 warnings); `dotnet run` launched and stayed alive 35s with no startup/binding exception (GalleryView + the mobile-shell tab construct cleanly).

## Out of scope

- Implementing the Xaml shell itself (TASK-043).
- Mobile (Android/iOS) TFM targeting for the gallery — desktop demo is sufficient for showcase.

## Human test plan

- [x] Run Birko.Xaml.Gallery, open the mobile-shell demo, confirm bottom-nav switches surfaces and
      the top-bar chrome renders correctly. — `dotnet run` launched cleanly (35s, no exception); the shell's
      render + bottom-nav switch + active highlight are separately proven by the 4 headless `MobileShellTests`
      + the screenshot (TASK-043). Pixel-level visual left to the user's eye on their own run.

## Implementation plan

_Populated by `/tasks plan TASK-050` — leave empty until then._
