---
id: EPIC-015
status: done
created: 2026-06-25
closed: 2026-07-06
owner: both
affects: [Birko.Xaml.Core, Birko.Xaml.Avalonia, Birko.Xaml.Shell, Birko.DesignTokens, Birko.Web.Components]
---

## Completion (2026-07-06)

All 8 stories done. Delivered `Birko.DesignTokens` (single-source tokens → byte-identical web CSS +
Avalonia AXAML), the Avalonia theme system (4 variants, runtime swap), `Birko.Xaml.Core` (i18n +
MVVM base VMs, Avalonia-free), ~20 Tier-1 restyled controls + building blocks (Form/Drawer/SplitPanel/
Modal), 7 Tier-2 composites (tree-menu, command-palette, object/JSON + XML viewers, kanban,
markdown-editor, chart on LiveCharts2), and `Birko.Xaml.Shell` with both sidebar and ribbon chrome,
nav, page bases, command palette, user/tenant areas, and page transitions. WPF deferred (shares
tokens/VMs; forks templates). The gallery lives in Consumers/. ~130 tests across the DesignTokens +
Xaml suites, CSS parity guarded.

# Birko.Xaml — Avalonia-first XAML UI framework mirroring Birko.Web

## Area of concern

A desktop UI framework that brings the Birko.Web design system, component catalogue, app
shell, and page shapes to native XAML (WPF/Avalonia) apps — so a desktop app (e.g. a Finstat
admin tool) can look and behave like its Birko.Web counterpart.

**Decisions locked in (design discussion 2026-06-25):**
- **Avalonia first**, not WPF. Avalonia 11's styling model (`Styles`, `ControlTheme`,
  `DynamicResource`, `ThemeVariant`) is far closer to CSS-tokens-and-themes than WPF's, it's
  cross-platform, and runtime theme-swap is first-class. WPF can come later as a *second skin*
  over shared tokens + view models (its XAML dialect differs — share tokens/VMs, **fork the templates**).
- **Primary goal = visual parity with Birko.Web.** This makes the single-source token
  generator the keystone (see below), not an optional nicety.
- **Greenfield / framework-first** — built as reusable `Birko.*` siblings, no specific consumer yet.

### The keystone — single-source design tokens

