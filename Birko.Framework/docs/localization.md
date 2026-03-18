# Birko.Localization

## Overview

Birko.Localization provides a pluggable localization system with culture fallback chains, CLDR-based pluralization, and multiple translation storage backends. It is a shared project (.shproj) with no external dependencies.

## Architecture

```
ILocalizer (main API)
    └── Localizer (default implementation)
            ├── ITranslationProvider (backend)
            │   ├── JsonTranslationProvider
            │   ├── ResxTranslationProvider
            │   ├── InMemoryTranslationProvider
            │   └── CompositeTranslationProvider
            ├── ICultureResolver
            │   └── ThreadCultureResolver
            └── LocalizationSettings
                └── MissingKeyBehavior
```

## Culture Fallback Chain

When resolving a translation key, the `Localizer` follows this fallback chain:

1. **Exact culture** — e.g., `sk-SK`
2. **Parent culture** — e.g., `sk` (if `FallbackToParentCulture` is enabled)
3. **Default culture** — configured in `LocalizationSettings`
4. **Missing key behavior** — `ReturnKey`, `ReturnEmpty`, or `ThrowException`

## Translation Providers

### JsonTranslationProvider

Loads translations from JSON files named `{culture}.json`:

```
locales/
├── en.json
├── sk.json
└── sk-SK.json
```

Supports flat and nested key formats:

```json
{
  "greeting": "Hello",
  "errors": {
    "notFound": "Not found"
  }
}
```

Nested keys are automatically flattened: `errors.notFound`.

### ResxTranslationProvider

Loads from standard .resx XML files:

```
resources/
├── Messages.resx        (default culture)
├── Messages.en.resx
└── Messages.sk.resx
```

### InMemoryTranslationProvider

Dictionary-based provider with a fluent builder:

```csharp
var provider = InMemoryTranslationProvider.Create()
    .AddTranslation("en", "greeting", "Hello")
    .AddTranslation("sk", "greeting", "Ahoj")
    .Build();
```

### CompositeTranslationProvider

Chains multiple providers by priority (first non-null result wins):

```csharp
var composite = new CompositeTranslationProvider(
    databaseOverrides,  // highest priority
    jsonDefaults        // fallback
);
```

## CLDR Pluralization

The `CldrPluralizer` implements CLDR plural rules for 30+ languages:

| Language Family | Forms | Languages |
|----------------|-------|-----------|
| East Asian | 1 (other) | zh, ja, ko, vi, th |
| Germanic/Romance | 2 (one, other) | en, de, nl, sv, es, it, pt |
| French | 2 (one incl. 0, other) | fr |
| Czech/Slovak | 3 (one, few 2-4, other) | sk, cs |
| Polish | 3 (one, few, other) | pl |
| East Slavic | 3 (one, few, other) | ru, uk, hr, sr |
| Arabic | 6 (zero, one, two, few, many, other) | ar |

### Example: Slovak

```csharp
var pluralizer = new CldrPluralizer();
var sk = CultureInfo.GetCultureInfo("sk");

pluralizer.GetPluralForm(1, sk);  // 0 → "1 deň"
pluralizer.GetPluralForm(3, sk);  // 1 → "3 dni"
pluralizer.GetPluralForm(5, sk);  // 2 → "5 dní"
```

## String Interpolation

### Named placeholders

```csharp
localizer.Get("welcome",
    new Dictionary<string, object?> { ["userName"] = "John" },
    culture);
// Template: "Welcome, {userName}!" → "Welcome, John!"
```

### Positional placeholders

```csharp
localizer.Get("items", new object[] { 5 }, culture);
// Template: "You have {0} items" → "You have 5 items"
```

## Formatting

### NumberFormatter

```csharp
var formatter = new NumberFormatter();
formatter.Format(1234.56m, enUS);           // "1,234.56"
formatter.FormatCurrency(99.99m, "USD", enUS); // "$99.99"
formatter.FormatPercent(0.75m, enUS);       // "75.00 %"
```

### DateFormatter

```csharp
var formatter = new DateFormatter();
formatter.Format(date, enUS);                    // "3/18/2026"
formatter.Format(date, "yyyy-MM-dd", invariant); // "2026-03-18"
formatter.FormatRelative(past, now);             // "5 minutes ago"
formatter.FormatRelative(future, now);           // "in 3 hours"
```

## Custom Translation Provider

Implement `ITranslationProvider` for custom backends (e.g., database):

```csharp
public class DatabaseTranslationProvider : ITranslationProvider
{
    private readonly IAsyncStore<Translation> _store;

    public string? GetTranslation(string key, CultureInfo culture)
    {
        // Query your store
    }

    public IReadOnlyList<CultureInfo> GetSupportedCultures() { ... }
    public IReadOnlyDictionary<string, string> GetAll(CultureInfo culture) { ... }
}
```

## Configuration

```csharp
var settings = LocalizationSettings.Default
    .WithDefaultCulture(CultureInfo.GetCultureInfo("en"))
    .WithFallbackToParentCulture(true)
    .WithMissingKeyBehavior(MissingKeyBehavior.ReturnKey)
    .WithKeyPrefix("myapp");
```

All settings are immutable — `With*()` methods return new instances.
