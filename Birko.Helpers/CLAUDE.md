# Birko.Helpers

## Overview
General-purpose utility classes for the Birko Framework — string manipulation, HTML stripping, path validation, CSV parsing, expression building, object comparison, and collection diffing.

## Project Location
`C:\Source\Birko.Helpers\` (Shared Project - `.shproj`)

## Components

- **StringHelper.cs** — Extension methods: `GenerateSlug`, `RemoveDiacritics`, `RemoveMultipleSpaces`, `CalculateSHA256Hash`, `CalculateSHA512Hash`, `ToHexText`
- **HtmlHelper.cs** — Static methods: `StripTagsRegexCompiled`, `StripATagsRegexCompiled`, `StripImgTagsRegexCompiled`
- **ObjectHelper.cs** — Static methods: `Compare` (null-safe IComparable), `CompareHash` (constant-time byte[] comparison)
- **PathValidator.cs** — Path security: `ValidatePath`, `CombineAndValidate`, `ValidateUserPath`, `NormalizePath`, `SanitizePath`, `ValidateDirectory`, `CombineAndValidateUnchecked`
- **CsvParser.cs** — RFC 4180-compliant CSV parser with lazy `IEnumerable<IList<string>>`, configurable delimiter/enclosure/encoding, BOM-aware
- **ExpressionBuilder.cs** — Generic `ExpressionBuilder<T>` for composing LINQ expression predicates with `And`, `Or`, `AndIf`, `OrIf`, `Not`, `Build`
- **EnumerableHelper.cs** — Collection utilities:
  - `DiffByKey<T>(current, desired, keySelector, filter?)` — O(n) HashSet-based diff returning `DiffResult<T>` (Added, Removed, Unchanged)
  - `Diff<T>` — [Obsolete] Old tuple-based diff with custom equality function, replaced by `DiffByKey`
- **DiffResult.cs** — Generic `DiffResult<T>` with `Added`, `Removed`, `Unchanged` as `IReadOnlyList<T>`

## Dependencies
- None (standalone utility project)

## Maintenance
- When adding new helpers, register the file in `Birko.Helpers.projitems`
- Add corresponding tests in `Birko.Helpers.Tests`