Because parity is the goal, tokens must not be hand-authored twice (they'd drift immediately).
`tokens.css` stops being the source of truth and becomes a *generated* artifact alongside the
Avalonia dictionaries:

```
Birko.DesignTokens/
  tokens.json            ← single source: light + dark + neon + finstat
  generate.(ts|cs)       ← emits all targets
        ├─► Birko.Web/.../css/tokens.css  (+ themes/*.css)   — existing, now generated
        └─► Birko.Xaml.Avalonia/Themes/   Tokens.axaml + Theme.{Light,Dark,Neon,Finstat}.axaml
```

Each `--b-color-primary: #2563eb` → paired `Color` + `SolidColorBrush` resource
(`BColorPrimary` / `BColorPrimaryBrush`). Spacing `rem`→px baked at generation time (XAML has
no `rem` cascade). `[data-theme]` blocks → swappable merged `ResourceDictionary`s wired to
Avalonia `ThemeVariant`. All four themes cross over for free (just more rows in `tokens.json`).
Round-trip proof: regenerating the existing CSS must produce it unchanged.

### Project layout (Birko sibling convention)

```
Birko.Xaml.Core        — platform-neutral: I18n singleton + {l:Tr} markup ext,
                         token-consumption helpers, base ViewModels (CRUD/list/detail)
Birko.Xaml.Avalonia    — ControlThemes for native controls + custom b-* controls + Themes/*.axaml
Birko.Xaml.Shell       — app chrome: ribbon → sidebar → status bar, command palette, base pages
(later) Birko.Xaml.Wpf — second skin: reuses Core + tokens, forks the templates
```

Naming mirrors `Birko.Web.Core / .Components / .Shell`.

**Convention deviation to document:** Avalonia control libraries with compiled AXAML want to be
**real csproj assemblies**, not `.shproj`/`.projitems` shared projects (AXAML-as-AvaloniaResource
through `.projitems` is fragile). So `Birko.Xaml.*` would be the first siblings shipping as
actual class-library assemblies rather than the aggregator/`.projitems` pattern in CLAUDE.md.
Justified, but a deliberate break — call it out in CLAUDE.md.

### What carries over from Birko.Web.Core — almost nothing (and that's good)

Birko.Web.Core mostly *polyfills*, in the browser, things .NET + Avalonia + Birko.Framework
already provide. Reuse the role-fillers that already exist; only port the two platform-neutral concepts.

| Web.Core module | Desktop story | Verdict |
|---|---|---|
| `base-component` (Shadow DOM, render→string) | Avalonia `TemplatedControl`/`UserControl` + DataTemplates + binding | Native — don't port |
| `state` (Signal/computed/Store) | `INotifyPropertyChanged`, `ObservableCollection`, CommunityToolkit.Mvvm | Native — don't port |
| `state/persist` (localStorage) | `Birko.Configuration` + `Birko.Data.JSON`/`InMemory` | Already in framework |
| `storage` (IndexedDB, Cache API) | Filesystem + SQLite + `Birko.Data.*` store hierarchy | Already in framework (richer) |
| `http/api-client` (token/tenant inject, ProblemDetails) | `Birko.Communication.REST` (`RestClient`) | Already in framework |
| `http/event-source`, `websocket-client` | `Birko.Communication.gRPC`, `ClientWebSocket` (GraphQL client wraps it) | Already in framework |
| `offline` (sync-manager, action-queue) | `Birko.Data.Sync` | Already in framework |
| `router` (hash router) | Avalonia `ViewLocator` + region + Shell module/route map | Re-architect in Shell |
| `i18n` (singleton, formatters) | Port the concept; can sit on `Birko.Localization` | **Port** ✅ |
| `css/length` | Tokens bake px at generation | Drop |

So `Birko.Xaml.Core` is deliberately **thin**: i18n + `{l:Tr}` + base ViewModels + token helpers.

### Component catalogue (~55 b-* → idiomatic XAML)

API shifts from imperative (`el.setConfig({...})`) to binding-first MVVM (StyledProperties +
bindings + base VMs). Visual/token parity carries over 100%; the programming model becomes
idiomatic Avalonia. Tiered:
- **Tier 1 — restyled native controls (~20):** input/select/button/checkbox/switch/radio/
  textarea/tabs/card/badge/tag/tooltip/table/data-table/modal/drawer/dropdown/progress/spinner/
  breadcrumb — mostly mechanical `ControlTheme`s. ~80% of the visual value.
- **Tier 2 — composite controls with no native peer:** ribbon, command-palette, kanban,
  tree-menu, json/xml/object-tree viewers, markdown-editor, chart (needs a plotting lib or custom).

### Page shapes (Birko.Web.Shell base pages) — port best of all

These are mostly *behavior* (data load, CRUD orchestration, permission gating, command wiring),
which MVVM was built for — so they cross over cleaner than the components. Only the view
templates are new.
- `BasePage` → `BasePageViewModel` + base view
- `BaseCrudPage` → `CrudViewModelBase<T>` (filters, ObservableCollection, Create/Edit/Delete commands, permissions)
- `BaseListPage<T>` → `ListPageViewModel<T>` + generic ListView
- `BaseSplitPage<T>` (master-detail) → SplitPanel over Avalonia `SplitView` + `GridSplitter` (responsive collapse)
- `BaseDetailPage<T>` → `DetailPageViewModel<T>` (load-by-id, form bind, save/cancel)
- `BaseFormModal<T>` → dialog Window/overlay + Form + Save/Cancel

**Forced decision:** to keep the CRUD bases *declarative* (`formSchema`), port `b-form` as a
schema-driven `Form` control — otherwise consumers hand-roll every XAML form and the bases lose
their drop-in quality. `Form`, `Drawer`, and `SplitPanel` are therefore Tier-1 dependencies of
the whole page layer (same shell→components dependency Birko.Web already has).

## Success criteria

- `Birko.DesignTokens` single-sources tokens; regenerating `tokens.css` is byte-identical to today's hand-authored file (parity proof).
- All four themes (light/dark/neon/finstat) render in an Avalonia gallery app and swap at runtime via `ThemeVariant`.
- Tier-1 restyled native controls visually match their `b-*` counterparts.
- The page bases (list / detail / split / form-modal) work as drop-in base ViewModels + generic views.
- Convention deviation (real assemblies vs `.projitems`) documented in CLAUDE.md.

## Build order (determined by dependencies)

Decomposed into 8 stories (dependency-ordered):

1. **STORY-029** — Tier 0: single-source design tokens + multi-target generator (`Birko.DesignTokens`). *(start here)*
2. **STORY-030** — Tier 0: Avalonia theme system + runtime `ThemeVariant` swap.
3. **STORY-031** — Tier 0 validation: gallery app + 3–4 first controls (go/no-go gate before the full sweep).
4. **STORY-032** — `Birko.Xaml.Core`: i18n (`{l:Tr}`) + base ViewModels (Avalonia-free).
5. **STORY-033** — Building blocks: schema-driven `Form`, `Drawer`, `SplitPanel`.
6. **STORY-034** — Tier 1: restyled native controls (~20).
7. **STORY-035** — Tier 2: composite controls (ribbon, command-palette, kanban, viewers, markdown-editor, chart).
8. **STORY-036** — Tier 3: `Birko.Xaml.Shell` — page bases + app chrome + navigation.

(STORY-032 can land any time after STORY-031; STORY-033 depends on Tier-0 + much of Tier-1; STORY-036 is last — it sits on 032/033/034/035.)

## WPF addendum — adding WPF after Avalonia

WPF is deferred, but three constraints below must be honored **during** the Avalonia build —
they cost nothing now and are expensive to retrofit. Principle: **share tokens + view models +
behavior; fork templates + markup-extension wrappers + window chrome.**

### Delta when WPF starts

| Layer | WPF delta | Cost |
|---|---|---|
| `Birko.DesignTokens` | One new emitter — WPF `ResourceDictionary` (.xaml) target; `tokens.json` unchanged; same `Color`+`SolidColorBrush` pairs, same baked px | Near-zero |
| `Birko.Xaml.Core` | Reused as-is **if Avalonia-free**; only the `{l:Tr}` markup ext needs a thin WPF wrapper (markup-ext namespaces differ; live locale re-resolution = binding to the I18n singleton) | Low |
| `Birko.Xaml.Wpf` (new assembly) | The bulk — every Avalonia `ControlTheme` → WPF `Style`+`ControlTemplate`; different trigger model (`:pointerover` selectors → `Trigger`/`VisualStateManager`/`IsMouseOver`), different XAML namespaces, `StyledProperty` → `DependencyProperty`. Token *references* carry over (same brush keys), so only template structure + control code-behind is rewritten | High |
| Shell views | Page-base **VMs reused**; generic views (ListView, SplitPanel, nav host) fork | Medium |
| Gallery app | New WPF gallery mirroring the Avalonia one (parity proof) | Low–Medium |
| TFM | New `net8.0-windows` + `<UseWPF>true</UseWPF>` assembly (Avalonia is plain `net8.0`) | trivial |

### WPF-specific divergences

- **No modern default look** — Avalonia 11 ships Fluent; WPF stock controls are Aero-era, so WPF re-templates *every* native control just to look current. WPF Tier-1 is **more** work per control than Avalonia's.
- **Ribbon** — WPF has a native `System.Windows.Controls.Ribbon` (Avalonia has none), so the WPF `b-ribbon` can lean on it — a place WPF is easier.
- **Theme swapping is manual** — no `ThemeVariant`; WPF swaps `Application.Resources.MergedDictionaries` at runtime (`DynamicResource` refs update live). Generated theme-manager differs per platform.
- **Window chrome forks** — custom title bar / acrylic-mica: WPF `WindowChrome` + DWM vs Avalonia `TransparencyLevelHint`.
- **DataGrid** — WPF's is in-box and mature (Avalonia's is a separate package), so that one is fine.

