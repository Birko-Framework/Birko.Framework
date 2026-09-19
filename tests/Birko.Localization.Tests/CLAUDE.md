# Birko.Localization.Tests

## Overview
Unit tests for the Birko.Localization project.

## Project Location
- **Path:** `tests/Birko.Localization.Tests/`
- **Type:** Test Project (.csproj, net10.0)
- **Framework:** xUnit 2.9.3, FluentAssertions 7.0.0

## Test Classes
- **LocalizationSettingsTests** — Immutable settings, With* builders, chaining
- **StringInterpolatorTests** — Named/positional placeholders, edge cases
- **ThreadCultureResolverTests** — Thread culture resolution, default culture
- **CldrPluralizerTests** — English, Slovak, Polish, Russian, Chinese, Arabic, French plural rules, negative counts
- **LocalizerTests** — Fallback chain, missing key behaviors, interpolation, key prefix, HasTranslation
- **InMemoryTranslationProviderTests** — Builder, dictionary constructor, GetAll, supported cultures
- **JsonTranslationProviderTests** — Flat/nested keys, regional cultures, constructor validation
- **ResxTranslationProviderTests** — RESX parsing, culture listing, constructor validation
- **CompositeTranslationProviderTests** — Priority ordering, culture union, merge behavior
- **NumberFormatterTests** — Culture-aware number/currency/percent formatting
- **DateFormatterTests** — Short date, custom format, relative time (past/future/edge cases)

## Test Resources
- `TestResources/en.json` — English translations (flat + nested)
- `TestResources/sk.json` — Slovak translations
- `TestResources/sk-SK.json` — Slovak (Slovakia) regional translations
- `TestResources/Messages.en.resx` — English RESX translations
- `TestResources/Messages.sk.resx` — Slovak RESX translations

## Running Tests
```bash
dotnet test Birko.Localization.Tests/
```
