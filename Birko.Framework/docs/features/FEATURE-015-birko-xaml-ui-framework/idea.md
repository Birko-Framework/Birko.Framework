---
id: FEATURE-015
created: 2026-06-25
owner: both
# status: idea | review (built, sign-off pending) | done | dropped | superseded
status: idea
---

# Birko.Xaml — Avalonia-first XAML UI framework mirroring Birko.Web

> Stakeholder-readable. Backfilled on 2026-08-01 from [EPIC-015](../../../tasks/EPIC-015-birko-xaml-ui-framework/EPIC.md),
> which predates this repo's feature tree. **Nothing here is reconstructed narrative** — the Problem
> section is the epic's own "Area of concern" text, and the decision ledger is built from its real
> stories. See [decisions.md](decisions.md) § History log for what that backfill does and does not claim.

## Problem

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

## Proposed shape

- `Birko.DesignTokens` single-sources tokens; regenerating `tokens.css` is byte-identical to today's hand-authored file (parity proof).
- All four themes (light/dark/neon/finstat) render in an Avalonia gallery app and swap at runtime via `ThemeVariant`.
- Tier-1 restyled native controls visually match their `b-*` counterparts.
- The page bases (list / detail / split / form-modal) work as drop-in base ViewModels + generic views.
- Convention deviation (real assemblies vs `.projitems`) documented in CLAUDE.md.

## Open questions distilled from the grill

_None recorded._ This feature was backfilled from an epic, so no [[grill-me]] interview preceded it and
there are no `proposed` rows awaiting a verdict. Questions raised from here on belong in
[decisions.md](decisions.md) as new `proposed` rows.

## Out of scope (initial)

- Not recorded at the time. The epic's `affects:` list is the closest thing to a scope boundary:
  `[Birko.Xaml.Core, Birko.Xaml.Avalonia, Birko.Xaml.Shell, Birko.DesignTokens, Birko.Web.Components]`.

## Prototype

**N/A — backfilled.** This feature predates the prototype step, so no prototype decision was taken at
the time and inventing one retroactively would misrepresent the record. Any *future* scope added to this
feature takes the prototype decision explicitly, as a new decision row.
