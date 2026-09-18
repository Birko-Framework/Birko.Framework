# Birko.Xaml.Avalonia

The Avalonia skin of the Birko design system (EPIC-015). STORY-030 delivers the **theme system**:
the generated token dictionaries + runtime theme switching that matches Birko.Web's look.

## Usage

Merge the tokens and restyled controls into your app, and pick a theme. Ship **only the themes you
offer** — light + dark is the core set:

```xml
<Application ... RequestedThemeVariant="Light">
  <Application.Resources>
    <ResourceInclude Source="avares://Birko.Xaml.Avalonia/BirkoTheme.Core.axaml" />
  </Application.Resources>
</Application>
```

Add any extra theme by merging its own dictionary next to the core include:

```xml
<ResourceInclude Source="avares://Birko.Xaml.Avalonia/BirkoTheme.Core.axaml" />
<ResourceInclude Source="avares://Birko.Xaml.Avalonia/Themes/Tokens.Neon.axaml" />
```

Or take all four at once with `BirkoTheme.axaml`. Core+extras is ~43% lighter than the all-in bundle.

> Light **and** dark are both in the core set deliberately: `ThemeVariant.Dark` has no
> `InheritVariant`, so a light-only app resolves no tokens at all under OS dark mode. Neon and
> Finstat are safely omissible — they inherit Dark and Light respectively.

Reference tokens in controls via `DynamicResource` (never hard-coded values):

```xml
<Border Background="{DynamicResource BColorPrimaryBrush}"
        CornerRadius="{DynamicResource BRadius}" />
```

Switch theme at runtime:

```csharp
IThemeManager themes = new AvaloniaThemeManager();   // after the app is initialized
themes.Available;                                    // exactly the themes you merged
themes.SetTheme("neon");                             // false if neon wasn't shipped
```

`SetTheme` sets `Application.RequestedThemeVariant`; every `DynamicResource` re-resolves live — no
restart. `Available` is **detected** from the dictionaries you actually merged, so the switcher never
offers a theme whose tokens are missing; pass `available:` to the constructor to override.

## How it works

- **`Themes/` is generated** by `Birko.DesignTokens` — never hand-edited. One dictionary per theme
  (`Tokens.Light.axaml` … `Tokens.Finstat.axaml`), each a `ResourceDictionary` with a single
  `ThemeDictionaries` entry holding that theme's resolved `Color`s, lengths (`x:Double`, rem baked to
  px), and `FontFamily`s. Each is complete and self-contained, which is what lets you merge any
  subset. `Tokens.Brushes.axaml` declares the brushes once, each linking its `Color` via
  `DynamicResource` so it tracks the active variant — required alongside *any* subset.
  `Tokens.axaml` is the all-four aggregate.
- Every theme dictionary also declares **`BThemeId`**, naming itself. That is what
  `AvaloniaThemeManager.DetectThemes` reads — presence alone can't work, since an omitted variant
  inherits its base and would answer regardless.
- **`Theming.BirkoThemeVariants`** — the `ThemeVariant` instances. Light/Dark reuse the built-ins; **Neon** (inherits Dark) and **Finstat** (inherits Light) are custom variants. The *same* static instances are the `ThemeDictionaries` keys (via `{x:Static}`) and the value assigned to `RequestedThemeVariant`, so lookup matches by identity.
- **`Theming.AvaloniaThemeManager`** — `IThemeManager` over `RequestedThemeVariant`.

## Scope

STORY-030 = tokens + theme swap. Composite/motion tokens (shadows, focus rings, transitions,
easings, gradients) are not yet mapped. Restyled controls are STORY-034; the gallery app is
STORY-031.

## Convention note

Real Avalonia `net8.0` assembly (Avalonia 11.2.3) — the EPIC-015 deviation (compiled AXAML through
`.projitems` is fragile). Referenced via `ProjectReference`, not the `.projitems` aggregator.
