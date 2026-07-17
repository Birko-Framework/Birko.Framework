# Birko Framework — Changelog

Newest-first record of architectural and behavioral changes that preserve design context. New notes may land first in the short `Recent Updates` section of [CLAUDE.md](CLAUDE.md) and roll here once that section grows (via the project-local `/roll-changelog` skill). The definitive change history is `git log`; this file is a summarized narrative for architecture-level decisions that would be hard to reconstruct from commit-level diffs.

---

## 2026-07-17 — Tenant isolation hardening — EPIC-017

Multi-tenant fail-closed support across the data + event layers (from a Symbio security review):
- **`TenantIsolationMode { Permissive (default), Strict }`** in `Birko.Data.Tenant` (STORY-044) — Strict throws on "no tenant in scope" instead of silently spanning all tenants; opt in via `StoreWrapperBuilder.Build(tenantMode:)`, `AsTenantAware(mode:)`, or the `AddTenant*Repository` DI extensions. Explicit cross-tenant admin via `ITenantContext.WithAllTenants(...)` (non-breaking default interface methods). Both async **and** sync wrappers are mode-aware.
- **`StoreWrapperBuilder` decorator reorder** (STORY-045) — Tenant now sits *inside* Default/Sluggable/SoftDelete but *outside* Audit/Timestamp/EventSourcing, so per-tenant uniqueness probes are tenant-scoped (fixed a cross-tenant default-clobber + global-slug-uniqueness leak).
- **`IEventScopeAccessor`** in `Birko.EventBus` + `OutboxProcessor` scope restoration (STORY-046) — background event dispatch re-establishes the tenant an event was published under, so handlers work under Strict. New **`Birko.EventBus.Tenant`** bridge sibling implements both halves via `AddEventTenantScope()`: a publish-side `TenantEventEnricher` stamps `EventContext.TenantGuid` from the ambient `Tenant.Current` (so `OutboxEntry.TenantGuid` is correct for HTTP, jobs, and explicit `WithTenant` scopes — not just authenticated HTTP), and a consume-side `TenantEventScopeAccessor` restores it (`WithTenantAsync` / `WithAllTenants`) before handlers run.

## 2026-07-14 — Birko.Helpers.PathValidator.ValidateDirectory — reject only path-invalid chars (consumer backport)

