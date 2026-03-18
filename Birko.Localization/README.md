# Birko.Localization

Localization and translation framework for the Birko Framework. Provides culture-aware string resolution, CLDR pluralization, and pluggable translation backends.

## Features

- **Culture fallback chain** — exact culture → parent culture → default culture
- **Multiple translation providers** — JSON files, RESX files, in-memory, composite (chainable)
- **CLDR pluralization** — Proper plural form selection for 30+ languages including Slovak, Czech, Polish, Russian, Arabic
- **String interpolation** — Named `{placeholder}` and positional `{0}` placeholders
- **Culture-aware formatting** — Numbers, currencies, percentages, dates, relative time
- **Immutable settings** — Thread-safe configuration with fluent builder
- **No external dependencies** — Uses only .NET built-in libraries

## Usage

### Basic Setup with JSON Files

```csharp
// Load translations from JSON files (en.json, sk.json, etc.)
var provider = new JsonTranslationProvider("/path/to/locales");
var settings = LocalizationSettings.Default
    .WithDefaultCulture(CultureInfo.GetCultureInfo("en"));
var localizer = new Localizer(provider, settings);

// Simple lookup
var greeting = localizer.Get("greeting", CultureInfo.GetCultureInfo("sk")); // "Ahoj"

// Named interpolation
var welcome = localizer.Get("welcome",
    new Dictionary<string, object?> { ["userName"] = "John" },
    CultureInfo.GetCultureInfo("en")); // "Welcome, John!"

// Positional interpolation
var items = localizer.Get("items", new object[] { 5 },
    CultureInfo.GetCultureInfo("en")); // "You have 5 items"
```

### JSON File Format

Supports both flat and nested keys:

```json
{
  "greeting": "Hello",
  "errors": {
    "notFound": "Not found",
    "forbidden": "Access denied"
  }
}
```

Nested keys are automatically flattened to dot notation: `errors.notFound`.

### In-Memory Provider (Testing)

```csharp
var provider = InMemoryTranslationProvider.Create()
    .AddTranslation("en", "greeting", "Hello")
    .AddTranslation("sk", "greeting", "Ahoj")
    .Build();
```

### Composite Provider (Layered Overrides)

```csharp
var composite = new CompositeTranslationProvider(
    databaseProvider,  // highest priority
    jsonProvider       // fallback
);
```

### CLDR Pluralization

```csharp
var pluralizer = new CldrPluralizer();

// Slovak: 3 forms (one, few 2-4, other 5+)
pluralizer.GetPluralForm(1, CultureInfo.GetCultureInfo("sk"));  // 0 (deň)
pluralizer.GetPluralForm(3, CultureInfo.GetCultureInfo("sk"));  // 1 (dni)
pluralizer.GetPluralForm(5, CultureInfo.GetCultureInfo("sk"));  // 2 (dní)

// Arabic: 6 forms
pluralizer.GetPluralFormCount(CultureInfo.GetCultureInfo("ar")); // 6
```

### Relative Time Formatting

```csharp
var formatter = new DateFormatter();
formatter.FormatRelative(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow); // "5 minutes ago"
formatter.FormatRelative(DateTime.UtcNow.AddDays(1), DateTime.UtcNow);     // "tomorrow"
```

### RESX Files

```csharp
var provider = new ResxTranslationProvider("/path/to/resources", "Messages");
// Loads Messages.en.resx, Messages.sk.resx, etc.
```

## Translation Providers

| Provider | Source | Use Case |
|----------|--------|----------|
| `JsonTranslationProvider` | `{culture}.json` files | Production, file-based |
| `ResxTranslationProvider` | `{baseName}.{culture}.resx` files | .NET standard resources |
| `InMemoryTranslationProvider` | Dictionary | Testing |
| `CompositeTranslationProvider` | Multiple providers | Layered overrides |

## License

MIT License - see [License.md](License.md) for details.
