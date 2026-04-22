# Birko Framework — Changelog

Historical record of architectural changes that are no longer "recent" but preserve design context. For the latest entries, see `Recent Updates` in [CLAUDE.md](CLAUDE.md). The definitive change history is `git log`; this file is a summarized narrative for architecture-level decisions that would be hard to reconstruct from commit-level diffs.

---

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