`Birko.Helpers.PathValidator.ValidateDirectory` checked the **whole** directory path against `Path.GetInvalidPathChars()` **plus** `Path.GetInvalidFileNameChars()` — but the file-name set additionally includes the directory separators (`\`/`/`) and the drive-letter `:` on Windows, so it threw `ArgumentException` on **every absolute Windows path** (e.g. `C:\Users\…`). A directory path legitimately contains separators and a drive colon; the method now checks against `Path.GetInvalidPathChars()` only (control chars etc.), and `Path.GetFullPath` still rejects truly-malformed paths. Surfaced by a consumer app. Tests: `Birko.Helpers.Tests` 88 → 92 (`ValidateDirectory` accepts an absolute path + separators/drive-colon, rejects a NUL control char, rejects null/empty).

## 2026-07-14 — BardStudio consumer backports — SQL/Xaml/Migrations/AI fixes

Backported seven framework fixes surfaced while building the BardStudio consumer (see `Consumers/BardStudio/docs/framework-backports-prompt.md`).
- **SQL translator (`Birko.Data.SQL/SQL/DataBase.cs`)** — `string.Contains/StartsWith/EndsWith(value, StringComparison)` corrupted the LIKE pattern: the argument loop fed EVERY arg into the condition value, so the trailing `StringComparison` enum (e.g. 5) overwrote the search string → `Title LIKE '%5%'`. String-pattern methods now use only the first argument; culture/comparison args carry no SQL operand and are ignored (case-insensitivity delegated to DB collation — SQLite LIKE is already ASCII-insensitive).
- **Avalonia Button (`Controls/Buttons.axaml`)** — the template now binds `BorderBrush`/`BorderThickness` (consumer borders rendered as none before), and hover/pressed set the Button's own `Background` property (was set directly on `PART_ContentPresenter`, outranking every consumer style) so consumer styles override normally.
- **Avalonia TabItem (`Controls/Surfaces.axaml`)** — `PART_Border` binds `{TemplateBinding Background}` (default Transparent via setter) instead of a hardcoded `Transparent`, so a consumer's `:selected`/`:pointerover` Background paints.
- **Avalonia ComboBox (`Controls/Inputs.axaml`)** — obsolete `PlacementMode` → `Placement` (clears AVLN5001 on every consumer build).
- **`SqlScriptMigration` (new, `Birko.Data.Migrations.SQL`)** — raw-SQL migration base: `UpSql` (required) + optional `DownSql`, runs each against the migration context's `Connection`/`Transaction`; consumers no longer cast `IMigrationContext`→`SqlMigrationContext` or hand-roll plumbing.
- **`Message.GetText()` / `MessageText.From` (new, `Birko.AI.Contracts`)** — canonical text accessor for `Message.Content` (an `object?` — string for user turns, `List<ContentBlock>` for assistant turns); replaces the `Content is string` cast that stringified a block list to a CLR type name.
- **Nested-projitems MSB4011 (#5) — interim "middle option"** — the three nested projitems imports (`Data.Stores`→`Configuration`, `Data.Core`→`Contracts`, `Time`→`Time.Abstractions`) now carry an **include-guard sentinel**: each leaf sets a `Birko*ProjitemsImported` property and each parent conditions its nested import on it, so an aggregator listing the full closure no longer double-imports (MSB4011 gone) while a parent-only import still pulls the leaf. Keeps the documented free-closure architecture; the permanent convention decision (keep guards vs. flip to "shared projects never import shared projects") is tracked in `tasks/_loose/TASK-059`.
- Suites green: SQL 289 (+4), Migrations.SQL 34 (+6, **0 MSB4011**), AI 27 (+7), Xaml.Avalonia 134 (+4). Not committed — pending review.

## 2026-07-14 — SqLiteConnector AUTOINCREMENT DDL fix — closes CR-M166 offline (TASK-058)

`SqLiteConnector.FieldDefinition` appended a bare `AUTOINCREMENT` after any other constraints for **any** `IsAutoincrement` field — invalid SQLite: the keyword is only legal as part of an `INTEGER PRIMARY KEY AUTOINCREMENT` constraint (adjacent to PRIMARY KEY, type exactly `INTEGER`), and never on a non-PK column. So a **dual-key** model (`[PrimaryField] Guid` + a separate non-PK `[IncrementField] Id`, e.g. `SqlSyncKnowledgeItem`) made `CreateTable` throw a syntax error. Fixed ([TASK-058](tasks/_loose/TASK-058-sqliteconnector-autoincrement-ddl-non-primary-key.md), option (a)): an autoincrement **PK** now emits `INTEGER PRIMARY KEY AUTOINCREMENT` as one adjacent clause; a non-PK `[IncrementField]` emits a **plain column** (SQLite has no per-column auto-increment outside the PK — the caller assigns the value; MSSql `IDENTITY` / PostgreSQL `SERIAL` differ, documented inline). **Verified offline (no Docker):** `Birko.Data.SQL.SqLite.Tests` 24 (no regression); the SQL sync-store CRUD round-trip now runs on a real SQLite `.db` (`Birko.Data.Sync.Sql.Tests` 2 → 6). This **closed CR-M166 offline** and removed it from the STORY-028 Docker pile → STORY-026 **267/275**, STORY-028 down to **8** findings.

## 2026-07-14 — Code-review remediation — STORY-027 low findings (EPIC-014)

Worked the 418 low-severity code-review findings from EPIC-014 in ~30 batches by project cluster (Birko.AI → BackgroundJobs → Caching → the whole Communication cluster → the Data.* storage backends → the SQL provider/view family → Data.Sync). Reached **209/418**; every batch ran `/code-review` clean. This entry replaces the ~30 per-batch notes that previously lived in `CLAUDE.md` § Recent Updates — the per-finding record (CR-L001 … CR-L209, with the fix + tests for each) lives in `tasks/EPIC-014-code-review-remediation/STORY-027-low-findings`. Breaking API changes surfaced by this story are recorded as their own entries (e.g. `IPort : IDisposable` below).

## 2026-07-13 — Code-review remediation — STORY-026 medium findings (EPIC-014)

Worked the 275 medium-severity code-review findings from EPIC-014 in ~60 batches spanning every project — Birko.AI, BackgroundJobs, Caching, the full Communication cluster, the Data.* storage backends (CosmosDB / ElasticSearch / InfluxDB / MongoDB / RavenDB / the SQL family / JSON / XML / InMemory), Migrations, Models (+ the SQL mapping siblings), MessageQueue, EventBus, Security, Serialization, Storage, Structures, Telemetry, Workflow, and the TypeScript Birko.Web.* track (verified via the Playground headless build — no in-framework unit runner by design). Reached **267/275**; the remaining **8** need Docker / a live server and are tracked as STORY-028. This entry replaces the ~60 per-batch notes previously in `CLAUDE.md` § Recent Updates — the per-finding record (CR-M001 … CR-M275) lives in `tasks/EPIC-014-code-review-remediation/STORY-026-medium-findings`.

## 2026-07-14 — Birko.Communication.Ports — BREAKING: `IPort : IDisposable`, `InvokeProcessData` protected (CR-L042/L043)
Two public-API changes landed with the STORY-027 low-findings sweep. **No in-tree consumer is affected** (a scan of `Consumers/` found no source implementing `IPort` directly nor calling `InvokeProcessData`), but they are breaking for **external** consumers, so recording them here.
- **`IPort` now extends `IDisposable`** (CR-L043). Every port is disposable (`AbstractPort.Dispose()` calls `Close()`), so consumers can use `using`. **Impact:** any type implementing `IPort` **directly** (not via `AbstractPort`) must now provide a `Dispose()`. Types deriving from `AbstractPort` inherit it and are unaffected; the three ports that already had their own `Dispose` (Serial, NfcReaderPort, BluetoothLE) became `override`. The one in-tree direct implementer, the Modbus test `MockPort`, was updated.
- **`AbstractPort.InvokeProcessData()` is now `protected`** (was `public`) (CR-L042). It fires the `OnProcessData` notification internally from a derived port after it processes data — it was never part of the `IPort` contract. **Impact:** external code calling `InvokeProcessData()` on a concrete port instance won't compile; derived ports (which is every caller in-tree) are unaffected.

## 2026-07-08 — Birko.Xaml — imperative dialog service (web `dialogs` backport)
Backported the web `birko-web-components/dialogs` helper (Reps TASK-062/063) into the XAML skin: an imperative, awaitable dialog API so a view-model calls a function instead of hand-wiring a `Modal` + buttons per screen.
- **`Birko.Xaml.Core/Dialogs/IDialogService`** (Avalonia-free — Core constraint #1) + option/enum models (`ConfirmOptions`, `PromptOptions`, `ChooseOption<T>`, `DialogVariant`, `NotifyVariant`). Eight members mirroring the web: `ConfirmAsync` / `ConfirmDeleteAsync` (→`Task<bool>`), `AlertAsync`, `PromptAsync` (→`Task<string?>`), `ChooseAsync<T>` (→`Task<T?>`), `PromptFormAsync<T>` (model-based, not a value dict — matches the XAML `Form` which two-way binds a POCO → returns the mutated model or `null`), `BusyAsync<T>`/`BusyAsync`, and `Notify`.
- **`Birko.Xaml.Avalonia/Dialogs/DialogService`** renders each as a token-styled `Modal` (reused) / spinner / toast added to a host `Panel` that spans the window (supplied via a `Func<Panel?>` provider, so a VM holds only the Avalonia-free interface). Confirm/prompt/choose/promptForm reuse `Modal`; `busy` is a non-dismissable `BusySpinner` overlay; `notify` stacks transient token-colored toasts (auto-dismiss). Localized via `II18n` (`bxaml.dialog.*`) with English fallbacks.
- **Danger/secondary buttons applied here** via token brushes (`BColorDangerBrush` / `BColorSecondaryBrush`) — the framework ships only a primary `Button` theme.
- **Gotcha fixed:** `Dismiss()` flips `Modal.IsOpen=false`, which re-enters the backdrop-cancel handler — so the result must be set *before* dismissing (guard on `TaskCompletionSource.TrySetResult`'s return), else the cancel value clobbers a positive answer.
- **Tests:** `DialogServiceTests` (Avalonia suite **119→129**) — confirm/cancel, danger+delete defaults (background bound to the danger token), alert removal, prompt value/required-block/Escape, choose selection, promptForm save-returns-model / cancel-null, busy overlay-then-remove, notify toast, + a rendered screenshot. Core stays **41** (the Avalonia-free guard still holds). **Gallery visual sign-off deferred** (needs a desktop session; behaviour + a headless screenshot cover it).

## 2026-07-06 — Birko.Communication.AspNetCore — docs backfill + workspace registration
The `Birko.Communication.AspNetCore` project (owner-scoped minimal-API CRUD helpers) had shipped its code + tests but was missing the mandatory per-project docs and its `.code-workspace` folder entries. Backfilled to convention — no code change.
- **Per-project docs added** — `README.md`, `CLAUDE.md`, `License.md` (the standard trio every sibling carries), documenting `MapOwnedCrud<TModel,TRequest,TRepo>`, the host-free `OwnedCrudResults` guards (404-absent/foreign, 409-own-vs-404-foreign id, PUT/DELETE gate), and the per-resource `OwnedCrudMapping` delegate set. Requires the `Microsoft.AspNetCore.App` shared framework in the host (like `.gRPC.Server`).
- **Root registration** — added to `README.md` (Communication table), `CLAUDE-projects.md` (Communication list), `docs/communication.md` (new section), and `Birko.Framework.code-workspace` (project + `.Tests` folders; it was already in `Birko.Framework.slnx`).

## 2026-07-06 — Birko.Xaml — clickable Breadcrumb (web b-breadcrumb parity)
The Avalonia `Breadcrumb` was display-only (static `TextBlock`s from an `IEnumerable` of arbitrary objects) while the web `b-breadcrumb` renders non-last crumbs as `<a href>` links. Brought them to parity.
- **`Navigation.BreadcrumbItem`** (`Birko.Xaml.Core`, Avalonia-free) — the crumb model (`Label` + optional `Href` / `Run`), mirroring the web `{ label, href? }` items.
- **`Breadcrumb` control** (`Birko.Xaml.Avalonia`) — `ItemsSource` now accepts plain values (static text, **backward compatible** — strings still render as before) **or** `BreadcrumbItem`s. A non-last item carrying a `Run` action or an `Href` renders as a clickable text link; clicking invokes `Run` and raises a new `ItemInvoked` event (so a shell can route on `Href`). The **last** crumb is the current location and is never clickable (exact web behaviour).
- **`BBreadcrumbLink` ControlTheme** (`Controls/Nav.axaml`, merged by `Controls.axaml`) — a chrome-free `Button` styled as a link: token-driven secondary text that turns primary + underlined on hover (web `a` / `a:hover`), disabled-opacity aware. Resolved in code via `Application.Current.TryGetResource` (falls back to a plain clickable button if absent).
- **Tests:** Avalonia suite **116→119** (`Tier1TailTests`: links render only for non-last items with a target; click fires `Run` + `ItemInvoked` carrying the item/`Href`; last item is never a link). Core stays Avalonia-free (suite 41 — `BreadcrumbItem` is pure model).

## 2026-07-06 — Birko.Xaml — Form MultiSelect / Tags / File field types (EPIC-015 / TASK-057)
The remaining `b-form` field types that needed new Xaml controls — closes the field-type parity gap entirely. [tasks/EPIC-015/TASK-057](tasks/EPIC-015-birko-xaml-ui-framework/TASK-057-xaml-form-multiselect-tags-file.md).
- **MultiSelect** → a multi-`ListBox` (`SelectionMode=Multiple|Toggle`) over `Options`, synced to an `IList` model prop on SelectionChanged (SelectedItems isn't bindable); initial model selection reflected.
- **Tags** → a `WrapPanel` chip input over an `IList<string>` (seeded when null): Enter adds, ✕/Backspace removes, chips re-render (token-styled).
- **File** → a read-only path `TextBox` (bound to a `string`) + a Browse `Button` via `TopLevel.StorageProvider.OpenFilePickerAsync` (no-op headless/unsupported).
- Core stays Avalonia-free (FieldType neutral; controls in the Avalonia `Form`). Avalonia suite **111→116** (MultiSelect/Tags bind, File render, screenshot); gallery `DemoForm` exercises all three; screenshot-verified. **`FieldType` is now 21** — full parity with `b-form` (the File OS-dialog pick is the one manual check). EPIC-015 complete.

## 2026-07-06 — Birko.Web.Components — b-range vertical orientation (equalizer) (EPIC-001 / TASK-053)
The web counterpart to the Xaml Slider work: `b-range` gained a vertical mode so it can stack into an equalizer/mixer. [tasks/EPIC-001/TASK-053](tasks/EPIC-001-web-components-ui-polish/TASK-053-b-range-vertical-orientation.md).
- **`orientation` attribute** (`horizontal` default | `vertical`). Vertical: native input via `writing-mode: vertical-lr` + `direction: rtl` (up = more); the custom rail/fill overlay geometry switches to bottom/height (`_fillStyle` + `_updateFill` branch). Horizontal path untouched.
- **Layout fix:** a vertical `<input type=range>` has a huge (~600px) vertical min-content height that blew out the container; positioning it `absolute` (fills the definite slider box) makes sizing uniform.
- Playground `pg-equalizer` demo (5 slider-only vertical ranges) + README updated. Screenshot-verified in headless Chromium (grey rail, primary fill bottom→thumb, thumbs at their values). Web has no unit runner (TASK-052), so verification is verify.mjs + the screenshot.

## 2026-07-06 — Birko.Xaml — date/time picker field types; EPIC-015 complete again (TASK-056)
The last EPIC-015 gap: the Form had no date/time inputs. [tasks/EPIC-015/TASK-056](tasks/EPIC-015-birko-xaml-ui-framework/TASK-056-xaml-date-time-picker-controls.md).
- **`FieldType` +4:** `Date` (→`CalendarDatePicker.SelectedDate`), `Time` (→`TimePicker.SelectedTime`), `DateTime` (a date+time composite; handlers recombine into one `DateTime?`), `DateRange` (two pickers over a shared `Forms.DateRange` value class, seeded when the model prop is null). Core stays Avalonia-free (the composites/handlers live in the Avalonia `Form`).
- **Light restyle:** `CalendarDatePicker` + `TimePicker` ControlThemes `BasedOn` Fluent's + Birko token setters on the resting surface (border/bg/radius/font). Verified to resolve at runtime (whole theme loads). The flyout calendar/clock keep Fluent internals — full grid re-theming is a deferred follow-up. Native pickers are culture-aware automatically (screenshot: `5. 1. 2026`).
- **Tests:** Avalonia suite **106→111** (Date/Time bind, DateTime combine, DateRange seed+write, datetime screenshot). Gallery `DemoForm` now exercises all four.
- **EPIC-015 is complete again** (TASK-054/055/056 done) — the field-type/Slider follow-ups that reopened it are all landed.

## 2026-07-06 — Birko.Xaml — Slider control + Range field type (EPIC-015 / TASK-054)
Closed the Tier-1 `Slider` gap and wired a `Range` form field — the vertical/equalizer slider the review surfaced. [tasks/EPIC-015/TASK-054](tasks/EPIC-015-birko-xaml-ui-framework/TASK-054-xaml-slider-control-and-range-fieldtype.md).
- **Token-restyled `Slider` ControlTheme** (`Inputs.axaml`), horizontal **and** vertical. Custom template: a rail `Border` + a `Track` laying out DecreaseButton (primary fill) | `Thumb` (white circle, primary border) | transparent IncreaseButton; cross-axis thickness/alignment set per orientation via `:horizontal`/`:vertical` styles on the named parts (one template, both orientations). Sub-themes `BSliderRepeat` / `BSliderThumb`. All `{DynamicResource B*}`.
- **`Forms.FieldType.Range`** — `Form` renders it as a `Slider` (Min/Max, `Step`→SmallChange+TickFrequency+snap), `RangeBase.Value` two-way bound to the model. (`Min`/`Max`/`Step` came from TASK-055.)
- **Tests:** Avalonia suite **102→106** — Range-field bind, `Slider_uses_the_birko_template` (both orientations, rail+thumb present), + an equalizer screenshot. Gallery Controls tab shows a horizontal slider + a 6-up vertical equalizer bank; verified visually (grey rail, primary fill bottom→thumb).

## 2026-07-06 — Birko.Xaml — Form field-type parity with b-form (EPIC-015 / TASK-055)
The schema-driven Xaml `Form` supported only 5 field types (Text/TextArea/Number/Checkbox/Select) vs `b-form`'s ~22; this lands the "cheap half" — the types whose restyled Avalonia control already exists. [tasks/EPIC-015/TASK-055](tasks/EPIC-015-birko-xaml-ui-framework/TASK-055-xaml-form-field-type-parity.md).
- **`FieldType` +8:** `Switch` (→ToggleSwitch), `Markdown` (→MarkdownEditor), `Password` (TextBox `PasswordChar`), `Email` / `Search` (TextBox — semantic), `Percent` (numeric TextBox), `Radio` / `OptionGroup` (RadioButton group, vertical/horizontal). `Form.cs` grew the render branches; a radio group two-way-binds each button to the model via an equality converter (checked ⇔ value == option; only the newly-checked one writes back).
- **`FormField` +5 props (Core, neutral):** `Min` / `Max` (Number/Percent clamp on commit) · `Step` (carried for slider/spinner consumers — TASK-054) · `Default` (seeds the model property when null at bind time) · `Hint` (muted helper text rendered under the field).
- **Tests:** the `Form` control is Avalonia-side, so tests live in `Birko.Xaml.Avalonia.Tests` (`FormFieldTypesTests`, 8 headless) — Avalonia suite **94→102**; Core stays Avalonia-free (suite 41). Gallery `DemoForm` now shows Email/Password/Switch/Radio/Number+Hint.
- **Deferred (own tasks):** Range slider (TASK-054), date/time pickers (TASK-056), and MultiSelect/Tags/File (new controls).

## 2026-07-06 — Birko.Xaml — offline mirror + device utils (EPIC-016 / STORY-040 P3)
Closed STORY-040 (6/6) with the offline/device trio, ported as **neutral abstractions + desktop impls** (mobile backends slot in once `Birko.Xaml.Avalonia` targets mobile TFMs). [tasks/EPIC-016/STORY-040](tasks/EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/STORY.md).
- **`Data.MirrorDataSource<T>`** (`Birko.Xaml.Core`, Avalonia-free) — network-first read-through over the `ICrudDataSource<T>` port (the web `MirrorStore`/`readThrough` analogue): reads try the remote and refresh a local mirror; on failure fall back to the mirror; a remote 404 evicts the stale entry; writes pass through remote→mirror. Exposes an observable `Status` (`SyncStatus` Synced/Syncing/Offline). Caller supplies an `idOf` selector (the port has no id accessor).
- **`Controls.SyncStatusIndicator`** (`Birko.Xaml.Avalonia`) — the `<b-sync-status>` analogue; a `ContentControl` whose `Status` styled property drives localized text (`bxaml.sync.*`, English fallback) + a status class + token foreground (success/warning/danger). Binds straight to `MirrorDataSource.Status`.
- **`Device.IWakeLock` / `IAudioCue`** (`Birko.Xaml.Core`) + **`AvaloniaWakeLock` / `AvaloniaAudioCue`** (`Birko.Xaml.Avalonia`) — screen wake-lock (desktop no-op tracking `IsActive`, `AcquireCore`/`ReleaseCore` hooks for mobile) and a beep+vibrate cue (`Console.Beep` on Windows off the UI thread, no-op elsewhere; never throws). The web audio-cue's gesture-unlocked `AudioContext`/iOS priming is documented as web-only (doesn't port).
- **Tests:** Core suite **37→41** (`MirrorDataSourceTests`: passthrough / offline-fallback / 404-eviction / upsert), Avalonia suite **90→94** (`WakeLockTests`, `AudioCueTests`, `SyncStatusIndicatorTests`). Offline/online/evict behaviour is exercised via a togglable fake remote; mobile-device verification (real wake-lock, audible tone/vibration) is inherently deferred to the mobile-TFM work.

## 2026-07-06 — FIX: MSSqlStore.SetSettings was lossy (EPIC-016 / TASK-051)
Bug found while building TASK-042: the **sync** `MSSqlStore<T>.SetSettings(RemoteSettings)` rebuilt a `PasswordSettings` keeping only Location/Name/Password — dropping UserName/Port/MultipleActiveResultSets/TrustServerCertificate — so the store's connector authenticated with no user id and ignored the SQL-Server flags. (Its pre-existing `SetSettings(PasswordSettings)` override redirected *into* that lossy method, so both entry points were affected.) [tasks/EPIC-016/STORY-039/TASK-051](tasks/EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-051-fix-mssqlstore-setsettings-lossy.md).
- Fix: `SetSettings(RemoteSettings)` now passes the full settings through (`base.SetSettings((ISettings)settings)`), matching `AsyncMSSqlStore` and the MySQL/PostgreSQL sync stores. MySQL/PostgreSQL were already correct — no change.
- Regression test in `Birko.Data.SQL.Providers.Tests` (now **8**): after `SetSettings` with a full `MSSqlSettings`, the store's connector retains `User ID=…` + `MultipleActiveResultSets=True` (and is an `MSSqlSettings`, not a narrowed `PasswordSettings`).

## 2026-07-06 — Cross-provider SQL store-factory + DI (EPIC-016 / TASK-042)
Backported SQLite's store-factory + `IServiceCollection` wiring (TASK-033) to the other three SQL providers, so a server-DB host gets the same one-line setup. [tasks/EPIC-016/STORY-039](tasks/EPIC-016-birko-backports-from-reps/STORY-039-cross-provider-sql-di/TASK-042-store-factory-di-mssql-mysql-postgres.md).
- **`Add{MSSql,MySql,PostgreSql}Stores(...)`** + `{P}StoreFactory` / `I{P}StoreFactory` / `{P}StoreFactoryOptions` in `Birko.Data.SQL.{MSSql,MySQL,PostgreSQL}` (Stores/ + Extensions/). Options carry server/db/user/port/UseSecure/CommandTimeout + the provider flag (MSSql MARS+TrustServerCertificate / MySQL BulkInsertBatchSize / PG UseBinaryImport); the factory builds one shared `{P}Settings` and exposes `GetAsyncStore<T>()` + `GetConnector()`. No file-path logic (that's SQLite-only). Registered in each `.projitems`.
- **Factory hands out the *async* store** — `MSSqlStore<T>.SetSettings` is lossy (drops UserName/Port/flags, keeping only Location/Name/Password); `AsyncMSSqlStore` passes full settings. Fixing the sync store is a flagged follow-up.
- **`Birko.Data.SQL.Providers.Tests`** (new, registered in `.slnx` + `.code-workspace`) — 7 guarded tests: per-provider factory/settings/connection-string + `AddXStores` singleton resolution run offline; the **live CRUD round-trip is env-gated** (`BIRKO_{PROV}_TEST`) and skipped until a server is supplied. Task stays `review` pending that run.
- The other two backend backports were confirmed already-shared (create-tables migration + migration-transaction fix live in `Birko.Data.Migrations.SQL`, inherited by all SQL providers), so no work was needed there.

## 2026-07-06 — BMobileAppShell showcased in Playground + Gallery (EPIC-016 / STORY-041)
Reference-surface demos for the mobile shell, so it's discoverable, not just library code. Consumer-side (no framework edits). [tasks/EPIC-016/STORY-041](tasks/EPIC-016-birko-backports-from-reps/STORY-041-bmobileappshell-showcase/STORY.md).
- **Birko.Web.Playground (TASK-049, done)** — a playground-local `BMobileAppShell` subclass registered as `pg-mobile-shell` (surfaces Home/Log/Stats), shown phone-framed in the Navigation section. Headless-verified: `node verify.mjs` renders it non-empty with zero page errors; `backport-smoke` asserts one nav item per surface + hash-driven active state.
- **Birko.Xaml.Gallery (TASK-050, review)** — a "Mobile shell" tab hosting `MobileShellView` (3 surfaces) in a phone frame; gallery builds clean. Run-the-app visual sign-off pending (needs a desktop session).

## 2026-07-06 — Birko.Xaml.Shell — mobile app-shell (BMobileAppShell equivalent) (EPIC-016 / TASK-043)
The flagship Web→Xaml backport (EPIC-016, from Reps): the Xaml family shipped only desktop *sidebar* + *ribbon* shells; this adds the **mobile** chrome the web already had. [tasks/EPIC-016/STORY-040](tasks/EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-043-xaml-mobile-app-shell.md).
- **`Views/MobileShellView`** (`Birko.Xaml.Shell`) — fixed top-bar (active-surface title + theme switcher), scrolling content region (same `ViewLocator` + `CrossFade` as the other shells), and a fixed **bottom-nav** (one item per surface, `UniformGrid Rows=1`). Binds the **same `ShellViewModel`** as the desktop shells — no new shell VM.
- **`MobileNavItem` + `ShellViewModel.NavItems`** (`Birko.Xaml.Core`, Avalonia-free) — the bottom-nav model is a *projection* of the existing `ModuleDefinition` list (the review's point that the web `Surface[]` maps 1:1 onto `ModuleDefinition`), adding only an observable `IsActive`. `ShellViewModel` refreshes active state on `Navigated`; the active item highlights primary via `Classes.active`. **No new nav-model type invented.**
- **Safe-area insets** — code-behind reads the platform `IInsetsManager` and pads the top-bar (notch/status-bar) + bottom-nav (home indicator); on desktop/headless the manager is null → no-op. Deliberately keeps `Birko.Xaml.Avalonia` on `net8.0` (mobile TFMs are a separate precondition tracked in STORY-040).
- **Tests** — 6 Core VM tests (`MobileShellViewModelTests`: NavItems projection + active-surface tracking on navigate/back/command) + 4 headless render tests (`MobileShellTests`: one nav item per surface, active page via ViewLocator, surface switch, screenshot). Core suite **31→37**, Avalonia suite **→90**. Screenshot-verified at a 390×780 phone viewport (fixed top-bar + content + bottom-nav, active Home highlighted).

## 2026-07-06 — Birko.Xaml.Core — locale-aware Formatter (EPIC-016 / TASK-044)
First of the **Web→Xaml backports from Reps** (EPIC-016, migrated from the WorkoutTracker consumer). `Birko.Xaml.Core` had translation-only `I18n` but no formatter; this adds the XAML analogue of Birko.Web.Core's `createFormatter`. [tasks/EPIC-016/STORY-040](tasks/EPIC-016-birko-backports-from-reps/STORY-040-web-to-xaml-backports/TASK-044-xaml-formatter.md).
- **`Localization.IFormatter` / `Formatter`** — bound to an `II18n`; resolves `CultureInfo` from the active `Locale` at call time, so a `SetLocale` reflows formatting with no re-wiring. Avalonia-free (only `System.Globalization`).
- **`Duration(totalSeconds, alwaysHours=false)`** → `m:ss` / `h:mm:ss`, negatives clamp to `0:00`, fractional seconds floored — **byte-for-byte parity** with the web `duration()` (ported verbatim, locale-independent).
- **Culture-aware** `Date` (Short/Long/Full via `DateStyle`; Long strips the weekday to mirror Intl `{day, month:'long', year}`), `Time`, `DateTime`, `Number` (null decimals → ≤3 fraction digits), `Currency` (symbol driven by the currency **code**, not the culture, matching Intl), `Percent` (0–100 input).
- **16 xUnit + FluentAssertions tests** (`Birko.Xaml.Core.Tests/FormatterTests.cs`) — duration boundaries/clamp/floor + locale-independence, en-US vs de-DE separator parity for Number/Currency, code-driven currency symbol, Full-vs-Long weekday, live locale switch, and unknown-locale → invariant fallback. Core suite now **31**.

## 2026-07-06 — EPIC-015 (Birko.Xaml) complete — shell polish closes STORY-036
Final polish, closing STORY-036 and the whole **EPIC-015**. [tasks/EPIC-015](tasks/EPIC-015-birko-xaml-ui-framework/EPIC.md).
- **RibbonShellView** (`BAppShell`): a second shell chrome — a `Ribbon` (bound to `ShellViewModel.RibbonTabs`) over the content region + status bar, in place of the sidebar, on the same `ShellViewModel` (+ Ctrl+K palette). Screenshot-verified (ribbon tabs/groups + ListPage content).
- **Cross-fade page transitions**: both shells' content regions use a `TransitioningContentControl` (`CrossFade`) so navigation fades.
- **`ListBoxItem` restyle** (`Controls/Lists.axaml`): token hover/selected — improves the command palette, split-page list, kanban.
- Avalonia suite now **84**; CSS parity clean.
- **EPIC-015 done (8/8 stories):** single-source design tokens (byte-identical web CSS + Avalonia AXAML), the theme system (4 variants, runtime swap), Avalonia-free Core (i18n + MVVM base VMs), ~20 Tier-1 controls + building blocks (Form/Drawer/SplitPanel/Modal), 7 Tier-2 composites (tree-menu, command-palette, object/JSON + XML viewers, kanban, markdown-editor, chart), and the app shell with **both** sidebar + ribbon chrome, nav, page bases, palette, user/tenant, and transitions. WPF skin deferred (shares tokens/VMs; forks templates). Gallery lives in `Birko/Consumers`.

## 2026-07-06 — Birko.Xaml.Avalonia — BChart on LiveCharts2 → STORY-035 done (EPIC-015)
The final Tier-2 composite, closing **STORY-035** (all 7 done). [tasks/EPIC-015/STORY-035](tasks/EPIC-015-birko-xaml-ui-framework/STORY-035-tier2-composite-controls/STORY.md).
- **`BChart`** (`b-chart`) over **LiveCharts2** (`LiveChartsCore.SkiaSharpView.Avalonia` 2.0.5) — chosen over ScottPlot/OxyPlot for the best API + UX (modern/animated, MVVM-first) and Avalonia+WPF support (the epic's both-platforms constraint). Bind `Series` (Core `ChartSeries`) + `Kind` (Line/Column) + `Labels`; series colored from the token palette (`BColorPrimary`/`Info`/`Success`/`Warning`/`Danger` → `SKColor`). Verified: series/kind config tests + a screenshot (token-blue line + axes/labels; LiveCharts animates on load so the headless frame is mid-animation). **First external UI dependency beyond Avalonia**; SkiaSharp aligns cleanly with Avalonia 11.2.3 (spike-checked).
- Avalonia suite now **81**. **STORY-035 complete** — Tier-2: tree-menu, command-palette, object/JSON + XML viewers, kanban, markdown-editor, chart.

## 2026-07-06 — Birko.Xaml — Ribbon (BAppShell chrome) (EPIC-015 / STORY-036)
The last big chrome piece: **`Ribbon`** (`b-ribbon`) — a tab strip whose active tab shows labeled groups of icon+label command buttons, model-driven via Core `RibbonTab`/`RibbonGroup`/`RibbonItem` (`Tabs` + `SelectedIndex`, item `Run` on click), token-styled. Screenshot-verified (Home/View tabs; Clipboard + Records groups). Avalonia suite now **78**. Remaining STORY-036: a thin RibbonAppShell view (compose the ribbon over the content region), transition animations, ListBox restyle. [tasks/EPIC-015/STORY-036](tasks/EPIC-015-birko-xaml-ui-framework/STORY-036-shell-page-bases/STORY.md).

## 2026-07-06 — Birko.Xaml.Shell — command palette (Ctrl+K), user area, tenant switcher, FormModal (EPIC-015 / STORY-036)
Advanced the shell chrome + page shapes. [tasks/EPIC-015/STORY-036](tasks/EPIC-015-birko-xaml-ui-framework/STORY-036-shell-page-bases/STORY.md).
- **Command palette (Ctrl+K):** `ShellViewModel` builds `PaletteCommands` from the nav modules (go-to) + themes (switch), exposes `IsPaletteOpen`/`OpenPaletteCommand`; `ShellView` overlays the `CommandPalette` (from STORY-035) bound to those, opened by a `Ctrl+K` `KeyBinding`.
- **User area:** header avatar + `UserName` (hidden when empty) with a Flyout of `UserCommands` (`RunUserCommand`).
- **Tenant switcher:** header `ComboBox` bound to `Tenants`/`CurrentTenant`, shown when `HasMultipleTenants`.
- **`FormModal` page-shape** (`Birko.Xaml.Avalonia`): reusable create/edit dialog composing `Modal` + `Form` + Save/Cancel (`IsOpen`/`Title`/`Fields`/`Model`/`SaveCommand`/`CancelCommand`). The epic's `FormModal<T>`.
- Screenshot-verified (full header chrome: tenant + theme + user; and the FormModal dialog). **11 shell/formmodal tests**, Avalonia suite now **74**. Remaining STORY-036: the ribbon (`BAppShell`), transition animations, ListBox restyle.

## 2026-07-05 — Birko.Xaml.Avalonia — Tier-2 composites: tree-menu, command-palette, viewers, kanban, markdown-editor (EPIC-015 / STORY-035)
Building the Tier-2 composites. [tasks/EPIC-015/STORY-035](tasks/EPIC-015-birko-xaml-ui-framework/STORY-035-tier2-composite-controls/STORY.md) in-progress.
- **tree-menu** — `TreeView`/`TreeViewItem` token restyle (`Controls/Tree.axaml`): expander chevron (`PART_ExpandCollapseChevron`), token hover/selected, indented children, `:empty` hides the chevron on leaves. Screenshot-verified.
- **command-palette** — `CommandPalette` (`Controls/CommandPalette.cs` + Blocks.axaml template) over a platform-neutral `CommandItem` model (`Birko.Xaml.Core.Commands`): an overlay whose search box filters commands, keyboard-navigable (Up/Down/Enter/Esc), invokes `Run` + closes. Screenshot-verified (search box + grouped command list). This is also the **STORY-036** shell palette — the control exists; wiring Ctrl+K into `ShellView` is a small follow-up.
- **object-tree / JSON viewer** — `ObjectTree` (`Controls/ObjectTree.cs`): `Source` (object graph) or `Json` (string) → recursive tree over the restyled `TreeView`, type-colored monospaced values (string=success/number=primary/bool=warning/null=muted); walks `JsonNode`/dict/enumerable/POCO; invalid JSON → raw-string leaf. Screenshot-verified (nested `user{}`/`roles[]`/`null`). Covers `b-object-tree` + `b-json-viewer`.
- **xml-viewer** — `XmlViewer` (`Controls/XmlViewer.cs`): `Xml` string → `XDocument` → tree (elements `<tag>` primary, `@attributes`/`#text` success-colored leaves, monospace); invalid XML → raw leaf. Screenshot-verified. Covers `b-xml-viewer`.
- **kanban** — `Kanban` (`Controls/Kanban.cs`) over Core `KanbanColumn`/`KanbanCard`: horizontal token-styled columns (header + live count) each an `ItemsControl` bound to `column.Cards` (`ObservableCollection` → model moves update live), card surfaces; best-effort pointer drag-drop between columns. Screenshot-verified (3-column board). Covers `b-kanban` (recursive nesting deferred).
- **markdown-editor** — `MarkdownEditor` (split editor + live preview) + dependency-free `MarkdownRenderer` (static): a common Markdown subset (headings, `**bold**`/`*italic*`/`` `code` ``/`[text](url)`, lists, fenced code, hr) → token-styled controls. Screenshot-verified (H1 + bold + list + inline-code + code block). Covers `b-markdown-editor` (Markdig can replace the parser later).
- Avalonia suite now **65**. **Only `chart` remains** in STORY-035 — parked for a both-platforms plotting-lib decision (LiveCharts2/ScottPlot/OxyPlot).

## 2026-07-05 — Birko.Xaml.Gallery moved to the Consumers bucket
Relocated the gallery from `Birko/Framework/Birko.Xaml.Gallery` → `Birko/Consumers/Birko.Xaml.Gallery` so it mirrors `Birko.Web.Playground`: a **consumer/demo app**, not a framework project. It now references the `Birko.Xaml.*` assemblies via `ProjectReference` across the bucket (`../../Framework/...`), is **removed from `Birko.Framework.slnx` + `.code-workspace`**, and the framework test suite no longer depends on it (dropped the gallery-only `ParityScreenshotTests`; the gallery is run/validated standalone via `dotnet run`, like the Web playground's `verify.mjs`). Keeps its own git history. Framework Avalonia suite: 45 → 44.

## 2026-07-05 — Birko.Xaml.Avalonia — Tier-1 complete: Modal + DataGrid (EPIC-015 / STORY-034 done)
The last two Tier-1 controls, closing **STORY-034** (all ~20 restyled controls done). [tasks/EPIC-015/STORY-034](tasks/EPIC-015-birko-xaml-ui-framework/STORY-034-tier1-native-controls/STORY.md).
- **`Modal`** — centered dialog over a dimming backdrop (`IsOpen`/`Title`, backdrop-click closes, `--b-modal-width` card). A `Form` + Save/Cancel inside it = the **FormModal** pattern (screenshot-verified: titled card, required-asterisk form, buttons).
- **`DataGrid`** (`data-table`) — restyled with Birko tokens. DataGrid ships its theme as **Styles** (not a ResourceDictionary), so it's a separate `Controls/DataGridStyles.axaml` (includes DataGrid's Fluent theme + layers Birko tokens: header band via `--b-table-header-*`, cell text/font, grid lines) added to `Application.Styles`; needs the `Avalonia.Controls.DataGrid` package. Finstat's dark charcoal header band verified in the gallery.
- **3 new tests** (Modal toggle + FormModal screenshot, DataGrid header token) — Avalonia suite now **45**. CSS parity clean.
- **EPIC-015 is now 6/8 stories done** (029/030/031/032/033/034); only STORY-036 tail (ribbon/command-palette/user-tenant) and STORY-035 (Tier-2 composites) remain.

## 2026-07-05 — Birko.Xaml.Avalonia — Tier-1 tail: ToggleSwitch, BusySpinner, dropdown menu, Breadcrumb (EPIC-015 / STORY-034)
Filled in most of STORY-034's deferred controls — now **16 Tier-1 controls** done, only `DataGrid`/`modal` remain. [tasks/EPIC-015/STORY-034](tasks/EPIC-015-birko-xaml-ui-framework/STORY-034-tier1-native-controls/STORY.md).
- **`ToggleSwitch`** — token-driven; correctly declares the required `PART_MovingKnobs` + `PART_SwitchKnob` (the latter must be a `Panel`) so the XAML compiler's `AVLN2207`/`AVLN2205` contract passes; `:checked` moves the knob + turns the track primary.
- **`BusySpinner`** — custom `TemplatedControl` (renamed from `Spinner` to avoid colliding with Avalonia's built-in `Spinner`): a rotating token-colored `Arc`, default size from `BSpinnerSize`.
- **Dropdown menu** — `MenuFlyoutPresenter` (token surface) + `MenuItem` (hover/padding) ControlThemes.
- **`Breadcrumb`** — `ContentControl` that builds token-styled crumbs + separators in code, last crumb emphasized.
- **Gotcha captured** (project CLAUDE.md): a top-level ControlTheme animation `Style` (`<Style Selector="^ /template/ X"><Style.Animations>`) **silently breaks the whole theme's application** — scope animations inside the templated element's own `.Styles` (BusySpinner rotates via `Arc.Styles` + a `RotateTransform.Angle` animation).
- **6 headless tests** (ToggleSwitch checked-track, BusySpinner theme+arc, Breadcrumb crumbs+separators, dropdown themes registered, + the existing) — Avalonia suite now **42**; shown across themes in the gallery.

## 2026-07-04 — Birko.Xaml.Shell — app shell + navigation + page views (EPIC-015 / STORY-036, MVP)
The capstone: a working desktop CRUD app shape — sidebar chrome + navigation + generic list/detail/split page views binding the STORY-032 base VMs. New `Birko.Xaml.Shell` Avalonia assembly + platform-neutral nav/shell VMs in `Birko.Xaml.Core`. [tasks/EPIC-015/STORY-036](tasks/EPIC-015-birko-xaml-ui-framework/STORY-036-shell-page-bases/STORY.md) is **in-progress** (MVP done; ribbon/command-palette/user-tenant/FormModal deferred).
- **Navigation (Core, Avalonia-free — constraint #3):** `Navigation/ModuleDefinition` (the `buildModuleRoutes`/`ModuleManifest` analogue), `INavigationService`/`NavigationService` (module map + back-history + `Current` page VM), `Mvvm/ShellViewModel` (nav + `IThemeManager` + title; Navigate/Back/SetTheme commands), `Mvvm/SplitPageViewModel<T>`. Page-base VMs (`CrudViewModelBase`/`DetailPageViewModel`) gained a `Fields` schema so the generic views render a `Form`.
- **`Birko.Xaml.Shell` (Avalonia):** `ViewLocator` (`*ViewModel→*View` naming convention, else generic base-page mapping — Split before List since it derives from List); `Views/ShellView` (**sidebar** chrome = module nav + header title + theme switcher + content region via the ViewLocator + status bar — the `BSidebarAppShell` analogue); generic `ListPageView` (permission-gated New/Edit/Delete toolbar + search + inline create/edit `Form`), `DetailPageView` (`Form` + Save/Cancel), `SplitPageView` (master list + detail `Form` over `SplitPanel`).
- **Proven end-to-end** — a headless screenshot shows the full shell (sidebar Contacts/About nav, header title + theme switcher, a SplitPage list in the content region, status bar), all token-driven. **6 shell/nav tests** (navigation + Back, ViewLocator resolves Split/List/Detail VMs, shell renders the active page via the locator, nav swaps the page). Avalonia suite now **38**.
- **Page views use `x:CompileBindings="False"`** — their DataContext is a *generic* base VM with no fixed `x:DataType`, so bindings are reflection-based (documented in the project CLAUDE.md).
- **Deferred:** ribbon chrome (`BAppShell`), command palette, user-area / tenant switcher, a `FormModal` dialog (inline edit covers create/edit), content-transition animations, ListBox restyle/display templating.

## 2026-07-04 — Birko.Xaml — building blocks: Form + Drawer + SplitPanel (EPIC-015 / STORY-033)
The three controls the page layer composes, so the CRUD page bases stay declarative. Closes [tasks/EPIC-015/STORY-033](tasks/EPIC-015-birko-xaml-ui-framework/STORY-033-building-blocks-form-drawer-splitpanel/STORY.md). EPIC-015 now **5/8 stories done**.
- **`Form`** (keystone) — schema-driven (`Birko.Xaml.Avalonia/Controls/Form.cs`): binds `Fields` (`Birko.Xaml.Core.Forms.FormField[]`) + `Model` and generates labeled, two-way-bound inputs (Text/TextArea/Number→`TextBox`, Checkbox→`CheckBox`, Select→`ComboBox`) with a required asterisk, all token-styled. **Pairs directly with the STORY-032 VMs** — bind `Fields` + `CrudViewModelBase.EditingItem` / `DetailPageViewModel.Model` instead of hand-rolling XAML per screen. Verified in the gallery (renders + re-themes incl. finstat flat corners).
- **`Drawer`** — slide-in overlay (`IsOpen`/`Placement` left|right, backdrop-click closes), token width/bg.
- **`SplitPanel`** — master/detail over `GridSplitter` with responsive collapse (`:collapsed` below `CollapseWidth` hides the master; pixel master column so the splitter can drag).
- **Schema in Core, control in `.Avalonia`** — the `FormField` schema is platform-neutral (Avalonia-free, reusable by a future WPF `Form`); the control is the Avalonia view. Same split as i18n's logic/markup-extension.
- **8 headless tests** (Form generate/initial-bind/two-way/required-asterisk, Drawer visibility toggle, SplitPanel wide-vs-narrow collapse) + per-theme gallery screenshots. Avalonia suite now **31**.
- **Deferred:** DataAnnotations validation (only the required asterisk today), Drawer slide animation, `GridSplitter` drag precision.

## 2026-07-04 — Birko.Xaml.Core — i18n + base ViewModels (EPIC-015 / STORY-032)
`Birko.Xaml.Core` grew from theme abstractions into the thin, **Avalonia-free** platform core: i18n + base MVVM ViewModels + a CRUD port. Closes [tasks/EPIC-015/STORY-032](tasks/EPIC-015-birko-xaml-ui-framework/STORY-032-xaml-core-foundation/STORY.md). Its only dependency is CommunityToolkit.Mvvm (platform-neutral, works Avalonia + WPF).
- **i18n** — `Localization.II18n`/`I18n` (+ `I18n.Instance` singleton, mirrors Birko.Web's `i18n`): locale dictionaries, `this[key]` indexer (fallback → key), `SetLocale` (raises `INotifyPropertyChanged` + `LocaleChanged`), `Translate(key, args)` with `{placeholder}` interpolation.
- **`{l:Tr}` markup extension** lives in **`Birko.Xaml.Avalonia`** (`Markup/TrExtension.cs`), not Core — it must return an Avalonia `Binding`. It resolves through the Core singleton and re-resolves live on `SetLocale`. **Gotcha:** Avalonia doesn't observe `INotifyPropertyChanged` on **indexer** accessors, so `{l:Tr}` binds a small per-binding source's real `Value` property (refreshed on `LocaleChanged`) rather than `I18n[key]` directly.
- **Base ViewModels** (CommunityToolkit.Mvvm) — `BasePageViewModel` (busy/loaded/title + live re-localization), `CrudViewModelBase<T>` (observable items, selection, editing slot, **permission-gated** Create/Edit/Delete/Save/Refresh commands), `ListPageViewModel<T>` (client-side search), `DetailPageViewModel<T>` (load-by-id, gated Save).
- **`Data.ICrudDataSource<T>` port — deliberately NOT `IAsyncBulkStore<T>` directly.** Key architectural boundary: `Birko.Data.*` are shared **`.projitems`** (compiled into each importing assembly) while `Birko.Xaml.*` are **real assemblies** — importing the store projitems into Core would duplicate those types against a consumer's own aggregator (the hazard root CLAUDE.md warns about). So Core owns a thin CRUD port and the consumer supplies a ~10-line store→port adapter in the assembly that already has the Birko.Data types.
- **Avalonia-free enforced by test** — `Birko.Xaml.Core.Tests` asserts the Core assembly references no `Avalonia.*` (WPF-addendum constraint #1). **15 Core tests** (i18n, CRUD VM over a fake port, permission gating, i18n re-emit) + a headless `{l:Tr}` re-resolution test in `Birko.Xaml.Avalonia.Tests` (now 24). Registered in `.slnx` (`/Tests/`) + `.code-workspace`.

## 2026-07-04 — Birko.Xaml.Avalonia — Tier-1 restyled controls, first batch (EPIC-015 / STORY-034)
Started the Tier-1 control sweep (the ~80%-of-visual-value layer). `Birko.Xaml.Avalonia/Controls/` is now split into category `ResourceDictionary`s — **Buttons / Inputs / Toggles / Surfaces / Indicators / Overlays** — merged by `Controls.axaml` (which `BirkoTheme.axaml` includes). [tasks/EPIC-015/STORY-034](tasks/EPIC-015-birko-xaml-ui-framework/STORY-034-tier1-native-controls/STORY.md) is **in-progress**.
- **11 controls done, all token-driven** (`{DynamicResource B*}`, no hard-coded colors/sizes): `Button`, `TextBox` (single + multiline via `AcceptsReturn`), `Card`, `Badge`, `Tag`, `TabControl`/`TabItem`, `CheckBox`, `RadioButton`, `ProgressBar`, `ToolTip`, `ComboBox` (+ `ComboBoxItem`). Named `ContentControl` themes (`BCard`/`BBadge`/`BTag`) apply via `Theme="{StaticResource …}"`.
- **Verified visually** — the gallery now shows the full set; headless Skia screenshots per theme confirm parity, incl. finstat's flat/square corners + brand green flowing through checkbox/radio/progress/tab accents. **23 headless tests** (theme system + control themes + Tier-1 + screenshot).
- **Deferred** (need more than a mechanical restyle; some are STORY-035 candidates): `ToggleSwitch` (strict `PART_MovingKnobs`/`PART_SwitchKnob` + knob animation), a `Spinner` (custom control + rotation), `Menu`/dropdown, `table`, `data-table` (Avalonia `DataGrid` = separate pkg + row-height parity), `modal` overlay, `breadcrumb`.
- **Control-theme gotchas** captured in the project CLAUDE.md: `double`→`CornerRadius` needs a `CornerRadius` resource (generator now emits radius tokens as such); several native controls enforce required template parts (`AVLN2205`) — supply them or defer; `TextPresenter.Foreground` isn't bindable (let it inherit).

## 2026-07-04 — Birko.Xaml — Tier-0 gallery + first restyled controls, gate = GO (EPIC-015 / STORY-031)
Third slice of **EPIC-015** and the **go/no-go gate**: the first restyled `b-*`-equivalent controls + a runnable Avalonia gallery proving the token pipeline gives visual parity with Birko.Web and live theme swap. Closes [tasks/EPIC-015/STORY-031](tasks/EPIC-015-birko-xaml-ui-framework/STORY-031-tier0-gallery-validation/STORY.md). **Verdict: GO** — Tier-1 (STORY-034) proceeds.
- **First ControlThemes** — `Birko.Xaml.Avalonia/Controls/Controls.axaml`: token-driven `Button` + `TextBox` (implicit `{x:Type}` themes) and reusable `Card`/`Badge` (`ControlTheme` on `ContentControl`, applied via `Theme=`). Every visual value is `{DynamicResource B*}`, so controls re-theme live. New one-include `BirkoTheme.axaml` merges `Tokens.axaml` + `Controls.axaml`.
- **`Birko.Xaml.Gallery`** — a runnable Avalonia `net8.0` desktop app (Avalonia.Desktop + Fluent base), dev/validation artifact (not a shipped consumer). Theme switcher + Button/TextBox/Card/Badge; `dotnet run --project Birko.Xaml.Gallery`.
- **Gate proven visually** — a headless **Skia screenshot** test renders the gallery to a PNG per theme; all four (light/dark/neon/finstat) render distinctly and correctly, incl. **finstat's flat/square corners** (radius token → 0) vs rounded elsewhere. Same token source as the web → same look.
- **Generator fix surfaced by the gate** — a `double` radius token can't bind to a `CornerRadius` property (runtime `InvalidCastException` that aborts the ControlTheme). STORY-029's `AxamlEmitter` now emits `--b-radius*` as **`CornerRadius`** resources (not `x:Double`); other lengths stay `x:Double`. **CSS byte-parity unaffected** (`verify` clean).
- **15 headless tests** (`Birko.Xaml.Avalonia.Tests`, now with Fluent + Skia) — theme-system resolution (9) + control themes (Button/TextBox/Card re-theme per variant, `CornerRadius`-from-token, Badge findable) + the parity-screenshot capture. DesignTokens suite stays 30, `verify` clean.

## 2026-07-04 — Birko.Xaml.Core + Birko.Xaml.Avalonia — Avalonia theme system (EPIC-015 / STORY-030)
Second slice of **EPIC-015**: the Avalonia skin's theme system — drop in the generated token dictionaries and switch theme at runtime (light/dark/neon/finstat), matching Birko.Web. Closes [tasks/EPIC-015/STORY-030](tasks/EPIC-015-birko-xaml-ui-framework/STORY-030-avalonia-theme-system/STORY.md). **First real Avalonia/.NET assemblies in the `Birko\Framework` bucket** (the EPIC convention break — compiled AXAML through `.shproj`/`.projitems` is fragile), both `net8.0` (Avalonia 11.2.3; TFM differs from the framework's net10.0 because that's what Avalonia 11.2.3 targets). Registered in `Birko.Framework.slnx` (`/Xaml/`) + `.code-workspace`; referenced via `ProjectReference`, **not** the `Birko.Framework.csproj` aggregator (that's `.projitems`-only).
- **`Birko.Xaml.Core`** (Avalonia-free, WPF-addendum constraint #1 — enforced: no `using Avalonia.*`) — `ThemeInfo` (mirrors the web shell's `ThemeOption`), `BirkoThemes` (the 4 built-ins, in sync with the `Birko.DesignTokens` sheets + web `BUILTIN_THEMES`), `IThemeManager` (`Available`/`Current`/`ThemeChanged`/`SetTheme` — the XAML analogue of the web `setTheme`). STORY-032 grows this with i18n + base VMs.
- **`Birko.Xaml.Avalonia`** — `BirkoThemeVariants` (the `ThemeVariant` instances: Light/Dark reuse built-ins; **Neon** inherits Dark, **Finstat** inherits Light), `AvaloniaThemeManager : IThemeManager` (over `RequestedThemeVariant`), and the generated `Themes/Tokens.axaml`.
- **ThemeDictionaries, not swapped MergedDictionaries.** A single `Tokens.axaml` `ResourceDictionary` whose `ThemeDictionaries` hold one entry per theme; setting `RequestedThemeVariant` re-resolves every `{DynamicResource}` **live, no restart**. A spike de-risked this first: Avalonia resolves **custom** variants (neon/finstat), not just Light/Dark, via `TryGetResource(key, variant)` with `InheritVariant` fallback.
- **Key identity is the trick** — `ThemeDictionaries` keys are `{x:Static themes:BirkoThemeVariants.X}`, the *same* static `ThemeVariant` instances assigned to `RequestedThemeVariant`, so lookup matches by identity (not fragile string keys). **Brushes at root, colors per-variant**: one `SolidColorBrush` per color token with `Color="{DynamicResource BColorX}"`, so a swap updates the brush without duplicating it 4×; colors live per-theme in `ThemeDictionaries`. Controls bind `{DynamicResource BColorXBrush}` / `{DynamicResource BRadius}`.
- **Generator restructured** — STORY-029's `AxamlEmitter` now emits this one ThemeDictionaries file instead of 4 separate dicts. **CSS byte-parity is unaffected** (still `verify`-clean; the `git diff` on the web CSS stays empty).
- **8 headless tests** (`Birko.Xaml.Avalonia.Tests`, Avalonia.Headless.XUnit, net8.0) — the real `Tokens.axaml` loads, all 4 variants resolve `BColorPrimary`, `var()`-ref tokens (`BBorderFocus`) follow the active primary, lengths bake (finstat radius → 0), a live `RequestedThemeVariant` swap re-colors a `DynamicResource` brush, and `AvaloniaThemeManager` lists/switches themes. (DesignTokens suite adjusted to 30 for the single-file AXAML.)
- **Deferred:** composite/motion tokens (shadows/focus-rings/transitions/easings/gradients) map when a control needs them (STORY-034); gallery app is STORY-031; scoped `inverse` theme not emitted to AXAML.

## 2026-07-04 — Birko.DesignTokens — single-source token generator (EPIC-015 / STORY-029)
First slice of **EPIC-015 (Birko.Xaml)**: `tokens.json` becomes the single source of truth for all design tokens, and a C# tool generates every target so the web (CSS) and desktop (Avalonia XAML) design systems can never drift. Closes [tasks/EPIC-015/STORY-029](tasks/EPIC-015-birko-xaml-ui-framework/STORY-029-design-tokens-generator/STORY.md).
- **`Birko.DesignTokens` — the first real, buildable `.csproj` in the `Birko\Framework` bucket** (every other sibling is `.shproj`/`.projitems`). Justified: it is a build-time code-generation **tool**, not a runtime shared library, so it needs build output and is **not** imported into the `Birko.Framework.csproj` aggregator. Foreshadows EPIC-015's note that `Birko.Xaml.*` also ship as real assemblies. Registered in `Birko.Framework.slnx` (new `/Xaml/` folder) + `.code-workspace`.
- **CLI** — `generate` (everyday: emit CSS + AXAML from `tokens.json`), `verify` (diff on-disk CSS vs `tokens.json`, exit 1 on drift — for CI/pre-commit), `extract` (bootstrap/re-derive `tokens.json` from the current CSS). Path resolution follows the `$(BirkoSrc)`/`BIRKO_SRC` convention (walks up to the folder holding both `Framework` and `Web`).
- **Byte-identical CSS parity (the acceptance gate).** Each CSS file is modeled as `prologue` + `{selector} {` + ordered body lines + `}` + `epilogue`; every body line is one node — a structured `var` (name/value/**verbatim trailing comment**, which preserves the hand-authored comment-column alignment) or verbatim `raw` (comments, blanks). **Values** are single-sourced on the `var`; comments/layout round-trip verbatim. Regenerating all 5 files (`tokens.css` + `themes/{dark,neon,finstat,inverse}.css`) is byte-identical to the committed files, proven against `git show HEAD:`.
- **Line endings** — the repo is canonically **LF** (`core.autocrlf=true` had presented `tokens.css` as CRLF in the working tree; the 4 theme files were LF). The model is kept LF and comparisons normalize CRLF→LF, so generation is **machine-independent** (no dependency on a checkout's autocrlf state) — this fixed a latent "works on my machine" trap.
- **Avalonia AXAML** (first cut) — `Tokens.axaml` + `Theme.{Light,Dark,Neon,Finstat}.axaml` into `Birko.Xaml.Avalonia/Themes/`. `--b-color-x` → `Color` + paired `SolidColorBrush` (`BColorX`/`BColorXBrush`; `rgba` alpha folds into `#AARRGGBB`); rem/px → `x:Double` (rem baked at 16px; unitless `0` → 0); `--b-font*` → `FontFamily`. `var()` refs resolve **per theme**, so each dictionary is full & self-contained (a theme swap is a whole-dictionary replace) — all 4 expose an identical 181-key set. Composite/motion tokens (shadows, focus rings, transitions, easings, durations, gradients), `ThemeVariant`/`DynamicResource` wiring, and the scoped `inverse` partial in AXAML are **deferred to STORY-030**.
- **`tokens.json` is deliberately language-neutral** (no C#-isms, no logic in the data) so a later swap of the C# generator for a TypeScript one is a one-file emitter rewrite against the golden CSS — the schema and extracted data carry over untouched.
- **32 xUnit + FluentAssertions tests** (`Birko.DesignTokens.Tests`) — per-sheet CSS round-trip parity, extractor round-trip on the live files, single-source/uniqueness checks, AXAML well-formedness, per-theme resolution (primary + `var()`-ref follow-through), cross-theme key-set parity (swap safety), and color/length/key-name conversion unit tests.

## 2026-06-25 — Birko.Security.AspNetCore — server-side role resolution + claim-delimiter fix
`ICurrentUser.Roles` was always empty: the JWT deliberately omits roles and — unlike permissions — they were never resolved server-side, so anything relying on `Roles` silently got nothing. Roles now have the same per-request server-side resolution path permissions already had.
- **`IUserPermissionResolver.GetRolesAsync(userId, tenantId, ct)`** — new method, added as a **default interface method** returning an empty set so existing implementers don't break; role-store-backed resolvers override it (parallel to the existing `GetAsync` for permissions)
- **`PermissionResolutionMiddleware`** — new `RolesItemsKey` const; after stashing resolved permissions it now also calls `GetRolesAsync` and stashes the result in `HttpContext.Items[RolesItemsKey]`
- **`ResolvedPermissionsCurrentUser.Roles`** — reads the middleware-resolved set first, falls back to the role claim when the slot is absent (splitting on both `,` and `;`)
- **`ClaimsCurrentUser` delimiter fix** — the producer (`TokenServiceAdapter`) joins multi-values with `;` but both readers split on `,` only; `Roles` and `Permissions` now split on **both** `[',', ';']` (fixes e.g. a superadmin `*` packed into a joined permission claim not surfacing for `Contains("*")`)
- **Consumer responsibility** — the framework default returns empty; a consuming app must override `GetRolesAsync` (in Symbio: `UserPermissionResolver.GetRolesAsync` → cached → `RoleService.GetUserRoleNamesAsync`, batched, tenant-scoped, mirroring the permissions path)
- **13 new xUnit + FluentAssertions tests** (`Birko.Security.AspNetCore.Tests`) — `ResolvedPermissionsCurrentUserTests` (resolved set wins, claim fallback, both-delimiter split, empties), `PermissionResolutionMiddlewareTests` (populates both slots, null tenant, no-ops when unauthenticated / bad userId, default `GetRolesAsync` is empty), plus delimiter `[Theory]` cases in `ClaimsCurrentUserTests`. Suite green (77 total)

## 2026-06-24 — Birko.Web.Testing — shared E2E / browser-automation package
New fourth source-only sibling in the **`Birko\Web` bucket** (`Birko.Web.Testing`, pkg `birko-web-testing`) — a reusable browser-automation toolkit for every Birko.Web consumer (Symbio, Playground, Presenter, WorkoutTracker, …). Consumed by TypeScript `paths` via the Node test runner (not esbuild — no `build.js` alias). Not in any `.slnx`/`.sln`.
- **Two lanes + a driver-agnostic core.** **Playwright** = the test suite (`birkoPlaywrightPreset`, `runSmoke` route-sweep, fixtures `page/form/dataTable/nav/consoleGuard`, page objects). **Puppeteer** = utility scripts only (`launchSession` for PDF/screenshot/perf/scrape). **Core** (`/core`, no driver imports) = `RouteEntry` manifests, `b-*` selectors, `loginViaApi` (+ the `localStorage` auth snapshot `birko-web-shell`'s `createAuthStore` writes), one `attachCollector` (console + 4xx/5xx) that works for both drivers.
- **Selectors grounded in real source** — `b-data-table` `.row-action-trigger[data-id]` → top-level `b-dropdown-menu .item[data-id]`; `b-form` `[data-field]`/`[name]`; `b-sidebar`/`b-ribbon`; `BaseCrudPage` `#btn-create`/`#modal`/`#form`/`#btn-save`. Playwright CSS pierces shadow automatically; Puppeteer uses `>>>`.
- **One-Chromium policy** — Playwright owns the browser (`npx playwright install chromium`); Puppeteer reuses it (`PUPPETEER_SKIP_DOWNLOAD=1` + `PUPPETEER_EXECUTABLE_PATH`). Never install the drivers globally; pin per consumer. Note: a path-mapped source package resolves its deps relative to its own dir, so each consumer's tsconfig must also map `@playwright/test`/`puppeteer` → its `node_modules`.
- **Reference implementation** wired into Symbio at `Birko.Consumers/Symbio/tests/ui-e2e/` (config + `auth.setup` + Communication `smoke.spec` + Inquiries `communication.spec`); **typechecks clean**. Live run is the consumer's step (needs the dev stack up + a seeded Building). Docs: package `README.md`/`ENV.md`, `docs/web.md`, and the [[birko-new-project]] skill.

## 2026-06-19 — Birko.Web.Components — b-accordion + shared `coerceCssLength`
Closed [tasks/EPIC-001/STORY-028](tasks/EPIC-001-web-components-ui-polish/STORY-028-display-disclosure-components/STORY.md) (display & disclosure components), both surfaced while building the Birko.Web Playground. Note: these live in the **`Birko\Web` bucket** (frontend libs), not the .NET `Birko\Framework`.
- **`b-accordion`** (new `layout` component, 58th overall) — collapsible disclosure group. `setItems([{id,header,open?,disabled?}])` config mirroring `b-tabs.setTabs`; bodies via `slot="{id}"`. Single-open by default, `multiple` attribute allows several open. Native `<button>` headers (`aria-expanded`/`aria-controls`), `<section role="region">` panels toggled via `hidden`, keyboard Enter/Space (toggle) + Up/Down/Home/End (`rovingIndex` from `dom-utils`) across enabled headers. `size` (sm|md|lg) = vertical-footprint via `--b-control-min-height-*`. Emits `toggle` `{id,open}`. No new i18n keys (headers are consumer-supplied; state is conveyed via ARIA). Added to the playground gallery.
- **`coerceCssLength(value, unit='px')`** (new in `Birko.Web.Core`, `src/css/length.ts`) + **`BaseComponent.lengthAttr(name, fallback, unit)`** — coerce a bare-number length (`"160"` → `"160px"`) so it can't produce the invalid `style="height:160"` the browser drops (which let a `height:100%`/`width:100%` child stretch unboundedly). Routed `b-chart` (`height`), `b-skeleton` (`width`/`height`/`size`), and the four data viewers (`b-json-viewer`/`b-object-tree`/`b-pre`/`b-xml-viewer`, `max-height`) through it — six components, same latent bug.

## 2026-06-15 — Birko.Data.InMemory — in-memory store backend + test-fake consolidation
New `Birko.Data.InMemory` sibling: the simplest possible store, backing the entity set with a thread-safe `ConcurrentDictionary<Guid, T>` and no persistence. Built to be the canonical **test double** (one correct implementation instead of a hand-rolled fake per test project), a **reference implementation** (the `AbstractJsonStore` dictionary model minus the file I/O), and a zero-setup **prototyping/demo** backend.
- **Stores** — `InMemoryStore<T>` (sync, `AbstractBulkStore<T>`) and `AsyncInMemoryStore<T>` (async, `AbstractAsyncBulkStore<T>`), plus `AbstractInMemoryStore<T>` / `AbstractAsyncInMemoryStore<T>` base classes. Both implement the **full** `IBulkStore<T>` / `IAsyncBulkStore<T>` contract — filter-based `Update(filter, …)` / `Delete(filter)`, `ReadFirst`/`ReadFirstAsync`, ordering + paging, and `IAggregatableStore<T>` / `IAsyncAggregatableStore<T>` via `AggregateHelper.LinqAggregate(Async)` — which the ad-hoc fakes never fully did
- **Conventions honored** — overrides only the `*Core` methods (lazy-init preserved); `Read(Guid)` / `ReadAsync(Guid)` do O(1) dictionary lookups; `Delete(filter)` / `DeleteAsync(filter)` do a single pass; bulk reads return a `List<T>` snapshot so the concurrent dictionary can mutate mid-enumeration. No settings class — `ISettingsStore<Settings>` is implemented as a no-op purely for drop-in compatibility (an in-memory store can stand in for a JSON/SQL store in the same wiring)
- **40 xUnit + FluentAssertions tests** (`Birko.Data.InMemory.Tests`) covering both stores: CRUD, bulk ops, filter update/delete, ordering/paging, lazy-init, aggregation, `Save`, `Destroy`, settings surface, and async cancellation
- **Fake consolidation** — migrated five hand-rolled in-memory stores across four sibling test repos to subclass the new store, deleting ~250 lines of duplicated boilerplate: `Birko.Security.OAuth.Server.Tests` (78→14 lines; the 5 marker stores are now bare subclasses), `Birko.Validation.Tests` (~90-line nested fake → 4 lines), `Birko.Localization.Data.Tests` (kept only `Seed`), and `Birko.Data.Sync.Tests` (`TestBulkStore` + `TestSyncKnowledgeItemStore`, keeping only the sync-bookkeeping members). All four suites stay green (43 / 122 / 26 / 21)
- Registered in `Birko.Framework.slnx` (Data.NoSQL + Tests folders), `Birko.Framework.code-workspace`, and the `Birko.Framework.csproj` aggregator

## 2026-06-15 — Birko.Data.Stores — async stores observe cancellation consistently
`AbstractAsyncStore.EnsureInitializedAsync` now calls `ct.ThrowIfCancellationRequested()` at the top. **Behavior change:** every public async CRUD method funnels through this gate, so an already-cancelled `CancellationToken` now surfaces as `OperationCanceledException` even on an already-initialized store (previously the gate returned at `if (_initialized) return;` without checking the token, so cancellation was only observed on the very first, uninitialized call). This is the idiomatic .NET contract and affects only callers that pass a cancelled token — which is what they asked for.
- **Regression fix** — `AsyncStoreTests.Operations_ShouldRespectCancellationToken` was red on `main`: written 2026-03-11 when the in-memory test store overrode the *public* async methods as token-ignoring no-ops, it went stale when the **lazy-init refactor (2026-04-10)** routed all public methods through `EnsureInitializedAsync(ct)`. Its assertion (`NotThrowAsync<TaskCanceledException>`) contradicted the test's own name; corrected to `ThrowAsync<OperationCanceledException>` (the base type also catches `TaskCanceledException`)

## 2026-06-15 — Birko.Data.Stores — `ReadFirst` / `ReadFirstAsync` on bulk stores
Closed a sharp overload-shadowing footgun in the store hierarchy. On any bulk store, `store.Read(filter)` does **not** return a single entity (the `IStore<T>` contract) — it returns the whole `IEnumerable<T>` result set. This is C# member lookup, not overload preference: a bulk store declares the collection overload `Read(filter, orderBy, limit, offset)`, and the compiler only keeps methods named `Read` declared in the *most-derived* type that declares any such method, so the inherited single-result `Read(filter)` is removed from the candidate set entirely (reachable before only by casting to `IReadStore<T>`). The same applied to `ReadAsync`.
- **New single-result accessor** — `IBulkReadStore<T>.ReadFirst(filter)` → `T?` and `IAsyncBulkReadStore<T>.ReadFirstAsync(filter, ct)` → `Task<T?>`, added as **default interface methods** (delegate to the cast-through-`IReadStore<T>` path) so every existing `IBulkStore<T>` implementer — including the decorator/wrapper chain — gets them with zero breakage, and interface-typed callers can use them directly
- **Concrete `public virtual` overrides** on the four bulk base classes — `AbstractBulkStore<T>`, `DataBaseBulkStore<DB,T>`, `AbstractAsyncBulkStore<T>`, `AsyncDataBaseBulkStore<DB,T>` — so concrete-typed callers (the common case) call `ReadFirst` directly; each delegates to `base.Read(filter)` / `base.ReadAsync(filter, ct)`, which routes through the single-row `ReadCore` (the SQL stores already issue `LIMIT 1`). Override on a platform store for native single-row optimization
- **Non-breaking** — `Read(filter)` keeps returning the collection; nothing currently working changes. `ReadFirst` is purely additive. Named `ReadFirst` (not `ReadSingle`) because it is `FirstOrDefault` semantics, not uniqueness-enforcing
- **7 xUnit + FluentAssertions tests** (`Birko.Data.Tests/Stores/BulkStoreReadFirstTests.cs`) — single-match, no-match→null, the collection-vs-single distinction, and reachability through `IBulkStore<T>` / `IAsyncBulkStore<T>` for both sync and async
- Documented in the **Conventions** section above

## 2026-06-15 — Birko.Web.Components — accessibility (ARIA / screen-reader) pass
Closed the accessibility gaps found in an audit of the `b-*` catalogue. Adds shared infrastructure so new components stay accessible by default, plus a new [`Birko.Web.Components/ACCESSIBILITY.md`](../Birko.Web.Components/ACCESSIBILITY.md) reference. Form-association via `ElementInternals` is deliberately out of scope (it's a feature, not an SR gap) — tracked in [tasks/EPIC-001/STORY-023/TASK-035](tasks/EPIC-001-web-components-ui-polish/STORY-023-form-associated-elements/TASK-035-element-internals-form-association.md).
- **`BaseComponent.uid`** (`Birko.Web.Core`) — stable per-instance id prefix, allocated lazily, surviving re-renders. Used to mint deterministic element ids for `aria-*` IDREFs (`${this.uid}-error`, `${this.uid}-body`, `${this.uid}-tip`)
- **Form-validation ARIA** — new `fieldAria()` + `renderError()` helpers in `inputs/label-hint.ts`. `fieldAria({uid, error, required})` spreads `aria-invalid`/`aria-describedby`/`aria-required`; `renderError(uid, error)` renders the error as a `role="alert"` live region linked via `aria-describedby`. Wired across **12 inputs** (input, textarea, select native+combo, color-picker, multi-select, tag-input, markdown-editor, date-picker native+custom, datetime-picker, time, range, date-range-picker). `aria-required` only on non-native (div-based) controls; native `required` covers the rest
- **`aria-expanded`** on every expand/collapse toggle — `b-tree-menu` (on the `treeitem`; invalid empty value for leaves fixed; `aria-busy` while lazy-loading), `b-kanban` card toggles (+ `aria-controls`), `b-object-tree` (now full `tree`/`treeitem`/`group` semantics), `b-form` collapsible group legends (now `role="button"` + Enter/Space activation + `aria-controls`), `b-sidebar` toggle
- **Shared `.sr-only` utility** — new `srOnlySheet` (`@sheet srOnly` in `shared-styles.css`, + `.sr-only-focusable`), replacing the copy previously private to `b-kanban`
- **`b-breadcrumb`** rebuilt as `nav > ol > li` with `aria-label="Breadcrumb"` and `aria-current="page"` on the last crumb
- **`b-command-palette`** — polite `role="status"` live region announcing result counts / "Searching…" / "No results", plus `role="combobox"` + `aria-controls` + `aria-activedescendant` on the search input
- **`b-ribbon`** — fixed broken `aria-controls` (every tab pointed at a per-tab panel id, but only the active tab's panel exists; now a single static `ribbon-panel` id, omitted in `tabs-only` mode); mobile tab headers gained `aria-expanded` + `aria-controls` (kept in sync on toggle); mobile `<dialog>` gained `aria-labelledby`
- **Reference/keyboard sweep across nav + data + inputs** —
  - `b-tabs`: tab buttons had no `id`, so each panel's `aria-labelledby` pointed at nothing → added `id="tab-${id}"` (panels reference it)
  - `b-button`: now forwards `aria-label` / `aria-current` / `title` from the host to the inner shadow `<button>` (they were previously dropped, so e.g. `b-pagination`'s "Previous page" never reached AT)
  - `b-dropdown-menu`: trigger exposes `aria-haspopup`/`aria-expanded` immediately, not only after first open
  - `b-multi-select`: invalid `role="listbox"` wrapping checkboxes → `role="group"` + `aria-label` + `aria-controls` (it's a checkbox group, not a listbox)
  - `b-data-table`: select-all + per-row selection checkboxes gained `aria-label` (new `labels.selectAll` / `labels.selectRow`)
  - `b-table`: sortable headers were click-only → inner `<button>` makes them keyboard-operable while the `<th>` keeps `columnheader`/`aria-sort`
  - `b-segmented` + `b-option-group`: were `tab`/`tablist` (or plain buttons) with no panels and no keyboard nav → now proper `radiogroup`/`radio` with roving tabindex + arrow/Home/End selection
  - `b-inline-edit`: click-only display → `role="button"` + `tabindex` + Enter/Space
  - `b-file-upload`: the native input is `display:none`, and the dropzone wasn't focusable → keyboard users couldn't upload; dropzone is now `role="button"` + `tabindex` + Enter/Space (+ focus ring), new `bwc.fileUpload.browse` label
- **De-duplication** — extracted a shared `src/dom-utils.ts` (`escapeHtml` / `escapeAttr`, `isActivationKey`, `rovingIndex`). Replaced **16** per-component copies of `_escapeHtml`/`_escapeAttr`/`_esc` (and `cell-renderers.ts`'s standalone pair — whose `escapeAttr` didn't escape `&`, now fixed) and the duplicated radio-group / Enter-Space keyboard logic with imports from it
- **Smaller fixes** — `b-button` loading sets `aria-busy` (+ `aria-hidden` spinner); `b-confirm-dialog` gets `aria-labelledby`/`aria-describedby`; `b-tooltip` links trigger→tip via `aria-describedby` (cross-shadow slotted-content naming documented as a known limitation)
- New i18n keys `bwc.palette.resultsCount`, `bwc.breadcrumb.label`. Documented in `Birko.Web.Components` CLAUDE.md (new **Accessibility** section + checklist item) and README

## 2026-06-11 — Birko.Communication.gRPC + .Server — gRPC client/server primitives
New gRPC support split into a client and a server shared project, mirroring the existing `Birko.Communication.REST` / `.REST.Server` split (client over `Grpc.Net.Client`, server over `Grpc.AspNetCore` which pulls in ASP.NET). Closes [tasks/EPIC-009/STORY-019/TASK-026](tasks/EPIC-009-communication-protocols/STORY-019-grpc/TASK-026-grpc-client-server.md). Code generation (`.proto` → C#) is intentionally out of scope — consumers bring generated clients/services via `Grpc.Tools`; these primitives configure and wrap them.
- **`Birko.Communication.gRPC`** (client) — `GrpcSettings : RemoteSettings` (`Endpoint` aliases `Location`; `MaxReceiveMessageSizeBytes` / `MaxSendMessageSizeBytes` / `DeadlineSeconds` / `Credentials` / `ExtraMetadata`); `GrpcChannelPool` (endpoint-keyed `ConcurrentDictionary` cache of reusable `GrpcChannel`s — `GetChannel` / `Remove` / `Clear`, mirrors `RestClient.GetClient`); `GrpcClientFactory.CreateClient<TClient>()` (constructs any generated `ClientBase` over a pooled channel or explicit `CallInvoker`, applying interceptors via `CallInvoker.Intercept`); `GrpcAuthenticationInterceptor` (client `Interceptor` overriding all five call kinds, token-provider or raw `Action<Metadata>` constructor); `GrpcException` (wraps `RpcException` → `StatusCode` / `Detail` / `Trailers`, mirrors `GraphQLException`)
- **`Birko.Communication.gRPC.Server`** (server) — `GrpcServerSettings : Settings` (`EnableDetailedErrors`, message-size caps, `EnableReflection`); `AddBirkoGrpc(this IServiceCollection, GrpcServerSettings?)` DI extension (in `Microsoft.Extensions.DependencyInjection` namespace, returns `IGrpcServerBuilder`); `GrpcServerAuthenticationInterceptor` (server `Interceptor` overriding all four handler kinds, validates request metadata via a `Func<Metadata, ServerCallContext, Task<bool>>`, throws `RpcException(Unauthenticated)` on failure)
- **Settings** descend the `RemoteSettings` (client) / `Settings` (server) chain; shared projects carry no `PackageReference` — the importing csproj supplies `Grpc.Net.Client` / `Grpc.AspNetCore` (+ the `Microsoft.AspNetCore.App` framework reference for the server)
- **32 xUnit + FluentAssertions tests** (24 client + 8 server) — settings, channel-pool caching/eviction/guards, interceptor metadata injection (captured-continuation), client factory over an in-memory `CallInvoker`, exception mapping; server settings, `AddBirkoGrpc` DI registration, and the auth interceptor against an in-memory `ServerCallContext`
- Registered in `Birko.Framework.slnx` (Communication + Tests folders) and `Birko.Framework.code-workspace`

## 2026-06-10 — Birko.Web.Components — `b-table` uniform row height
Fixed the long-standing visual mismatch where the `b-table` / `b-data-table` header band rendered shorter than body rows. The header CSS was already balanced for *plain text* (the `th` carries `+1px` extra vertical padding and a `2px` bottom border to offset its smaller `--b-text-xs` font vs the body's `--b-text-sm`), so text-only tables matched within a pixel. The visible gap appeared in `b-data-table`, whose body cells carry **controls** — the `size="sm"` row-action `⋮` `b-button` (`b-data-table.ts:451`), selection checkboxes, badges — whose intrinsic height (~24–26px) inflates `tbody` rows past the ~33px header. `vertical-align: middle` centred them but couldn't shrink the band.
- **`--b-table-row-height` token** — both `th` and `td` now set `height: var(--b-table-row-height, var(--b-control-min-height, 2.375rem))` (38px) with `box-sizing: border-box`, `vertical-align: middle`, and vertical padding dropped to `0` (horizontal `--b-space-md` kept). The fixed band height now drives the row; single-line content — plain text, `size="sm"` *and* default `b-button`s (both fit under 38px), badges, checkboxes, inline-edit inputs — all centre within one consistent height, header included. Wrapped/multi-line content still grows the row gracefully; the empty-state `.empty` cell keeps its `--b-space-3xl` padding (class beats element selector)
- **Consumer override** — set `--b-table-row-height` on the `<b-table>`/`<b-data-table>` instance (or globally on `:root`) for denser/airier grids, e.g. `--b-table-row-height: var(--b-control-min-height-sm)` (28px). Works on `b-data-table` too since it wraps a `b-table`
- Documented in `Birko.Web.Components` README design-token table

## 2026-06-10 — Birko.Web — `finstat` theme + 5 new themeable tokens
Added a fourth built-in theme that reproduces the Finstat web app's brand look, so a Birko.Web app (e.g. a migrated Finstat admin SPA) can match the existing product visually. Adding it surfaced 5 design facets that had no `--b-*` slot — those are now first-class tokens (so any theme can set them), wired into the components with defaults that preserve the existing look.
- **`[data-theme="finstat"]` token block** in `Birko.Web.Components/css/tokens.css` — extracted from Finstat's LESS design tokens (`finstat/DevContent/less/`, catalogued in `finstat/LESS_TOKENS.md`). Brand green primary (`#25ba7a`, hovers *lighter* `#33db93` per Finstat's actual button behavior), warm-grey surfaces (page `#f3f2f0`, white cards), charcoal text (`#434040`), `"Roboto", Arial` font, blue focus glow (`#54b9e8`), and Finstat's signature **flat/square corners** (`--b-radius*: 0`, since its `@border-radius-*` are all `0px`). Shadows mirror Finstat's `@box-shadow` / `@box-shadow-dropdown`. `-light` tints and darker danger-hover are derived (Finstat had no token for those slots)
- **5 new tokens** (defined in `:root` with current-look defaults, overridden in `finstat`):
  - **`--b-font-heading`** — title/heading family (default `var(--b-font)`); wired into the card header (`b-card`) and the shared `overlayHeader` sheet (`b-modal` / `b-drawer` titles). Finstat → `"Roboto Condensed"`
  - **`--b-table-header-bg` / `--b-table-header-text` / `--b-table-header-text-hover`** — `b-table` `<th>` band (defaults `var(--b-bg)` / `var(--b-text-secondary)` / `var(--b-text)`). Finstat → dark `#434040` band + white text (`@table-header-bg` / `@table-header-color`)
  - **`--b-row-hover-bg`** — `:host([hoverable])` row hover in `b-table` (default `var(--b-bg-tertiary)`). Finstat → warm-yellow `rgba(253,222,129,.3)` (`@clr-hover`)
- Documented in `Birko.Web.Components` README + CLAUDE.md

## 2026-06-10 — Birko.Web — modular, per-project opt-in themes
Made themes pluggable so each consumer app ships only the theme tokens it actually uses, instead of every theme sitting in one `tokens.css`. **Behavior change:** the switcher no longer hard-codes light/dark/neon/finstat — apps register what they want.
- **CSS split** — `Birko.Web.Components/css/tokens.css` now holds only the base/light `:root` tokens (+ reduced-motion). Each alternate theme moved to its own file under `css/themes/` (`dark.css`, `neon.css`, `finstat.css`). A project links/bundles `tokens.css` + only the theme files it uses; unused theme bytes never load. A project-private theme is just a `[data-theme="my-brand"]` block in the app's own CSS — no framework edit
- **Theme registry** (`Birko.Web.Shell/src/shell/theme-registry.ts`) — `registerTheme()` / `registerThemes()` / `unregisterTheme()` / `getRegisteredThemes()` + `BUILTIN_THEMES` (ready-made `{id,label,icon}` for light/dark/neon/finstat — metadata only, CSS still opt-in). `'light'` is always present and can't be removed. Exported from `birko-web-shell`
- **Shell wiring** — `BCoreAppShell.getAvailableThemes()` now returns `getRegisteredThemes()` (was a hard-coded array). `renderThemeDropdown()` auto-hides the switcher when fewer than 2 themes are registered (a single-theme app has nothing to switch). App bootstrap: `registerThemes([BUILTIN_THEMES.dark, BUILTIN_THEMES.finstat])` + link the matching CSS
- **Migration** — apps that relied on the old default switcher must add a one-line `registerThemes([...])` in bootstrap (and link the theme CSS) to restore the entries they want
- Documented in `Birko.Web.Components` README/CLAUDE.md + `Birko.Web.Shell` README (new "Themes" section)
- **Base-token upstreaming** (from the Symbio consumer) — promoted genuinely-universal tokens into Birko base `:root` so consumers need fewer local overrides: a **status-alpha** system (`--b-color-{danger,success,warning,info}-alpha-bg/-border`), a **neutral overlay** system (`--b-overlay-subtle/light/medium`), and scale extensions (`--b-text-4xl`, `--b-icon-2xl`, `--b-border-width-thick`). `dark.css`/`neon.css` retune alpha+overlay for dark surfaces. Also fixed a latent z-index bug: `--b-z-dropdown` `100 → 220` so menus overlay sticky bars (`--b-z-sticky: 200`). Symbio now imports neon/finstat (which carry their own alpha/overlay) and a build-time token-parity guard warns when Birko base adds tokens Symbio's `:root` lacks

## 2026-06-08 — Birko.Web.Shell — `renderHeaderActions()` hook
Added a first-class extension seam for app-specific header controls, replacing the prior practice of overriding `renderThemeDropdown()` to smuggle in unrelated buttons.
- **`BCoreAppShell.renderHeaderActions(): string`** — new `protected` hook, returns `''` by default. Rendered at the left edge of the header action cluster (before the theme switcher and user area) in all three layouts: the core/minimal `renderHeader()`, the sidebar shell (reuses core's header), and `BAppShell`'s ribbon `after-tabs` slot. The header is not re-rendered by `refresh*()`, so subclasses wire returned controls once in `onMount()` and keep mutable state in sync from store subscriptions
- **Why** — `renderThemeDropdown()` is documented as rendering only the theme switcher, and the shell's `refreshThemeMenu()`/`_setupThemeDropdown()` assume its markup *is* the switcher. The old hack (prepend custom markup, chain `super.renderThemeDropdown()`) silently dropped the custom controls whenever `showThemeSwitcher` was false, and hid the extension point from the base class
- **Consumer migration** — the gameshow control shell (`gs-control-shell`, the only consumer that was overriding `renderThemeDropdown`) now overrides `renderHeaderActions()` for its key-color picker + reload button; its `onMount` wiring is unchanged. `renderThemeDropdown()` remains for legitimately restyling the switcher itself. Documented in `Birko.Web.Shell` CLAUDE.md + README

## 2026-06-08 — Birko.Web.Components — b-color-picker
New `inputs` component backported from the Gameshow control surface (its chroma-key backdrop picker, previously a raw native `<input type="color">`).
- **`<b-color-picker>`** — pairs a native color swatch (reskinned with `--b-*` tokens; the swatch opens the OS color dialog) with a monospace hex text field. Both stay in sync: dragging the swatch live-previews into the text field, typing a valid hex live-previews on the swatch, and either control commits on `change`.
- Accepts loose hex input (`#rgb`, `rgb`, `#rrggbb`, `rrggbb`); the canonical `value` and the `change` detail are normalized to lowercase `#rrggbb`. Bad hex snaps back to the current value on commit.
- **Opt-in `alpha`** — adding the attribute shows an opacity slider (native `<input type="range">`, track tinted over a token checkerboard so opacity reads visually) and switches the canonical value to 8-digit `#rrggbbaa`; the text field then accepts `#rgba`/`#rrggbbaa` too. The native swatch is sRGB-only, so RGB comes from the swatch and the alpha byte from the slider. Typing a 6-digit hex in alpha mode preserves the current slider alpha.
- Two-event contract mirroring native: `input` = ephemeral live preview during any drag/typing (no attribute reflection → no re-render storm), `change` = committed (attribute reflected). Standard form-input surface: `formFieldSheet` + `formControlSheet`, `label`/`hint`/`error`/`required`/`disabled`, `size` (vertical-footprint — swatch matches `--b-control-min-height*`), i18n via `bwc.colorPicker.*`.

## 2026-06-05 — Birko.Web.Components — b-button-group + b-toolbar
Two new layout components backported from the Gameshow control surface (its contest transport controls):
- **`<b-button-group>`** — bordered, padded, rounded cluster (`--b-bg-secondary` fill, `--b-radius-lg`) that makes related `b-button`s read as one unit (e.g. Start/Pause/Stop). Purely presentational: `role="group"`, optional `label` → aria-label, default slot only — slotted buttons keep their own variant/size/clicks.
- **`<b-toolbar>`** — flex row (`--b-space-lg` gap, wraps) laying out the clusters; `role="toolbar"`, optional `label`. An `end` slot pushes content to the far edge — the conventional spot for destructive/exit actions (the end container hides itself when empty so no phantom trailing gap, same slotchange pattern as `b-card`).

## 2026-06-05 — Birko.Web.Shell — user area hides for anonymous apps
`BCoreAppShell.renderUserDropdown()` (inherited by `BSidebarAppShell` + `BAppShell`/ribbon) now follows the shell's return-value feature-toggle convention instead of always rendering a dropdown:
- **`getUserName()` returns `''`** → the whole user area (avatar + name + dropdown) is omitted — for anonymous apps (kiosks, public dashboards, ribbon apps without auth)
- **`getUserMenuItems()` returns `[]`** → static avatar + name badge (`.user-trigger.is-static`, no pointer cursor/hover) instead of a dropdown that opened an empty list
- `refreshUserMenu()` no-ops when items are empty; switching anonymous ↔ signed-in needs a full `update()` (apps already re-render on auth change since the username is baked into `render()`)

## 2026-05-30 — Birko.Web — `neon` theme + header theme switcher
Added a third built-in theme and a built-in theme switcher to all shells.
- **`neon` theme** — new `[data-theme="neon"]` token block in `Birko.Web.Components/css/tokens.css`: dark navy base (`#0b0f1a`), neon green primary (`#8cffb0` with dark `#06301a` text), magenta danger (`#ff4466`), cyan info (`#66e0ff`), amber warning (`#ffaa44`). Mirrors the `dark` theme's override set plus neon focus glows and an `--b-input-thumb-bg` flip
- **`--b-bg-gradient` token** — only defined in the `neon` theme (`radial-gradient(circle at 50% 30%, #16306a 0%, #0b0f1a 70%)`); the shell content area uses `var(--b-bg-gradient, var(--b-bg-secondary))` so light/dark are unaffected and any theme can opt into a radial backdrop
- **Theme switcher** — `BCoreAppShell` now renders a theme dropdown in the header (inherited by `BSidebarAppShell` + `BAppShell`/ribbon). New API: `setTheme(id)` (applies `data-theme`, persists to `{storagePrefix}-theme`, emits `theme-change`), `currentTheme` getter, `getAvailableThemes()` (override to add/localize), `themeMenuLabel`, `showThemeSwitcher`, `renderThemeDropdown()` helper, `refreshThemeMenu()`. Active theme shown with a checkmark; trigger shows the active theme's glyph. Reuses the existing `data-theme` restore path — no new persistence wiring
- New `ThemeOption` type in `shell-types.ts`. Switcher works out-of-box with English labels (no consumer i18n keys required)

## 2026-05-29 — Birko.Security.OAuth.Server — OAuth2 authorization server
New shared project that issues tokens — complements the existing `Birko.Communication.OAuth` client. Pure handler library (no ASP.NET dep); a host wires the four handlers to whatever HTTP framework it's running. Closes [tasks/EPIC-009/STORY-020/TASK-027](tasks/EPIC-009-communication-protocols/STORY-020-oauth2-server/TASK-027-birko-security-oauth-server.md).
- **Four grant types** — `client_credentials`, `authorization_code` (+ PKCE), `refresh_token`, RFC 8628 `urn:ietf:params:oauth:grant-type:device_code`. `password` and implicit are intentionally not supported (deprecated by OAuth 2.1)
- **Composition root** — `OAuthServer` owns one handler per endpoint: `Token`, `Authorize`, `DeviceAuthorization`, `ClientRegistration`. Hosts route their HTTP endpoints to those handlers
- **Persistence via `Birko.Data.Stores`** — five new entity models (`OAuthClient`, `AuthorizationCode`, `RefreshTokenRecord`, `DeviceCodeRecord`, `ConsentRecord`) plus matching `IXxxStore : IAsyncStore<T>` interfaces with default-interface-method lookups (`GetByClientIdAsync`, `GetByCodeAsync`, `GetByHashAsync`, …). Any backend that implements `IAsyncStore<T>` works
- **Security defaults** — PKCE required for public clients (`RequirePkceForPublicClients` = true), refresh-token rotation enabled (`RotateRefreshTokens` = true, per RFC 6819 §5.2.2.3). Client secrets and refresh tokens stored as SHA-256 hashes with `CryptographicOperations.FixedTimeEquals` verification — plaintext only exists in transit. Authorization codes are single-use (`Used` flag set on redemption)
- **Settings** — `OAuthServerSettings : Settings` (extends `Birko.Configuration.Settings`) — `AccessTokenLifetimeSeconds` (3600), `RefreshTokenLifetimeSeconds` (14d), `AuthorizationCodeLifetimeSeconds` (60), `DeviceCodeLifetimeSeconds` (600), `DeviceCodePollingIntervalSeconds` (5), `Issuer`, `RotateRefreshTokens`, `RequirePkceForPublicClients`, `SupportedScopes`
- **Token signing** — composes with existing `Birko.Security.Jwt` `JwtTokenProvider` (or any `ITokenProvider`). The handler builds the claims dictionary (`sub`, `client_id`, `iss`, `scope`) and delegates to the provider
- **RFC 8628 device flow** — `DeviceAuthorizationHandler` issues `device_code` + ambiguity-free `user_code` (alphabet `BCDFGHJKMNPQRSTVWXYZ23456789`); `ApproveAsync(userCode, userId, approved)` is the consent-UI bridge. Token endpoint enforces poll interval as `slow_down`, `authorization_pending` until the user clicks Allow, `access_denied` on Deny, `expired_token` after `DeviceCodeLifetimeSeconds`
- **Dynamic client registration** (RFC 7591) — `ClientRegistrationHandler.RegisterAsync` returns the plaintext secret once at registration; subsequent `GetAsync` calls omit it. `ClientType` is immutable post-creation. Host gates this endpoint behind its own admin-only policy
- **43 xUnit + FluentAssertions tests** — one success + one failure path per grant type plus PKCE/redirect-URI/replay/expiry edges, full device-flow lifecycle, all four `OAuthErrorCodes` callsites exercised
- **Registered in** `Birko.Framework.slnx` (Security folder + Tests folder), `Birko.Framework.code-workspace`, and `Birko.Framework.csproj` aggregator

## 2026-05-28 — Task tracking — `tasks/` folder + `/tasks` skill
Introduced hierarchical task tracking (Epics → Stories → Tasks) as markdown files under `tasks/`. Pilot import migrated all open work from the former `TODO.md` into a structured backlog: **12 EPICs, 22 STORIES, 34 TASKs**, organized by area of concern (Web.Components polish, Data.Redis, Caching.NCache, Storage cloud providers, Messaging/MessageQueue expansion, Telemetry exporters, Health checks, Communication protocols, RavenDB index ergonomics, Test coverage gaps, MQTT v5). Each TASK is self-contained (Context / Acceptance criteria / Out of scope) so a human or AI agent can pick it without re-discovery. Old `TODO.md` removed (history preserved in git).
- **Skill location** — `~/.claude/skills/tasks/` (global, reusable across projects)
- **Verbs** — `/tasks new`, `/tasks triage`, `/tasks pick`, `/tasks close <ID>`, `/tasks import <file|--github|--jira>`, `/tasks export <ID> --to github|jira`, `/tasks migrate --to github|jira`
- **Modes** — `local` (file-only, default) and `hybrid` (files + GitHub Issues / Jira export via `gh` CLI / Atlassian MCP). Configured per project in `tasks/.config.yml`
- **Shape detection** — meta-repo (Birko.Framework) uses per-sub-project `Birko.X/tasks/` for project-local work and root `tasks/` for cross-cutting epics with `affects: [Birko.AI, Birko.Data, ...]`; consumer solutions use solution-root `tasks/`
- **Lifecycle** — status-only, no archiving. Dashboard hides done by default; epics/stories are often open-ended areas of concern
- See [tasks/README.md](tasks/README.md) for the live dashboard

## 2026-05-28 — Birko.AI.Agents — Prompt convention realignment
Audited all 20 agents in `Birko.AI.Agents` against Anthropic's "Building Effective Agents" principles (simplicity, transparency, well-documented tools) and the helper pattern in `Agent.cs` (`GetDepthGuidance()` virtual + `GetFileOperationGuidelines()` + `GetCommonBestPractices()` statics). Realigned 5 outlier agents so the catalogue is consistent.
- **`RefactorAgent` / `TestAgent` / `DebugAgent` / `DocumentationAgent`** — were inlining a local `Options.ModelDepth switch` directly in `SystemPrompt`'s getter, bypassing the `protected virtual string GetDepthGuidance()` hook in `Agent.cs:126`. Now each `protected override`s the method (same shape `OrchestratorAgent` already uses) and the prompt interpolates `{GetDepthGuidance()}` like every other agent. Depth-behavior tuning is now in one method per agent instead of buried in a string literal
- **`TestAgent` guideline list** trimmed 24 → 9 bullets (removed redundant phrasing — AAA pattern, behavior-over-implementation, boundary values, mocking, pyramid, regression tests, clean code). System prompt down from ~100 lines to ~70
- **`RefactorAgent` guideline list** trimmed 22 → 9 bullets (consolidated duplicate "preserve behavior", "small steps", "no bug fixes during refactor" mentions). System prompt down from ~95 lines to ~65
- **`{GetCommonBestPractices()}`** now interpolated by `RefactorAgent`, `TestAgent`, `DebugAgent`, `DocumentationAgent`, `DiagrammingAgent` — previously these 5 silently dropped the shared best-practices block (test, retry on failure, be methodical). Replaced redundant ad-hoc "be systematic and methodical" closers with the helper
- **`HtmlCodingAgent` / `CssCodingAgent`** — step 4 was "Test your changes by viewing rendered output", which the agent loop cannot do (no browser tool). Replaced with "Validate by re-reading the file (structure/syntax/specificity)" — reachable with existing tools
- Reference for future audits: [`design-agent` skill](~/.claude/skills/design-agent/SKILL.md) encodes both the Anthropic pattern ladder and the Birko.AI prompt template + review checklist

## 2026-05-26 — Birko.Web.Components — b-date-range-picker

Added `<b-date-range-picker>` as the 21st input component (total components now 55). Selects a date range with two endpoints in one panel; the API mirrors existing `b-*` input conventions (single `value`, `inputValue` contract, `change` event with `{name, value: {start, end}}` payload — same shape as `b-range` range mode).

- **Two-month side-by-side panel** by default; `months-visible="1"` for narrow viewports. Single `value="start/end"` ISO interval attribute (uniform with all other inputs); `getRange()` / `setRange({start, end})` typed accessors mirror the `getSelected()` / `setSelected([])` family. Static `BDateRangePicker.setLocale({months, days, today, clear, apply, cancel, presets})` matches the other date components.
- **Two commit modes** — default is instant commit on second click; `confirm` boolean attribute switches to an Apply-button footer (Apply enabled only when both endpoints set, Cancel reverts pending range).
- **Range painting via `data-range` attribute** (`start | end | in | hover-in | hover-end`) — JS only mutates the attribute, CSS does all the visual work via `::before` pseudo-elements. Smooth hover preview as the user mouses over potential end dates without `innerHTML` thrash. Emits `range-preview` `{ start, end }` during pick, `change` `{ name, value: { start, end } }` on commit.
- **Constraints** — `min` / `max` (hard date bounds), `min-days` / `max-days` (range length; auto-extend / clip on second click). `min-days="0"` default allows same-day ranges; auto-swap if user picks `end < start`.
- **Opt-in presets** — `presets='[{"label":"bwc.daterange.preset.last7days","start":"-7d","end":"today"}]'` JSON; no footer if `presets` attribute absent (no default list). Token resolver handles `today`, `yesterday`, `month-start`/`-end`, `year-start`/`-end`, `quarter-start`, relative `±Nd/w/m/y`, and any ISO date.
- **Native fallback** (`native` attribute) renders two `<input type="date">` posting as `${name}-start` / `${name}-end`.
- **b-form integration** — new `date-range` `FieldType`, `_getFieldValue` returns `{ start, end } | null`, `_setFieldValue` accepts object or interval string. `FormField` gains `minDays`, `maxDays`, `monthsVisible`, `confirm`, `presets`, `separator` props.
- **i18n** — new `bwc.daterange.*` keys (`placeholderStart`, `placeholderEnd`, `apply`, `presets`, `nightsCount`, `daysCount`, `preset.*`) in `locales/en.json`.

## 2026-05-24 — Birko.Models.SQL Split into Framework + Domain Siblings

Decoupled the fluent SQL mapping framework from the canonical domain mappings. `Birko.Models.SQL` now contains only `Mapping/` (ModelMap, FieldBuilder, IModelMapping, ModelMapRegistry — ~150 LOC). Consumers can pick exactly the domains they persist.

- **5 new sibling shared projects** — `Birko.Models.Users.SQL` (8 mappings: User, UserLogin, UserProfile, UserRole, UserTenant, Role, RolePermission, Tenant), `Birko.Models.Customers.SQL` (Address+InvoiceAddress+ContactPerson, Customer), `Birko.Models.Inventory.SQL` (StockItem, StorageLocation, InventoryDocumentLine), `Birko.Models.Pricing.SQL` (Currency, Tax, PriceGroup), `Birko.Models.Product.SQL` (MeasureUnit, UnitConversion, ProductPartnerCode)
- **`CurrencyMapping.cs` split** — the old file mixed Pricing (Currency/Tax/PriceGroup) with Product (MeasureUnit/UnitConversion). Now in their respective domain projects; new `MeasureUnitMapping.cs` in `Birko.Models.Product.SQL`
- **Namespaces renamed** — `Birko.Models.SQL.Mappings` → `Birko.Models.{Domain}.SQL.Mappings` (e.g. `Birko.Models.Users.SQL.Mappings.TenantMapping`). Consumers using `RegisterFromAssembly(typeof(SomeMapping).Assembly)` need to update the type anchor (Symbio updated in same change)
- **Aggregator imports** — `Birko.Framework.csproj` and `Symbio.Birko.csproj` now `<Import>` the 5 new projitems alongside the existing `Birko.Models.SQL.projitems`. Consumers that don't persist a given domain can simply omit that domain's `.SQL` import
- **Why** — importing `Birko.Models.SQL` previously forced you to also import `Birko.Models.Users` + `.Customers` + `.Inventory` + `.Pricing` + `.Product` (the 5 domain projects whose canonical mappings live there), regardless of which models you actually use. Split removes that coupling

## 2026-05-19 — Birko.Serialization.Yaml

Added YAML serializer sibling project alongside `.Newtonsoft` / `.MessagePack` / `.Protobuf`.

- **`YamlDotNetSerializer`** — implements `ISerializer` over YamlDotNet; `ContentType` = `application/yaml`, `Format` = `SerializationFormat.Yaml`
- **`SerializationFormat.Yaml`** added to the enum
- Constructor accepts optional `YamlDotNet.Serialization.ISerializer` / `IDeserializer` to override the default pipeline (camelCase + `IgnoreUnmatchedProperties()`)
- Stream overloads wrap UTF-8 `StreamReader`/`StreamWriter` (`leaveOpen: true`); async methods are sync-wrapped since YamlDotNet has no async API
- 13 new tests in `Birko.Serialization.Tests/Yaml/` (xUnit + FluentAssertions)

## 2026-04-28 — Birko.Security.AspNetCore — Per-Request Permissions + JWT-from-Query

ASP.NET Core integration gained an opt-in path for hosts whose effective permission sets are too large to embed in a JWT, plus query-string token retrieval for SSE/WebSocket clients that cannot set headers.

- **`IUserPermissionResolver`** (`Authorization/IUserPermissionResolver.cs`) — host-supplied service returning the effective permission set for `(userId, tenantId)`; typically backed by the app's role store + cache
- **`PermissionResolutionMiddleware`** — runs after `UseAuthentication`, invokes the resolver once per request, stashes the set in `HttpContext.Items`
- **`ResolvedPermissionsCurrentUser`** — `ICurrentUser` that reads identity claims like `ClaimsCurrentUser` but pulls `Permissions` from the middleware slot (sync getter, no per-call DB hit)
- **Opt-in DI** — `services.UseResolvedPermissions()` swaps the registration; `app.UseBirkoPermissionResolution()` inserts the middleware. Default `ClaimsCurrentUser` behavior unchanged unless host opts in
- **JWT from query string** — `JwtBearerExtensions` now wires `OnMessageReceived` to extract `?token=…` for `/api/sse` and `/ws` paths (EventSource cannot set custom headers)
- **Comma-joined claim values** — `ClaimsCurrentUser.Permissions`/`Roles` now split each claim value on `,` so both shapes work: multiple same-name claims and a single comma-joined claim (fixes superadmin `*` bypass when multiple permissions packed into one claim)
- **`TenantId → TenantGuid`** rename across `ICurrentUser` and related types (originally 2026-03-15)

## 2026-04-24 — Birko.Web — Unified i18n

All three Birko.Web.* packages share a single global i18n singleton — no more per-component `this.attr('label-X', 'English')` islands or one-off `setTranslate` hooks.

- **`birko-web-core` exports** `i18n` (default `I18n` instance), `t(key, params?, fallback?)`, `useI18n(instance)` (swap in an app-owned instance), `onI18nChange(fn)` (subscribers auto re-wire on swap), plus the existing `I18n` class, `createFormatter`, `getFormatter`
- **`BaseComponent.label(attrName, i18nKey, fallback, params?)`** — new helper: explicit attribute wins > global i18n lookup > English fallback; all `bwc.*`-prefixed keys interpolate `{param}` placeholders; `BaseComponent` auto-subscribes to `onI18nChange` so components re-render on `setLocale()`
- **`BaseComponent.listen<T extends Event>(...)`** — now generic so consumers can pass `(e: KeyboardEvent) => void` without casts
- **~150 call sites migrated** across command-palette, ribbon, sidebar, tree-menu, pagination, toast, empty, confirm-dialog, modal, drawer, spinner, file-upload, search-input, json/xml-viewer, object-tree, table, markdown-editor, datetime-picker, time, date-picker
- **Canonical key namespaces** — `bwc.*` for Components (`bwc.common.close`, `bwc.palette.placeholder`, `bwc.pagination.prev`, etc., shipped in `Birko.Web.Components/locales/en.json`); `bws.*` for Shell (`bws.common.new`, `bws.common.confirmDelete`, `bws.pagination.items`, `bws.ribbon.selectModule`). Shell's `t()` auto-interpolates `{entity}` with `this.entityLabel` so bundle entries like `"bws.common.new": "Nový {entity}"` produce localized entity-specific strings
- **`b-app-shell.ts` simplified** — no longer passes `label-*` attributes to `<b-ribbon>` / `<b-command-palette>`; those components pull from `bwc.*` global i18n directly
- **Backward-compatible shims preserved** — `BForm.setTranslate(fn)` still works (forwards to legacy path), `BDatePicker.setLocale(...)` / `BDatetimePicker.setLocale(...)` / `BTime.setLocale(...)` still win over global i18n for per-class month/day overrides, `base-crud-page.t(key)` still returns English defaults and can still be overridden
- **Library ergonomics tuned** for strict-mode consumer apps: `TableColumn.render` now accepts `any`-typed callbacks, `FormGroupDef.layout`/`TableColumn.align`/`FieldType`/`RuleType` widened via `(string & {})` so inline object literals type-check
- **Consumer migration** — one line: `useI18n(mineI18n)` in app bootstrap. Existing `label-*` attributes keep working unchanged

## 2026-04-24 — Provider-Specific Settings Classes

Created typed settings descendants for all store providers, replacing hardcoded configuration with per-instance settings. Stores and connectors now read from typed settings instead of static properties or inline constants.

**New settings classes:**
- `SqlSettings` (Birko.Data.SQL) — `CommandTimeout`, `ConnectionTimeout`, abstract `GetConnectionString()`
- `MSSqlSettings` (Birko.Data.SQL.MSSql) — `MultipleActiveResultSets`, `TrustServerCertificate`; overrides `GetConnectionString()`
- `MySqlSettings` (Birko.Data.SQL.MySQL) — `BulkInsertBatchSize` (previously hardcoded `const`); overrides `GetConnectionString()`
- `PostgreSqlSettings` (Birko.Data.SQL.PostgreSQL) — `UseBinaryImport`; overrides `GetConnectionString()`
- `SqLiteSettings` (Birko.Data.SQL.SqLite) — extends `PasswordSettings` (not `SqlSettings`), `CommandTimeout`; virtual `GetConnectionString()`
- `Birko.Data.CosmosDB.Stores.Settings` — `PartitionKeyPath`, `RequestTimeout`, `AllowBulkExecution`, `GetCosmosClientOptions()`; `CreateDocumentStore()` helper
- `Birko.Data.RavenDB.Stores.Settings` — `RequestTimeout`, `CreateDocumentStore()` helper

**Settings hierarchy (final):**
```
Settings → PasswordSettings → RemoteSettings → SqlSettings → MSSqlSettings / MySqlSettings / PostgreSqlSettings
                                                      → CosmosDB Settings / RavenDB Settings
PasswordSettings → SqLiteSettings
SqlSettings → TimescaleDBSettings
```

**Store changes:**
- CosmosDB stores: `ISettingsStore<RemoteSettings>` → `ISettingsStore<Settings>`, removed static `PartitionKeyPath`/`RequestTimeout`
- RavenDB stores: `ISettingsStore<RemoteSettings>` → `ISettingsStore<Settings>`, removed static `RequestTimeout`
- SQL connectors: `CreateConnection` checks for typed settings first, uses `GetConnectionString()` when available
- TimescaleDB `Settings`: now extends `SqlSettings` instead of `RemoteSettings`
- Migration settings: `SqlMigrationSettings` extends `SqlSettings`; `CosmosMigrationSettings`/`RavenMigrationSettings` extend their provider `Settings`

**Downstream consumers updated:**
- BackgroundJobs (SQL, CosmosDB, RavenDB) — switched to typed settings
- Workflow (SQL, CosmosDB, RavenDB) — switched to typed settings

**Bug fix:** `AsyncRavenDBStore` previously ignored `RequestTimeout` entirely — now reads from `_settings.RequestTimeout` via `CreateDocumentStore()`.

---

## 2026-04-23 — Platform-Agnostic Migrations + FieldDescriptor Unification

Rewrote the migration system so migrations are written once and run against any provider. Unified `PropertyMap` (Birko.Models.SQL) with `FieldDescriptor` (Birko.Data.Patterns) into a single type.

**Migration system:**
- `IMigration` now has `Up(IMigrationContext context)` / `Down(IMigrationContext context)` — no more provider-specific base classes
- `IMigrationContext` provides `Schema` (ISchemaBuilder), `Data` (IDataMigrator), `Raw(Action<object>)`, `ProviderName`
- Schema abstractions in Birko.Data.Patterns: `FieldType` enum, `FieldDescriptor`, `ISchemaBuilder`, `ICollectionBuilder`, `IIndexBuilder`
- Each provider implements IMigrationContext: SQL (wraps DbConnection + AbstractConnector), MongoDB (IMongoDatabase), ElasticSearch (ElasticClient), RavenDB (IDocumentStore), CosmosDB (Database), InfluxDB (InfluxDBClient), TimescaleDB (extends SQL)
- NoSQL providers silently skip inapplicable operations (AddField/DropField are no-op on schema-less databases)
- Runner constructors take the store's native connector: `new SqlMigrationRunner(store.Connector)`, `new MongoMigrationRunner(store.Client)`
- Deleted provider-specific base classes: SqlMigration, MongoMigration, ElasticSearchMigration, RavenMigration, CosmosMigration, InfluxMigration

**FieldDescriptor unification:**
- `PropertyMap` (Birko.Models.SQL.Mapping) deleted — `FieldDescriptor` (Birko.Data.Patterns.Schema) now serves both model mapping and migrations
- `PropertyMapBuilder<T>` renamed to `FieldBuilder<T>` — wraps FieldDescriptor with fluent API
- Added to FieldDescriptor: ColumnName, IsIgnored, IndexName, IndexOrder, IndexDescending
- Changed FieldDescriptor from `init` to `{ get; set; }` to support the builder pattern
- `ModelMap<T>` and `ModelMapRegistry` updated to use FieldDescriptor
- All 31 consumer mapping files unchanged (fluent API surface is identical)

---

## 2026-04-23 — Birko.Communication.GraphQL

New GraphQL client project following Birko.Communication.OAuth patterns. Zero external NuGet dependencies — uses HttpClient for queries/mutations, ClientWebSocket for subscriptions, and Birko.Serialization (SystemJsonSerializer) for JSON.

**Components:**
- `GraphQLSettings` extends `RemoteSettings` (Endpoint = Location alias). Adds SchemaPath ("/graphql"), UseSubscriptions, SubscriptionProtocol enum (WebSocket/SSE), TimeoutSeconds (30), EnableAutoPersistedQueries, ExtraHeaders.
- `IGraphQLClient` interface with `QueryAsync<T>`, `MutateAsync<T>`, `SubscribeAsync<T>`, `ExecuteAsync<T>` plus OnRequest/OnResponse/OnError events.
- `GraphQLClient` implementation — static `GetClient(endpoint)` caching (RestClient pattern), optional HttpClient injection, thread-safe via SemaphoreSlim. Uses `ISerializer` for all JSON operations.
- `GraphQLRequest` — serializable request model with Query, Variables, OperationName, Extensions. Serialize via ISerializer.
- `GraphQLResponse<T>` — typed response with Data, Errors, Extensions. Static Deserialize factory.
- `GraphQLError` — error model with Message, Locations (line/column), Path, Extensions.
- `GraphQLSubscription<T>` — IObservable<T> over ClientWebSocket using graphql-ws protocol. IDisposable.
- `GraphQLRequestBuilder` — fluent API: Query(), Mutation(), Variables(), OperationName(), WithExtension(), Build().
- `GraphQLException` — mirrors OAuthException with Errors list and StatusCode.

**Tests:** 49 tests in Birko.Communication.GraphQL.Tests (xUnit + FluentAssertions).

---
## 2026-04-22 — Birko.Web.Components — Markdown Editor Formatting

Extended `b-markdown-editor` with all missing formatting options:
- **Heading dropdown** — single H button replaced with dropdown panel showing H1–H6 levels with markdown hints (`#` through `######`); positioned below button, closes on outside click
- **Table insertion** — toolbar button inserts 2-column GFM table template (`| Header | Header |`); renderer already handled GFM tables
- **Task list** — toolbar button inserts `- [ ] task` checkbox item; renderer converts `- [ ]` / `- [x]` to `<li class="task-list-item">` with styled checkbox inputs; handled before general unordered list regex to avoid conflicts
- **Highlight** — `==text==` wraps in `<mark>` tag; pandoc extension; styled with `--b-color-warning-light` background; added to Word HTML cleanup (`<mark>` → `==text==`)
- **Superscript** — `^text^` wraps in `<sup>` tag; pandoc extension; added to Word HTML cleanup (`<sup>` → `^text^`)
- **Subscript** — `~text~` wraps in `<sub>` tag; pandoc extension; single-tilde syntax doesn't conflict with double-tilde strikethrough (`~~`); added to Word HTML cleanup (`<sub>` → `~text~`)
- **Preview CSS** — `.preview-content mark` with warning-light background, `sup`/`sub` with 0.75em sizing, `.task-list-item` with no bullet and styled checkbox using `--b-color-primary` accent; all values use `--b-*` tokens

## 2026-04-22 — Birko.Web.Components — b-kanban Card Nesting

Extended `b-kanban` with recursive card nesting support:
- **Data model** — `KanbanCard` gains `parentId`, `collapsed`, and `children` fields; `KanbanConfig.renderCard` signature updated to `(card, depth) => string` for depth-aware custom rendering; `maxNestingDepth` config option limits recursion
- **Expand/collapse** — `_expanded` Set tracks parent card state across re-renders; toggle button (`.card-toggle`) follows b-tree-menu pattern; public API: `toggleCard`, `expandCard`, `collapseCard`, `expandAll`, `collapseAll`
- **3-zone drag-and-drop** — Cards support `drop-before` / `drop-inside` / `drop-after` zones (top 25% / middle 50% / bottom 25% of card height, same as b-tree-menu); dropping inside a card sets `parentId` on the moved card and nests it; descendant-drop prevention
- **Nested DnD** — All nested cards are draggable; `moveCard` accepts optional `targetParentId` parameter for nesting operations; card-move/card-reorder events include parent context
- **Keyboard navigation** — ArrowRight on parent: expand or focus first child; ArrowLeft: collapse or focus parent; ArrowLeft on root-level card: move to previous column; flat up/down/home/end across all visible cards
- **Nesting API** — `addSubCard(parentId, card)`, `getChildren(cardId)`, `removeCard` removes card and all descendants recursively
- **CSS** — `.card-children` container with dashed `border-left` guide (using `--b-border` token), `.card-toggle` expand/collapse button, `.card-child-count` badge, `.card-header` flex row, `.drop-inside` outline highlight; all spacing, colors, radii, transitions use existing `--b-*` tokens

## 2026-04-22 — Birko.Web.* — Design Token Audit + Tokenization Cleanup

Swept `Birko.Web.Core`, `Birko.Web.Components`, `Birko.Web.Shell` for bare CSS values that should be tokens and filled the gaps:

- **New tokens in `tokens.css`** — `--b-space-2xs: 0.125rem` (2px fixed spacing); `--b-input-thumb-bg: #ffffff` (always-white thumbs/glyphs on colored active states); popover / picker / dropdown dimension tokens: `--b-date-picker-width` (17rem), `--b-time-picker-width` (11rem), `--b-tooltip-max-width` (16rem), `--b-dropdown-min-width` (10rem), `--b-kanban-col-min-width` / `--b-kanban-col-max-width` (16/22rem), `--b-dropzone-icon-size` (3rem), `--b-file-thumb-size` (5rem), `--b-filter-chip-width` / `--b-filter-chip-width-lg` / `--b-filter-chip-width-xl` (12/16/24rem), `--b-app-brand-max-width` / `--b-app-user-max-width` (8/12rem).
- **Tokenized bare `#fff` on colored surfaces** — `b-ribbon` notification badges, `b-chat` outgoing bubble + send button, `b-checkbox` checkmark + indeterminate dash now use `var(--b-text-inverse)`; `b-switch` / `b-range` thumbs now use `var(--b-input-thumb-bg)`. Not a dark-mode bug (colored surfaces are theme-agnostic), but makes intent explicit and overridable per-theme if desired.
- **Tokenized rem/px dimensions** — `b-date-picker`, `b-datetime-picker`, `b-time`, `b-tooltip`, `b-kanban`, `b-file-upload`, `shared-styles.css` dropdown-panel, shell app-bars, and CRUD-page filter row now reference named dimension tokens instead of bare values.
- **Tokenized small spacing** — `b-badge`, `b-inline-edit`, `b-tree-menu`, `b-chat`, `b-table`, `b-checkbox`, `b-file-upload` now use `--b-space-2xs` for 2px offsets instead of raw `2px` / `0.125rem` literals.
- **Tokenized transition** — `base-crud-page.ts` sub-row border/shadow transition now uses `var(--b-transition)` instead of hardcoded `0.15s ease`.
- **Structural borders/outlines left as-is** — 2px/3px `border-bottom`, `outline`, `border-left` accents and focus-ring fallbacks are visual design constants, not spacing; tokenizing them would add indirection without benefit.
- **Form-control sizing unified** — new `--b-control-min-height: 2.375rem` (≈38px) token applied to `input`/`select`/`textarea` via `formControlSheet`, `.combo-container` via `comboControlSheet`, and `b-tag-input`'s container. Before: three different heights (`~2.375rem` / `2.25rem` / `2rem`). Now: `b-input` / `b-select` (plain + searchable) / `b-multi-select` / `b-tag-input` / `b-textarea` all share the same vertical footprint, border, radius, focus ring (`var(--b-border-focus)` + `var(--b-focus-ring)`), error ring, and disabled-state opacity (`var(--b-disabled-opacity)`).
- **Form-control `size="sm"` / `size="lg"` variants** — added tokens `--b-control-min-height-sm: 1.75rem` (≈28px, dense grids/toolbars) and `--b-control-min-height-lg: 2.75rem` (≈44px, touch targets). Applied via `:host([size="sm|lg"])` rules in `formControlSheet` / `comboControlSheet` / `b-tag-input`. Opt-in: `<b-input size="sm">`, `<b-select size="lg">`, etc. — no `observedAttributes` changes needed (pure CSS-attribute switch).
- **`size` attribute semantics documented** — `Birko.Web.Components/CLAUDE.md` now has a five-category convention table (vertical-footprint / text-scale / width / shape-weight / inline-chip) so new sizeable components pick the right interpretation. `b-button` normalized from class interpolation (`class="${size}"`) to the shared `:host([size="sm|lg"])` pattern used by every other component — fixed a latent bug where `loading=true` emitted two `class` attributes. `b-badge` gained a `sm` variant for symmetry with `lg`.

## 2026-04-22 — Birko.Web.Components — Sticky Headers + Shared Viewer Sheets

Extended the display-widget set added earlier on 2026-04-22 with unified sticky-header behavior and extracted shared CSS:

- **New attributes on `b-object-tree`** — `show-header` (opt-in card chrome + Expand/Collapse/Copy toolbar), `header-title` (default `Tree`), `no-copy`, `no-expand-actions`. When the header is shown the component gets the same card look as `b-json-viewer` / `b-xml-viewer` (bg-tertiary, border, radius).
- **New attributes on `b-object-tree`, `b-json-viewer`, `b-xml-viewer`, `b-code-block`** — `max-height` (internal scroll; body/pre becomes the scroll container) and `sticky-header="page"` (card overflow flips to visible so the `position: sticky` header pins to the page viewport instead). The two modes are mutually exclusive: `sticky-header="page"` takes precedence and ignores `max-height`.
- **New shared `@sheet` sections in `src/shared-styles.css`** — `dataViewerCard` (card shell + `.sticky-page` modifier), `dataViewerHeader` (compact sticky toolbar header with `.title` + `.actions`), `toolbarBtn` (small bordered action button with `.copied` state). Exported as `dataViewerCardSheet`, `dataViewerHeaderSheet`, `toolbarBtnSheet`.
- **Refactored viewers** — `b-object-tree` (when `show-header` is on), `b-json-viewer`, `b-xml-viewer`, and `b-code-block` now consume the three shared sheets via `static get sharedStyles()`; each component's local `styles` shrank by ~40–50 lines (removed duplicated card shell, header flex row, and toolbar button CSS).
- **Design rationale** — `b-card` is intentionally different (elevated bg, semibold text-lg header) so reusing it would misrepresent data-inspection widgets as content cards. A separate `dataViewerCard`/`dataViewerHeader`/`toolbarBtn` family keeps the two visual languages distinct while eliminating per-component duplication.

## 2026-04-22 — Birko.Web.Components — Display & Inspection Widgets

Added 7 new Shadow DOM components (42 → 50):

- **`b-pre`** (`src/data/b-pre.ts`) — preformatted text block with `wrap`, `max-height`, `size` attributes. Slot-based content, monospace, tokenized colors and spacing.
- **`b-code-block`** (`src/data/b-code-block.ts`) — syntax-highlighted code display with built-in lightweight highlighter for `json`, `js`, `ts`, `html`/`xml`, `css`, `sql`, `csharp`, `bash`. Supports `language`, `code`, `wrap`, `show-line-numbers`, `no-copy`, `max-height`, `size`. Emits `copy` event after clipboard write.
- **`b-definition-list`** (`src/data/b-definition-list.ts`) — semantic `<dl>` wrapper with `layout` variants (`stacked` default, `inline`, `horizontal`, `grid`). `setItems([{term,description}])` or slot-based usage.
- **`b-object-tree`** (`src/data/b-object-tree.ts`) — generic recursive property tree for any JS value. Lazy expansion via `expanded-depth`, upper bound via `max-depth`, optional `show-types`. Methods: `setData`, `getData`, `expandAll`, `collapseAll`. Emits `toggle` with path + expanded state.
- **`b-json-viewer`** (`src/data/b-json-viewer.ts`) — composes `<b-object-tree>` with JSON-specific UX: header with Expand/Collapse/Copy buttons, parse-error panel, `src` attribute for string input, accepts both strings (parsed) and objects via `setData`.
- **`b-xml-viewer`** (`src/data/b-xml-viewer.ts`) — parses XML via `DOMParser` and renders the DOM as a collapsible tree with distinct coloring for elements, attributes, text, CDATA, comments, and processing instructions. Header with Expand/Collapse/Copy. `setSource(xml)` or `setDocument(doc)`.
- **`b-tag-input`** (`src/inputs/b-tag-input.ts`) — freeform multi-value input. Supports Enter-to-create, Tab-to-commit, Backspace-to-remove, paste-split on delimiters (default `,`, newline, tab; configurable via `separators` attribute). Attributes: `label`, `name`, `value`, `placeholder`, `max-count`, `allow-duplicates`, `error`, `disabled`, `required`, `hint`. Events: `change`, `add`, `remove`, `reject` (duplicate/max-count). Methods: `setTags`, `getTags`, `clear`. Fills the gap between `b-input` (plain comma-string) and `b-multi-select` (dropdown-driven creatable).

All components use existing `--b-*` design tokens and shared stylesheets (`formFieldSheet`, `formControlSheet`) where applicable; no new shared sheets required. `b-tag-input` replaces/avoids the need for `<b-multi-select>` `creatable` mode when no predefined option list is available.

## 2026-04-16 — Store-Level Aggregation & Shared Helpers

Centralized aggregation abstractions in Birko.Data.Stores and refactored all view platform implementations to use shared helpers:
- **New in Birko.Data.Stores** — `AggregateFunction` enum (moved from Birko.Data.Views), `AggregateField` record, `AggregateQuery<T>` (filter, group-by, time bucketing, ordering, paging), `AggregateResult` (dictionary-backed with typed accessors), `IAggregatableStore<T>` / `IAsyncAggregatableStore<T>` (optional store interfaces for server-side aggregation), `AggregateHelper` (LINQ fallback implementation), `TimeIntervalParser` (human-readable interval → TimeSpan), `OrderByHelper` (dynamic `OrderBy<T>` applicator for IQueryable/IEnumerable)
- **Birko.Data.Views** — `AggregateFunction.cs` deleted; `AggregateClause` and `ViewDefinitionBuilder` now import `AggregateFunction` from `Birko.Data.Stores`
- **Birko.Data.SQL.Views** — `SqlViewTranslator` delegates to `AbstractConnectorBase.GetSqlFunctionName()` and `FunctionField.CreateFunctionField()` instead of local dictionary/helpers
- **Birko.Data.SQL.View** — `FunctionField.CreateFunctionField()` static method added for creating typed function fields from function name and source field
- **Platform view refactoring** — MongoDB Views uses `StoreAggregationHelper.BuildGroupStageFromPaths()`; ElasticSearch Views uses `StoreAggregationHelper` for metric creation/extraction; CosmosDB Views uses `CosmosAggregationHelper.BuildAggregateSqlParts()`; RavenDB Views uses `OrderByHelper.ApplyTo()`; all platforms removed hardcoded camelCase field name conversions
- **Design rationale** — Establishes layered aggregation: Birko.Data.Stores (portable abstractions) → Birko.Data.Views (fluent builder using shared types) → platform translators (native aggregation via shared helpers)

## 2026-04-15 — Birko.BackgroundJobs.XML

Added XML-file backend for BackgroundJobs to achieve parity with `Birko.Workflow.XML` and `Birko.Data.Sync.Xml`:
- Uses `AsyncXmlStore` from `Birko.Data.XML` with `[XmlRoot]`/`[XmlElement]` attributes on the model
- Nullable `DateTime?` fields use `IsNullable = true` for proper `xsi:nil` handling
- Job metadata uses a `SerializableMetadata` wrapper (`System.Xml.Serialization` has no native `Dictionary<TKey, TValue>` support)
- Registered in `Birko.Framework.slnx`, `Birko.Framework.code-workspace`, and `Birko.Framework.csproj`

## 2026-04-10 — Store Lazy-Init with Template Method Pattern

Refactored all abstract store base classes to auto-initialize on first CRUD operation:
- **AbstractStore/AbstractAsyncStore** — Public CRUD methods (`Create`, `Read`, `Update`, `Delete`, `Count`) call `EnsureInitialized`/`EnsureInitializedAsync` (double-checked locking, thread-safe) then delegate to `protected abstract *Core` methods
- **AbstractBulkStore/AbstractAsyncBulkStore** — Same pattern for bulk methods (`Create(IEnumerable)`, `Read(filter,orderBy,limit,offset)`, `Update(IEnumerable)`, `Delete(IEnumerable)`)
- **SQL Bulk Stores** — `DataBaseBulkStore`/`AsyncDataBaseBulkStore` also use template method with `protected virtual *Core` methods
- **Breaking change** — Concrete stores must override `*Core` methods instead of public CRUD methods (e.g., `CreateCoreAsync` instead of `CreateAsync`)
- **Cleanup** — Removed duplicate `_initialized`/`EnsureInitializedAsync` boilerplate from 12 Workflow + BackgroundJobs stores (~150 lines removed)
- `Init()`/`InitAsync()` is now idempotent — safe to call multiple times or never (auto-called on first CRUD)
- `Destroy()`/`DestroyAsync()` not affected — still explicit

## 2026-03-31 — AI/LLM Infrastructure

Extracted reusable AI agent framework from DraCode into Birko.AI.* projects:
- **Birko.AI.Contracts** — ILlmProvider interface, Message/ContentBlock/TokenUsage models, Tool base class, AgentOptions, LlmProviderFactory (registration-based, `Birko.AI.Factories` namespace)
- **Birko.AI** — LlmProviderBase (retry, SSE, OpenAI-style helpers), Agent base class (run loop, streaming, tool execution), AgentFactory (registration-based, `Birko.AI.Factories` namespace), 9 default tools
- **Birko.AI.Providers** — 11 LLM providers: Claude, OpenAI, AzureOpenAI, Gemini, Ollama, OpenAiCompatibleBase, LlamaCpp, Vllm, Sglang, GitHubCopilot, ZAi + ProviderRegistration (registers all providers with LlmProviderFactory)
- **Birko.AI.Agents** — CodingAgent base, 10 language agents, 4 task agents (Debug, Refactor, Test, Documentation), 4 media agents, OrchestratorAgent + AgentRegistration (registers all agents with AgentFactory, convenience Create)
- **Birko.AI.Resilience** — ProviderRateLimiter (sliding window), ProviderCircuitBreaker (3-state), CostTrackingService (budget enforcement), TrackedLlmProvider (decorator)
- **Birko.AI.Orchestration** — ITaskDispatcher, DirectTaskDispatcher, ImplementationPlan/Step models, StepDependencyAnalyzer (parallel groups, topological sort), EscalationAlert
- **Birko.Communication.OAuth.Providers** — GitHubOAuthProvider (pre-configured device flow factory using Birko.Communication.OAuth)
- **Birko.Contracts** — RetryPolicy extended with BackoffMultiplier and AddJitter
- **Birko.Helpers** — Added PathHelper (IsPathSafe, IsUnderDirectory, GetCanonicalPath)

## 2026-03-30 — ViewModel Repository MapToModel Refactor

Removed circular `ILoadable<TViewModel>` constraint from `TModel` in all ViewModel repositories:
- **Breaking change** — `TModel` no longer requires `ILoadable<TViewModel>`; Models have no knowledge of ViewModels
- **MapToModel** — New abstract method `MapToModel(TViewModel source, TModel target)` on `AbstractViewModelRepository` and `AbstractAsyncViewModelRepository`; consumer concrete repositories must override it
- **Abstract platform repos** — All platform ViewModel repositories (SQL, MongoDB, ElasticSearch, RavenDB, CosmosDB, JSON, InfluxDB, TimescaleDB) made abstract; consumers must subclass
- **DeleteAsync bug fix** — `AbstractAsyncViewModelRepository.DeleteAsync` no longer creates from `data.GetType()` (wrong); uses `CreateModelInstance()` + `MapToModel`
- Migration notes in [MIGRATION-VIEWMODEL-MAPTOMODEL.md](MIGRATION-VIEWMODEL-MAPTOMODEL.md)

## 2026-03-30 — Phase 1 Test Coverage

Completed core data layer test coverage:
- **Birko.Validation.Tests** (new) — 122 tests: rules (Required, Email, Length, Range, Regex, Custom), fluent AbstractValidator, ValidationResult, store wrapper integration (sync, async, bulk)
- **Birko.Data.Tests** (expanded) — 181 tests: added async soft-delete/audit/timestamp decorators, DefaultStoreWrapper, SluggableStoreWrapper, SlugGenerator, SoftDeleteFilter, UnitOfWork exceptions, PagedResult
- **Birko.Data.Sync.Tests** (new) — 21 tests: SyncProvider (initial/download/upload), SyncQueue (serialization, concurrency), model defaults

## 2026-03-26 — Filter-Based Bulk Operations

Added native filter-based Update/Delete to all bulk stores and repositories:
- **PropertyUpdate\<T\>** — Fluent builder for partial property updates, translated natively by platforms
- **Native implementations** — SQL (`UPDATE SET WHERE`/`DELETE WHERE`), MongoDB (`UpdateMany`/`DeleteMany`), ElasticSearch (`UpdateByQuery`/`DeleteByQuery`)
- **Action\<T\> overload** — Read-modify-save fallback for complex mutations
- All decorators (SoftDelete, Timestamp, Audit, Tenant, EventSourcing, Localization, Telemetry, Validation) updated
- All repositories (AbstractBulk, AsyncBulk, ViewModel) delegate to stores

## 2026-03-23 — Birko.Data.CosmosDB

New Azure Cosmos DB (NoSQL API) store provider:
- **Birko.Data.CosmosDB** — Stores (sync/async), Repositories, UnitOfWork (TransactionalBatch), IndexManagement
- **Birko.Data.Sync.CosmosDB** — Sync knowledge store for Cosmos DB
- **Birko.Data.Migrations.CosmosDB** — Migration framework for Cosmos DB (container, indexing policy, document ops)
- **CosmosDbHealthCheck** added to Birko.Health.Data
- Uses Microsoft.Azure.Cosmos SDK v3 with bulk execution enabled

## 2026-03-22 — Birko.Models Restructuring

Three-phase restructuring of the model layer:
- **Birko.Models.Contracts** — Domain interfaces: ICatalogItem, IPriceable, IVariantable, ICategorizeable, IBatchable, ILocatable, IHierarchical, IDocument/IDocumentLine, IContactable, IAddressable
- **Birko.Models (Value Objects)** — Money, MoneyWithTax, Percentage, PostalAddress, Quantity
- **Birko.Models.Inventory** — Clean replacement for Warehouse: StockItem, StockItemVariant, StorageLocation, StockMovement, InventoryDocument, InventoryDocumentLine
- **Birko.Models.Pricing** — Pricing domain: Currency, Tax, PriceGroup, PriceList, PriceListEntry, Discount
- **Birko.Models.SQL** — Fluent SQL mapping framework: ModelMap\<T\>, IModelMapping\<T\>, ModelMapRegistry
- Existing models implement contracts additively (Product→ICatalogItem+ISluggable, Item→ICatalogItem+ICategorizeable, Address→IAddressable+IContactable, ValueData→IPriceable, AbstractTree→IHierarchical, Category→IHierarchical+ISluggable)

## 2026-03-06 — New Model Projects

Extracted reusable models from FisData.Stock:
- **Birko.Models.Customers** — Address, Customer, InvoiceAddress
- **Birko.Models.Users** — User, Tenant (formerly Agenda), UserTenant
- **Birko.Models** — Added AbstractPercentage, AbstractTree, ValueData
- *(Birko.Models.Accounting was merged into Birko.Models.Pricing during the 2026-03-22 restructuring)*

## 2026-03-05 — Recent Fixes

- Replaced `NativeAsyncDataBaseStore` with `AsyncDataBaseStore` in async stores/repos
- Fixed `AbstractAsyncStore.CreateAsync` return type: `Task` → `Task<Guid>`
- Changed `Connector` property from `private set` to `protected set` in DataBaseStore/AsyncDataBaseStore
- Added parameterless constructor to `DataBaseRepository`
- Fixed PostgreSQL/MySQL stores settings handling
