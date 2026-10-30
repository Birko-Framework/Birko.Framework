# Birko.Web ↔ Birko.Xaml parity

Where every Birko.Web building block stands on the desktop ([FEATURE-021](../Birko.Framework/docs/features/FEATURE-021-web-xaml-parity-list/idea.md)).
One row per web component (`## Components`) and one per Birko.Web.Shell source file except `index.ts`
(`## Shell`, keyed by its path under `src` without `.ts`). `ParityListTests` in `tests/Birko.Xaml.Avalonia.Tests` reads this file and the
`Birko\Web` checkout beside the framework, and fails when:

- a `b-*.ts` in `Birko.Web.Components/src`, or a `.ts` in `Birko.Web.Shell/src`, has no row here;
- a row names a component that no longer exists on the web;
- a status is not one of those below, or a component has two rows;
- a `skipped` row gives no reason in its Note.

Without the web checkout the test skips and says so; with `BIRKO_REQUIRE_LIVE` set it fails instead.
In CI the `xaml-web-parity` workflow checks out Birko.Web beside the framework, so there it never skips. It runs on
changes to Birko.Xaml and its tests; **a change on the web side does not trigger it** — after adding or removing a
web component or shell file, run it by hand (Actions → xaml-web-parity → Run workflow, or
`gh workflow run xaml-web-parity.yml`).

A new web component therefore fails the desktop test suite until it has a row. Decide its status, and if it is
`missing`, its port is separate work (a feature or a task), not part of adding the row.

| Status | Means |
|---|---|
| `built` | a Birko desktop control (or service) does what the web component does |
| `themed` | an Avalonia control in the Birko look covers it — nothing to port |
| `form-only` | available only as a `Form` field type, not as a control of its own |
| `needs-theme` | Avalonia has the control, but Birko does not style it yet |
| `missing` | nothing on the desktop yet |
| `skipped` | not needed on the desktop, on purpose — the Note says why |
| `n/a` | nothing to port: a type-only file whose types have no desktop use, or a browser-only helper |

**The Desktop column.** Every name in backticks there is checked by `ParityCounterpartTests`, whether or not the
web checkout is present: a type (`TagInput`), a member (`DialogService.Notify`), or a Birko theme key (`BCard`).
Words outside backticks are not. By status:

- `built` — a type or member in the Birko.Xaml assemblies;
- `form-only` — a `FieldType` member;
- `themed` — a Birko theme key, or an Avalonia control the Birko theme styles (a `ControlTheme` for its type in
  `BirkoTheme.axaml`, or a style in `DataGridStyles.axaml`);
- `needs-theme` — an Avalonia control the Birko theme does **not** style yet, so the row must move to `themed` when
  its theme lands.

`built`, `themed` and `form-only` rows must name at least one counterpart. Names in the Note are not checked.

## Components

| Web component | Status | Desktop | Note |
|---|---|---|---|
| `b-command-palette` | built | `CommandPalette` | |
| `b-badge` | themed | `BBadge` | a `ContentControl` theme |
| `b-chart` | built | `BChart` | over LiveCharts2 |
| `b-code-block` | needs-theme | `SelectableTextBlock` | no syntax highlighting |
| `b-data-table` | themed | `DataGrid` | `DataGridStyles.axaml` |
| `b-definition-list` | missing | | |
| `b-editable-table` | themed | `DataGrid` | the grid edits natively |
| `b-json-diff` | built | `JsonDiffViewer` | |
| `b-json-viewer` | built | `ObjectTree` | its `Json` property |
| `b-kanban` | built | `Kanban` | |
| `b-object-tree` | built | `ObjectTree` | |
| `b-pagination` | missing | | `DataGrid` does not page |
| `b-pre` | needs-theme | `SelectableTextBlock` | |
| `b-sortable-item` | missing | | an item of `b-sortable-list` |
| `b-sortable-list` | missing | | |
| `b-stat` | missing | | |
| `b-table` | themed | `DataGrid` | |
| `b-tag` | themed | `BTag` | a `ContentControl` theme |
| `b-xml-viewer` | built | `XmlViewer` | |
| `b-empty` | skipped | | an app composes its own empty state from text and an icon |
| `b-progress` | themed | `ProgressBar` | |
| `b-skeleton` | skipped | | desktop screens load local or cached data; `BusySpinner` covers the wait |
| `b-spinner` | built | `BusySpinner` | |
| `b-stale-banner` | skipped | | an app composes it from a `Border`; `SyncStatusIndicator` shows sync state |
| `b-sync-status` | built | `SyncStatusIndicator` | |
| `b-toast` | built | `DialogService.Notify` | |
| `b-button` | themed | `Button` | |
| `b-checkbox` | themed | `CheckBox` | |
| `b-color-picker` | needs-theme | | Avalonia's `ColorPicker`, in the `Avalonia.Controls.ColorPicker` package, not referenced yet |
| `b-date-picker` | themed | `CalendarDatePicker` | |
| `b-date-range-picker` | form-only | `FieldType.DateRange` | two pickers |
| `b-datetime-picker` | form-only | `FieldType.DateTime` | a date picker and a time picker |
| `b-file-upload` | form-only | `FieldType.File` | a path box and Browse |
| `b-form` | built | `Form` | |
| `b-inline-edit` | missing | | |
| `b-input` | themed | `TextBox` | |
| `b-markdown-editor` | built | `MarkdownEditor` | |
| `b-multi-select` | form-only | `FieldType.MultiSelect` | a multi-select `ListBox` |
| `b-option-group` | form-only | `FieldType.OptionGroup` | a group of radio buttons |
| `b-qty-stepper` | needs-theme | `NumericUpDown` | |
| `b-radio` | themed | `RadioButton` | |
| `b-range` | themed | `Slider` | |
| `b-rating` | missing | | |
| `b-search-input` | form-only | `FieldType.Search` | a plain `TextBox`: no clear button, no debounce |
| `b-segmented` | skipped | | a styled `ListBox` or a group of radio buttons does the same |
| `b-select` | themed | `ComboBox` | |
| `b-sortable-picker` | missing | | |
| `b-switch` | themed | `ToggleSwitch` | |
| `b-tag-input` | built | `TagInput` | |
| `b-textarea` | themed | `TextBox` | with `AcceptsReturn` |
| `b-time` | themed | `TimePicker` | |
| `b-accordion` | themed | `Expander` | |
| `b-button-group` | skipped | | a `StackPanel` of buttons does the same |
| `b-card` | themed | `BCard` | a `ContentControl` theme |
| `b-carousel` | needs-theme | `Carousel` | |
| `b-chat` | missing | | may come later, e.g. for a desktop version of the agent chat |
| `b-confirm-dialog` | built | `DialogService.ConfirmAsync` | |
| `b-drawer` | built | `Drawer` | |
| `b-dropdown-menu` | themed | `MenuFlyoutPresenter` and `MenuItem` | the look of a `MenuFlyout` |
| `b-gallery` | skipped | | an image gallery is a web-app pattern, not a desktop back-office one |
| `b-modal` | built | `Modal` | |
| `b-split-panel` | built | `SplitPanel` | |
| `b-tabs` | themed | `TabControl` | |
| `b-toolbar` | skipped | | a panel of buttons does the same, and the `Ribbon` covers command bars |
| `b-tooltip` | themed | `ToolTip` | |
| `b-tour` | skipped | | guided product tours are a web onboarding pattern |
| `b-breadcrumb` | built | `Breadcrumb` | |
| `b-ribbon` | built | `Ribbon` | |
| `b-sidebar` | built | the sidebar of `ShellView` (Birko.Xaml.Shell) | |
| `b-tree-menu` | themed | `TreeView` | |

