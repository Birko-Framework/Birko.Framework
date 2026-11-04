---
id: TASK-815
parent: EPIC-005
feature: null
status: done
priority: P2
assignee: ai
created: 2026-11-04
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# StringTemplateEngine: named value filters, and a `money` filter that formats an amount in its own currency

## Context

Asked by Symbio TASK-1047 (2026-11-04). An e-mail prints money raw: `{{TotalAmount}} {{CurrencyCode}}` renders
`390.0 CZK`. The engine stringifies a value with `ToString()`, so the separator follows the render culture, but the
scale is whatever the value was stored with, and the symbol, its position and the decimals are the template author's
guess per language. The storefront shows `390,00 Kč` (cs) and `CZK 390.00` (en).

Ruled by the user (Symbio TASK-1047): a filter in the engine, `{{TotalAmount|money:CurrencyCode}}`, formatted the way
FlowerFurStudio's `StorefrontMoney` does (matching the browser's `Intl.NumberFormat` for CZK and EUR, cs and en).

## Acceptance criteria

- [x] A placeholder may carry one filter: `{{Path|name}}` or `{{Path|name:ArgPath}}`. `ArgPath` is a model path,
      resolved like any placeholder. Filters are supplied through `TemplateRenderOptions` (name → filter); the engine
      ships none registered by default.
- [x] A filter name the options do not know, or a filter used with no options, throws `TemplateRenderException` —
      never ships the marker text to a recipient. `{{#if Path}}` is unchanged (no filter there).
- [x] The encoder runs on the filter's output, and `RawPaths` still exempts by path.
- [x] `MoneyTemplateFilter` (name `money`): formats a numeric value as currency in the render culture
      (`CultureInfo.CurrentCulture`), in the currency named by the argument, never the culture's own currency:
      the culture's own format when the code is its region's currency; otherwise `€`/`$`/`£`, or the ISO code (with
      a no-break space after it when the symbol comes first); two places for CZK/EUR whatever the value's scale;
      no-break spaces where `Intl.NumberFormat` puts them. Pinned: cs `390,00 Kč`, `390,00 €`; en `CZK 390.00`,
      `€390.00`; a value stored as `390.0` and `390` print the same.
- [x] Tests for both, mutation-proven; Birko.Messaging.Tests green.

## Out of scope

- Loops over lists (`Lines`), else branches, nested sections.
- Symbio's registration and template docs (Symbio TASK-1047).

## Human test plan

N/A — covered by the engine tests and Symbio TASK-1047's delivery tests.

## Implementation plan

1. `StringTemplateEngine`: widen the placeholder pattern to `\{\{(path)(?:\|(\w+)(?::(path))?)?\}\}`; resolve the
   value (and the argument path), look the filter up in the options, and fail loudly as above.
2. `ITemplateValueFilter { string Apply(object? value, object? argument, CultureInfo culture); }` and
   `TemplateRenderOptions.Filters` (`IReadOnlyDictionary<string, ITemplateValueFilter>`, case-insensitive).
3. `MoneyTemplateFilter` in `Birko.Messaging.Templates`, ported from `StorefrontMoney` and generalized (no
   storefront-specific default language).
4. Tests in `tests/Birko.Messaging.Tests`: filter syntax, unknown filter, encoder order, money cs/en × CZK/EUR and
   scale.

## Progress log

- 2026-11-04: done. 17 tests in `TemplateFilterTests`; Birko.Messaging.Tests 116/116. Mutation-proven four ways: filter
  ignored (14 red), the culture's own currency always (4 red), no no-break spaces (9 red), unknown filter passed through
  (2 red). README § *Filters* and CLAUDE.md updated. Consumed by Symbio TASK-1047.
