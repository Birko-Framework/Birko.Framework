# Birko.Helpers

## Overview
General-purpose utility classes for the Birko Framework — string manipulation, HTML stripping, path validation, CSV parsing, expression building, object comparison, and collection diffing.

## Project Location
`Birko.Helpers/` (Shared Project - `.shproj`)

## Components

- **StringHelper.cs** — Extension methods: `RemoveMultipleSpaces`, `CalculateSHA256Hash`, `CalculateSHA512Hash`, `ToHexText` (slug generation moved to `SlugGenerator` in Birko.Data.Patterns)
- **HtmlHelper.cs** — Static methods: `StripTagsRegexCompiled`, `StripATagsRegexCompiled`, `StripImgTagsRegexCompiled`
- **ObjectHelper.cs** — Static methods: `Compare` (null-safe IComparable), `CompareHash` (constant-time byte[] comparison)
- **PathValidator.cs** — Path security: `ValidatePath`, `CombineAndValidate`, `ValidateUserPath`, `NormalizePath`, `SanitizePath`, `ValidateDirectory`, `CombineAndValidateUnchecked`
- **CsvParser.cs** — RFC 4180-compliant CSV parser with lazy `IEnumerable<IList<string>>`, configurable delimiter/enclosure/encoding, BOM-aware
- **ExpressionBuilder.cs** — Generic `ExpressionBuilder<T>` for composing LINQ expression predicates with `And`, `Or`, `AndIf`, `OrIf`, `Not`, `Build`
- **EnumerableHelper.cs** — Collection utilities:
  - `DiffByKey<T>(current, desired, keySelector, filter?)` — O(n) HashSet-based diff returning `DiffResult<T>` (Added, Removed, Unchanged)
  - `Diff<T>` — [Obsolete] Old tuple-based diff with custom equality function, replaced by `DiffByKey`
- **DiffResult.cs** — Generic `DiffResult<T>` with `Added`, `Removed`, `Unchanged` as `IReadOnlyList<T>`
- **Process/ProcessRunner.cs** — `ProcessRunner.RunAsync(ProcessRunRequest)` → `ProcessRunResult(ExitCode, StandardOutput,
  StandardError, TimedOut, Duration)`, the ONE process runner (TASK-534; `RunCommandTool` and `GitService` use it).
  - Arguments go through `ArgumentList`, with no shell. `RawArguments` exists for a caller that holds one command string.
  - stdout and stderr are drained concurrently from the start, so a child cannot deadlock on a full pipe.
  - A timeout kills the process tree and is reported (`TimedOut`). The caller's cancellation kills the tree and throws.
  - `Environment` and `StandardInput` are optional; a program that cannot start throws `Win32Exception`.
  - In the `Birko.Helpers` namespace: a `Birko.Helpers.Process` namespace would shadow `System.Diagnostics.Process`.

## Dependencies
- None (standalone utility project)

## Maintenance
- When adding new helpers, register the file in `Birko.Helpers.projitems`
- Add corresponding tests in `Birko.Helpers.Tests`