## Shell

| Web file | Status | Desktop | Note |
|---|---|---|---|
| `auth/auth-guards` | missing | | `ModuleDefinition.Permission` is declared, but nothing enforces it |
| `auth/auth-session` | missing | | reads tokens from the sign-in redirect's URL fragment and refreshes them |
| `auth/auth-store` | missing | | the desktop shell has only `UserName` and `UserCommands` |
| `auth/auth-types` | n/a | | types of the auth store |
| `commands/entity-search-provider` | missing | | the palette lists modules and themes, not entities |
| `commands/module-nav-provider` | built | `ShellViewModel.PaletteCommands` | navigate-to-module entries |
| `connection/connection-state` | missing | | `SyncStatusIndicator` shows sync state, not connection |
| `dashboard/base-dashboard-widget` | missing | | |
| `modules/breadcrumb` | missing | | the desktop shell shows no breadcrumbs; the `Breadcrumb` control exists |
| `modules/module-store` | built | `NavigationService` | its `Modules` registry |
| `modules/module-types` | built | `ModuleDefinition` | |
| `modules/permissions` | missing | | see `auth/auth-guards` |
| `modules/ribbon-builder` | missing | | the host fills `ShellViewModel.RibbonTabs` by hand |
| `modules/route-builder` | built | `NavigationService.Navigate` | module ids instead of hash routes |
| `notifications/notification-store` | missing | | `DialogService.Notify` shows a toast but keeps no list |
| `notifications/notification-types` | n/a | | types of the notification store |
| `pages/base-crud-page` | built | `CrudViewModelBase` | |
| `pages/base-detail-page` | built | `DetailPageViewModel` and `DetailPageView` | |
| `pages/base-form-modal` | built | `FormModal` | |
| `pages/base-list-page` | built | `ListPageViewModel` and `ListPageView` | |
| `pages/base-page` | built | `BasePageViewModel` | |
| `pages/base-split-page` | built | `SplitPageViewModel` and `SplitPageView` | |
| `pages/endpoint-utils` | n/a | | builds REST URLs; desktop pages read through `ICrudDataSource` |
| `pages/live-refresh` | missing | | |
| `shell/b-app-shell` | built | `RibbonShellView` | |
| `shell/b-core-app-shell` | built | `ShellViewModel` | |
| `shell/b-mobile-app-shell` | built | `MobileShellView` | |
| `shell/b-sidebar-app-shell` | built | `ShellView` | |
| `shell/breadcrumbs` | missing | | see `modules/breadcrumb` |
| `shell/mobile-nav` | built | `MobileNavItem` | |
| `shell/shell-types` | n/a | | types shared by the web shells |
| `shell/shell-wrapper` | built | `ViewLocator` | swaps the page inside a persistent shell |
| `shell/theme-registry` | built | `IThemeManager` and `AvaloniaThemeManager` | |
| `tenants/branding` | missing | | the desktop shell has a tenant switcher but no per-tenant branding |
| `tenants/tenant-types` | n/a | | types of the tenant switcher; the desktop's is a list of names |
