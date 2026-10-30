# Birko.Web ↔ Birko.Xaml parity

Where every Birko.Web building block stands on the desktop ([FEATURE-021](../Birko.Framework/docs/features/FEATURE-021-web-xaml-parity-list/idea.md)).
One row per web component. `ParityListTests` in `tests/Birko.Xaml.Avalonia.Tests` reads this file and the
`Birko\Web` checkout beside the framework, and fails when:

- a `b-*.ts` in `Birko.Web.Components/src` has no row here;
- a row names a component that no longer exists on the web;
- a status is not one of those below, or a component has two rows;
- a `skipped` row gives no reason in its Note.

Without the web checkout the test skips and says so; with `BIRKO_REQUIRE_LIVE` set it fails instead.

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
| `n/a` | nothing to port (used by the shell section for type-only files) |

The Desktop column names the counterpart in plain words for now.

## Components

| Web component | Status | Desktop | Note |
|---|---|---|---|
| `b-command-palette` | built | `CommandPalette` | |
| `b-badge` | themed | `BBadge` theme on `ContentControl` | |
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
| `b-tag` | themed | `BTag` theme on `ContentControl` | |
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
| `b-color-picker` | needs-theme | `ColorPicker` | in the `Avalonia.Controls.ColorPicker` package, not referenced yet |
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
| `b-card` | themed | `BCard` theme on `ContentControl` | |
| `b-carousel` | needs-theme | `Carousel` | |
| `b-chat` | skipped | | a messaging surface, of little use in a desktop back-office app |
| `b-confirm-dialog` | built | `DialogService.ConfirmAsync` | |
| `b-drawer` | built | `Drawer` | |
| `b-dropdown-menu` | themed | `MenuFlyout` | `MenuFlyoutPresenter` and `MenuItem` themes |
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
