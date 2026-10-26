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

## Expander (the accordion look)

Avalonia's own `Expander` takes the Birko look of `b-accordion` (FEATURE-020): a bordered surface, a full-width header
row whose chevron turns when open, the panel under a divider. Nothing to opt into — it is the `Expander` theme once the
Birko theme is merged. All four `ExpandDirection`s work (the header moves to the far side). A group where opening one
section closes the others is not part of it (deferred, FEATURE-020 D11).

```xml
<Expander Header="Production — 31 differences" IsExpanded="True">
  <TextBlock Text="Items[2].Revenue: 1000 → 1007" />
</Expander>
```

## Tag input

`Controls.TagInput` (FEATURE-020, the port of `b-tag-input`) is a list of short values typed as removable chips — what
the `FieldType.Tags` form field renders too. A separator (comma, newline, tab by default) commits the text before it;
Enter, Tab and leaving the field commit the rest; Backspace in the empty box removes the last tag; pasting text with a
separator adds every part.

```csharp
var icos = new TagInput { Placeholder = "IČO", Separators = ",; \n", MaxCount = 50 };
icos.TagRejected += (_, e) => status.Text = $"{e.Tag}: {e.Reason}";   // Duplicate or MaxCount, as the web's reject
icos.TagsChanged += (_, _) => Run(icos.Tags);                          // the user changed the list
icos.SetTags(saved);                                                   // no events; duplicates and the limit still apply
```

`Tags` binds two-way (`IReadOnlyList<string>`); `Value` is the comma-joined string. `AllowDuplicates`, `IsReadOnly`
and `HasError` (danger border) mirror the web attributes.

## JSON / XML diff viewer

`Controls.JsonDiffViewer` (FEATURE-020, the port of `b-json-diff`) compares two bodies with
`Birko.Xaml.Core.Diff.JsonDiff` and shows the differences as a tree: added / removed / changed values in the
success / danger / warning colours with old → new, containers holding a change open with their change count,
everything else closed. A side that does not parse shows its error instead.

```csharp
var viewer = new JsonDiffViewer
{
    LabelA = "production", LabelB = "localhost",
    Format = JsonDiffFormat.Json,                // or Xml: bodies read with XmlTree
    OnlyChanges = false,                         // hide equal and ignored branches
    Options = new JsonDiffOptions { Ignore = ["**.RequestId"], Arrays = [new JsonArrayRule("Items") { Key = "Id" }] },
};
viewer.SetData(baselineBody, otherBody);         // or bind RawA / RawB
viewer.DiffComputed += (_, r) => status.Text = $"{r.Summary.Total} differences";
viewer.ExpandAll(); viewer.CollapseAll();        // tree view; also on its toolbar
viewer.View = JsonDiffView.SideBySide;           // or the toolbar's Tree / Side by side
```

`Result` is the last `JsonDiffResult` (null while a side does not parse or an option is malformed — that is shown,
never thrown). Rows carry `st-{status}` classes and state their status as their automation name. Strings are
`bxaml.jsonDiff.*` in `I18n.Instance`, English by default. The toolbar uses the new `BToolbarButton` /
`BToolbarToggle` themes and the chevrons `BTreeNodeToggle` (`Controls/DataViewers.axaml`), reusable by any viewer;
they arrive with `Controls.axaml`, so merging either `BirkoTheme.axaml` or `BirkoTheme.Core.axaml` is enough.

**Side by side** (TASK-773) is the web view: two labelled panes, each its whole body line by line (JSON pretty-printed
in the engine's key order, XML as the original document), with added / removed / changed lines tinted and ignored ones
dimmed. The sides are not lined up, as on the web; the one desktop addition is that the panes scroll together. "Only
changes" and expand / collapse belong to the tree view. Both views are virtualized: a 373 KB body (3 000 records)
opens side by side in about 0.5 s and as a tree in about 0.2 s (headless, measured in `JsonDiffViewerSplitTests`).

## Object / JSON viewer

`Controls.ObjectTree` renders an object graph (`Source`) or a JSON string (`Json`) as a tree.
`SelectedValue` is the graph object behind the selected node and `SelectedPath` its dotted path
(`user.roles[0]`).

```csharp
tree.HideNullMembers = true;   // leave out null properties / dictionary entries / JSON fields
tree.Source = response;        // the nodes stay the real objects, so SelectedValue can be serialized
```

`HideNullMembers` (default `false`) skips **named** members whose value is null, and the header
counts (`{n}`) count only what is shown. Array and enumerable items are always shown, null ones
included: their `[i]` index is their identity. A null root still shows as one `null` leaf.

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