### Constraints to honor NOW (during the Avalonia build)

1. **Keep `Birko.Xaml.Core` strictly Avalonia-free** — no `using Avalonia.*`. One Avalonia type in a base VM and WPF can't reuse Core. Enforce from day one.
2. **Pick a charting lib that targets both WPF and Avalonia** up front (LiveCharts2 / ScottPlot / OxyPlot all do) — an Avalonia-only choice forces a `b-chart` rewrite for WPF.
3. **Push everything platform-neutral out of the platform assemblies** — base VMs, commands, schema models, navigation logic in `Core`/`Shell.Core`; only templates + views + window chrome in `.Avalonia`/`.Wpf`. Thinner platform assemblies = cheaper second skin.

### Bottom line

Tokens, VMs, and behavior reuse for free; the expensive part is re-templating every control —
and WPF's dated baseline makes that *more* per-control work than Avalonia. Rough split: Tier-0 +
Core nearly free, Tiers 1–2 are roughly a full second pass of *template* authoring (C# behavior
reused, visual templates not). The token generator is what keeps the two skins visually identical
despite forked templates.

## Open questions (parked)

- Where `tokens.json` physically lives — it bridges the TS `Birko\Web` bucket and the .NET `Birko\Framework` bucket.
- Generator implementation — tiny TS script vs C# tool.
- Whether `Birko.Xaml.Core` i18n builds on `Birko.Localization` or stands alone.
- Plotting library for `b-chart` equivalent (LiveCharts2 / ScottPlot / OxyPlot / custom).

## Out of scope (epic level)

- WPF skin (deferred — Avalonia first; WPF later shares tokens/VMs, forks templates).
- Mobile/browser (Avalonia WASM) targets.
