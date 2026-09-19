# Birko.Localization

## Overview
Translations and culture support for the Birko Framework. Provides a pluggable localization system with culture fallback chains, CLDR-based pluralization, and multiple translation storage backends.

## Project Location
- **Path:** `Birko.Localization/`
- **Type:** Shared Project (.shproj/.projitems)
- **Namespace:** `Birko.Localization`

## Components

### Core Interfaces (`Core/`)
- **ILocalizer** — Main entry point for resolving localized strings (with named/positional interpolation)
- **ITranslationProvider** — Backend contract for loading translations (JSON, RESX, database, etc.)
- **ICultureResolver** — Determines current and default cultures
- **IPluralizer** — CLDR-based plural form selection
- **INumberFormatter** — Culture-aware number/currency/percent formatting
- **IDateFormatter** — Culture-aware date formatting with relative time support
- **LocalizationSettings** — Immutable configuration (default culture, fallback behavior, key prefix, missing-key behavior)
- **MissingKeyBehavior** — Enum: ReturnKey, ReturnEmpty, ThrowException

### Formatting (`Formatting/`)
- **StringInterpolator** — Internal utility for named `{placeholder}` and positional `{0}` interpolation. Takes an `IFormatProvider` so values format in the resolved translation culture, not the ambient thread culture (CR-L276); `Localizer` passes the resolved culture.

### Providers (`Providers/`)
- **Localizer** — Default ILocalizer with fallback chain: exact culture → parent culture → default culture → missing-key behavior
- **ThreadCultureResolver** — Resolves culture from CultureInfo.CurrentUICulture
- **CldrPluralizer** — CLDR plural rules for 30+ languages (including Slovak/Czech 3-form, Polish, Russian, Arabic 6-form)
- **NumberFormatter** — Wraps .NET CultureInfo number formatting
- **DateFormatter** — Short date, custom format, relative time ("5 minutes ago", "yesterday", "in 3 hours"). Relative phrasing is **English-only** by design — the culture param affects only the numeric/date formatting paths, not the relative wording (CR-L275).

### Translation Providers (`Translation/`)
- **InMemoryTranslationProvider** — Dictionary-based, with fluent builder. For testing.
- **JsonTranslationProvider** — Loads from `{culture}.json` files. Supports flat and nested keys (auto-flattened to dot notation). Lazy-loaded with thread-safe caching.
- **ResxTranslationProvider** — Loads from `{baseName}.{culture}.resx` XML files. Parses `<data name="key"><value>` elements.
- **CompositeTranslationProvider** — Chains multiple providers by priority (first non-null wins). Merges GetAll with higher-priority override.

## Dependencies
- No external dependencies (uses System.Text.Json, System.Xml.Linq, System.Globalization from .NET)

## Key Patterns
- **Immutable settings** with `With*()` builder methods (like Birko.Time's HolidayCalendar)
- **Culture fallback chain**: exact → parent → default → missing-key behavior
- **Lazy loading with ConcurrentDictionary.GetOrAdd** for thread-safe caching in file-based providers
- **Case-insensitive key lookup** across all providers

## Maintenance
- When adding new translation providers (e.g., database), implement ITranslationProvider
- When adding new languages to CldrPluralizer, add entry to PluralRules dictionary
- Test plural rules thoroughly with edge cases (11, 12, 21, 22, 111, 112 for Slavic languages)
