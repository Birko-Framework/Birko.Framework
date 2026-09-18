---
id: TASK-044
parent: STORY-040
feature: FEATURE-016
status: done
priority: P2
assignee: ai
created: 2026-07-06
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Formatter for Birko.Xaml.Core (duration + culture-aware)

## Context

`Birko.Web.Core` gained `Formatter.duration()` + `Intl` options passthrough from Reps (STORY-038 /
origin TASK-041). The Web→Xaml review found `Birko.Xaml.Core` has **no formatter layer** — its
`I18n`/`II18n` does translation only (key→string lookup + `{count}`/`{placeholder}` replacement, no
date/number/currency/duration). This is a clean, generic addition.

`duration(totalSeconds)` → `m:ss` / `h:mm:ss` is locale-independent logic that ports verbatim; the
locale-aware date/number/currency methods map onto .NET `CultureInfo`/`ToString`. Bind the formatter
to the existing `I18n.Locale` so it follows the app's current culture.

## Acceptance criteria

- [x] A `Formatter` in `Birko.Xaml.Core` with `Duration(...)` (`m:ss` / `h:mm:ss`) matching the web output. — `Localization/Formatter.cs` + `IFormatter.cs`; exact-parity theory test.
- [x] Culture-aware `Date` / `Number` / `Currency` helpers driven by the current `I18n.Locale` (→ `CultureInfo`). — resolved per call from `II18n.Locale`; also `Time`/`DateTime`/`Percent`.
- [x] Lives in Core (Avalonia-free); no dependency on `Birko.Data` or Avalonia. — enforced by existing `CoreIsAvaloniaFreeTests`.
- [x] Tests in `Birko.Xaml.Core.Tests` covering duration boundaries (0, <1min, ≥1h) and a couple of cultures. — `FormatterTests.cs`, 16 tests (en-US vs de-DE).
- [x] `Recent Updates` entry added. — `Birko.Framework/CLAUDE.md` + `Birko.Xaml.Core/CLAUDE.md`.

## Out of scope

- Reworking `I18n` translation behaviour — this is additive beside it.
- A web-parity `Intl` options object — map to the .NET-idiomatic `CultureInfo`/format-string approach instead.

## Human test plan

- [ ] N/A — fully covered by automated tests (pure formatting logic).

## Implementation plan

_Populated by `/tasks plan TASK-044` — leave empty until then._
