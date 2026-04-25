# Birko Framework

Modular .NET framework with data access, communication, AI, and model infrastructure. General-purpose across enterprise back-office, e-commerce, presentation/CMS, desktop, IoT, and real-time domains.

See also:
- [CLAUDE-projects.md](CLAUDE-projects.md) — Full project catalog
- [CLAUDE-maintenance.md](CLAUDE-maintenance.md) — Maintenance guidelines, new project checklist, solution registration
- [CHANGELOG.md](CHANGELOG.md) — Historical architectural changes
- [README.md](README.md) + [docs/](docs/) — User-facing documentation

Each project has its own `CLAUDE.md` at `../Birko.{ProjectName}/CLAUDE.md` with project-specific details.

## Architecture

### Store Hierarchy (Template Method Pattern)
```
AbstractStore -> AbstractBulkStore (sync)
AbstractAsyncStore -> AbstractAsyncBulkStore (async)
```

Stores use lazy-init: CRUD methods auto-call `Init()`/`InitAsync()` before first use (via `EnsureInitialized`/`EnsureInitializedAsync` with double-checked locking). Concrete stores override `*Core` methods (e.g., `CreateCoreAsync` instead of `CreateAsync`). Public methods are `virtual` on the base class.

### SQL Stores
```
DataBaseStore<DB,T> -> DataBaseBulkStore<DB,T> (sync)
AsyncDataBaseStore<DB,T> -> AsyncDataBaseBulkStore<DB,T> (async)
```

### Repository Hierarchy
```
AbstractRepository -> AbstractBulkRepository (sync)
AbstractAsyncRepository -> AbstractAsyncBulkRepository (async)
```

### Settings Chain (Birko.Configuration)
```
ISettings (GetId)
  -> Settings (Location, Name)
    -> PasswordSettings (+Password)
      -> RemoteSettings (+UserName, +Port, +UseSecure)
        -> SqlSettings (+CommandTimeout, +ConnectionTimeout, abstract GetConnectionString)
          -> MSSqlSettings (+MultipleActiveResultSets, +TrustServerCertificate)
          -> MySqlSettings (+BulkInsertBatchSize)
          -> PostgreSqlSettings (+UseBinaryImport)
          -> TimescaleDBSettings (+TimeColumn, +ChunkTimeInterval)
    -> SqLiteSettings (+CommandTimeout, Path, GetConnectionString) — extends PasswordSettings
    -> CosmosDB Settings (+PartitionKeyPath, +RequestTimeout, +AllowBulkExecution, GetCosmosClientOptions)
    -> RavenDB Settings (+RequestTimeout, CreateDocumentStore)
    -> MongoDB Settings (+AuthDatabase, +ReplicaSet, GetConnectionString) — already existed
    -> RedisSettings (+Database, +KeyPrefix, GetConnectionString) — already existed
```

### Dependency Flow
```
Birko.Contracts (zero deps: ILoadable, ICopyable, IDefault, ITimestamped, IGuidEntity, ILogEntity, RetryPolicy)
  -> Birko.Configuration (Settings hierarchy, namespace Birko.Configuration)
  -> Birko.Data.Core (AbstractModel, ViewModels, Filters, Exceptions)
    -> Birko.Data.Stores (store interfaces, imports Configuration)
      -> Birko.Data.Repositories

Birko.Models.Contracts (zero deps: ICatalogItem, IPriceable, IVariantable, ICategorizeable, IBatchable, ILocatable, IHierarchical, IDocument, IContactable, IAddressable)
  -> Birko.Models (AbstractPercentage, AbstractTree, ValueData + Value Objects: Money, MoneyWithTax, Percentage, PostalAddress, Quantity)
    -> Birko.Models.Inventory / .Pricing / .Customers / .Users / .Product / .Category / .SEO (clean, no SQL attrs)
    -> Birko.Models.SQL (ModelMap<T>, IModelMapping<T>, ModelMapRegistry — fluent SQL mapping)

Birko.Time.Abstractions (zero deps: IDateTimeProvider, SystemDateTimeProvider, TestDateTimeProvider)
  -> Birko.Time (calendars, working hours, time zones)

Birko.Data.Patterns + Birko.Data.Tenant + Birko.Time.Abstractions
  -> Birko.Data.Composition (StoreWrapperBuilder — runtime decorator chains)

Birko.Data.Core
  -> Birko.Data.Tagging (ITaggable, Tag, EntityTag, ITagService, TagServiceBase)

Birko.Data.Patterns (FieldType, FieldDescriptor, ISchemaBuilder, ICollectionBuilder, IIndexBuilder, IIndexManager, IndexDefinition, ISoftDeletable, IAuditable, ISpecification, IUnitOfWork, PagedResult)
  -> Birko.Data.Migrations (IMigrationContext, IDataMigrator, IContextualMigration, IMigration, IMigrationRunner, IMigrationStore)
    -> Birko.Data.Migrations.SQL (SqlMigrationContext — reuses AbstractConnector), .MongoDB, .ElasticSearch, .RavenDB, .CosmosDB, .InfluxDB, .TimescaleDB

Birko.AI.Contracts (zero deps: ILlmProvider, Message, ContentBlock, Tool, AgentOptions, LlmProviderFactory)
  -> Birko.AI (LlmProviderBase, Agent base, AgentFactory (registration-based), default tools)
    -> Birko.AI.Providers (Claude, OpenAI, Gemini, Ollama, AzureOpenAI, etc. + ProviderRegistration)
    -> Birko.AI.Agents (CodingAgent, language agents, media agents + AgentRegistration)
    -> Birko.AI.Orchestration (ITaskDispatcher, ImplementationPlan, StepDependencyAnalyzer)
  -> Birko.AI.Resilience (ProviderRateLimiter, ProviderCircuitBreaker, CostTrackingService, TrackedLlmProvider)

Birko.Communication.OAuth (IOAuthClient, OAuthClient, OAuthSettings)
  -> Birko.Communication.OAuth.Providers (GitHubOAuthProvider — pre-configured device flow)

Birko.Communication.GraphQL (IGraphQLClient, GraphQLClient, GraphQLSettings — queries, mutations, subscriptions over HttpClient + ClientWebSocket)

Birko.BackgroundJobs (IJobQueue, JobDescriptor, RetryPolicy, JobProcessor, JobScheduler)
  -> 8 backends: .SQL, .ElasticSearch, .MongoDB, .RavenDB, .JSON, .XML, .Redis, .CosmosDB

Birko.Workflow (WorkflowBuilder, WorkflowEngine, guards, actions, Mermaid/DOT)
  -> 7 backends: .SQL, .ElasticSearch, .MongoDB, .RavenDB, .JSON, .XML, .CosmosDB
```

### Reference Implementations
- **ElasticSearch** store — reference for async/bulk operations
- **JSON** store — reference for file-based storage
- **XML** store — reference for file-based storage with `System.Xml.Serialization` (note: no native `Dictionary` support — use wrapper types)

## Usage in Consumer Solutions

When using Birko.Framework projects in your solution, create a single aggregator library project named `{YourSolution}.Birko` (e.g. `FisData.Birko`) and include all `Birko.*` shared project references there. Your other projects then reference only `{YourSolution}.Birko`. This avoids compilation and transitive reference issues that arise when multiple projects import overlapping sets of shared projects independently.

## Conventions
- All stores implement: `IStore`, `IAsyncStore`, `IBulkStore`, `IAsyncBulkStore`
- All repositories implement: `IRepository`, `IAsyncRepository`, `IBulkRepository`, `IAsyncBulkRepository`
- Bulk stores support filter-based Update/Delete: `Update(filter, PropertyUpdate<T>)`, `Update(filter, Action<T>)`, `Delete(filter)`
- Use `PropertyUpdate<T>` for native platform operations (SQL SET, MongoDB $set, ES UpdateByQuery); use `Action<T>` for complex mutations
- New platform stores should override `Update(filter, PropertyUpdate<T>)` and `Delete(filter)` for native performance
- Concrete stores override `protected *Core` methods (e.g., `CreateCoreAsync`, `ReadCore`), **NOT** the public CRUD methods. The base class handles lazy-init in the public wrapper
- Use protected setters for properties that derived classes need to modify
- `RemoteSettings` should be passed via `base.SetSettings()`, not constructed inline

## Code Style
- **Guard clauses:** Use early returns instead of wrapping entire method bodies in if blocks. Prefer `if (x == null) return;` over `if (x != null) { ... }`.
- **No nullable warnings:** All new code must compile without CS8600–CS8605, CS8618, CS8625. Use proper null checks, `!` only when provably safe, or `?` annotations.

## Testing
- All test projects use **xUnit + FluentAssertions**
- Every new public functionality must have corresponding tests in `Birko.{ProjectName}.Tests`
- Test both success and failure cases; include edge cases and boundary conditions
- Each test project has its own `CLAUDE.md` describing scope and conventions
- See [CLAUDE-maintenance.md](CLAUDE-maintenance.md) for test requirements on new projects and health check patterns

## Recent Updates

For older entries, see [CHANGELOG.md](CHANGELOG.md).

### Birko.Web — Unified i18n (2026-04-24)
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

### Provider-Specific Settings (2026-04-24)
Created typed settings descendants for all store providers. Each provider now has its own `Settings` class with platform-specific configuration instead of relying on static properties or inline constants.
- **New settings classes:** `SqlSettings` (abstract base with `CommandTimeout`, `ConnectionTimeout`, abstract `GetConnectionString()`), `MSSqlSettings`, `MySqlSettings`, `PostgreSqlSettings`, `SqLiteSettings`, CosmosDB `Settings`, RavenDB `Settings`
- **Settings hierarchy:** `RemoteSettings → SqlSettings → MSSqlSettings/MySqlSettings/PostgreSqlSettings`; CosmosDB/RavenDB `Settings` extend `RemoteSettings` directly; `SqLiteSettings` extends `PasswordSettings`; `TimescaleDBSettings` now extends `SqlSettings`
- **Store updates:** CosmosDB/RavenDB stores changed from `ISettingsStore<RemoteSettings>` to `ISettingsStore<Settings>`, removed static `PartitionKeyPath`/`RequestTimeout`
- **SQL connectors:** read from typed settings first, use `GetConnectionString()` override
- **Downstream:** BackgroundJobs and Workflow backends (SQL, CosmosDB, RavenDB) switched to typed settings
- **Bug fix:** `AsyncRavenDBStore` now correctly applies `RequestTimeout` to `DocumentStore.Conventions`

### Platform-Agnostic Migrations (2026-04-23)
All migrations are platform-agnostic — write once, run against any provider.
- **`IMigration`** — `Up(IMigrationContext context)` / `Down(IMigrationContext context)` with Version, Name, Description, CreatedAt
- **`AbstractMigration`** — base class implementing IMigration
- **Schema abstractions** (Birko.Data.Patterns) — `FieldType` enum, `FieldDescriptor`, `ISchemaBuilder`, `ICollectionBuilder`, `IIndexBuilder`; reusable by stores and views
- **`IMigrationContext`** — `Schema` (ISchemaBuilder), `Data` (IDataMigrator), `Raw(Action<object>)` escape hatch, `ProviderName`
- **`IDataMigrator`** — UpdateDocuments, DeleteDocuments, CountDocuments, CopyData, BulkInsert
- **Provider contexts** — each provider translates agnostic operations to native calls: SQL (reuses AbstractConnector from store), MongoDB (MongoDBClient from store), ElasticSearch (ElasticClient), RavenDB (IDocumentStore), CosmosDB (Container), InfluxDB (InfluxDBClient), TimescaleDB (extends SQL context)
- **NoSQL providers** silently skip inapplicable operations (AddField/DropField are no-op on schema-less databases)
- **Runner constructors** take the store's native connector: `new SqlMigrationRunner(store.Connector)`, `new MongoMigrationRunner(store.Client)`
- **FieldDescriptor unified** — `PropertyMap` (Birko.Models.SQL) merged into `FieldDescriptor` (Birko.Data.Patterns). One type for both SQL model mapping and migration schema building. `PropertyMapBuilder<T>` renamed to `FieldBuilder<T>`

### Birko.Communication.GraphQL (2026-04-23)
New GraphQL client project with zero external dependencies (HttpClient + ClientWebSocket + Birko.Serialization):
- **GraphQLSettings** — extends RemoteSettings, Endpoint = Location alias, adds SchemaPath, UseSubscriptions, SubscriptionProtocol, TimeoutSeconds, EnableAutoPersistedQueries, ExtraHeaders
- **IGraphQLClient / GraphQLClient** — Query, Mutation, Subscription; static GetClient caching; optional HttpClient injection; OnRequest/OnResponse/OnError events
- **GraphQLRequest / GraphQLResponse / GraphQLError** — request serialization via ISerializer, typed response deserialization, error model with locations/path/extensions
- **GraphQLSubscription** — IObservable<T> over WebSocket; graphql-ws protocol (connection_init, start, stop, data, complete); lifecycle management
- **GraphQLRequestBuilder** — fluent builder: Query(), Mutation(), Variables(), OperationName(), WithExtension(), Build()
- **GraphQLException** — exception with Errors list and StatusCode (mirrors OAuthException)
- 49 unit tests (xUnit + FluentAssertions)

### Birko.Web.Components — b-kanban Card Nesting (2026-04-22)
Extended `b-kanban` with recursive card nesting, expand/collapse, and 3-zone drag-and-drop:
- **`KanbanCard` extended** — new `parentId`, `collapsed`, `children` fields for recursive card hierarchy
- **`KanbanConfig` extended** — `renderCard` callback now receives `(card, depth)` for depth-aware rendering; `maxNestingDepth` limits recursion
- **Expand/collapse** — `_expanded` Set tracks parent card state; toggle button per parent card (same pattern as `b-tree-menu`); `toggleCard`/`expandCard`/`collapseCard`/`expandAll`/`collapseAll` public API
- **3-zone DnD** — top 25% = drop before, middle 50% = drop inside (nest), bottom 25% = drop after (same zone pattern as `b-tree-menu`); prevents dropping into self or descendants
- **Keyboard navigation** — ArrowRight expands parent / focuses first child; ArrowLeft collapses / focuses parent; flat up/down/home/end across all visible cards
- **Nesting API** — `addSubCard(parentId, card)`, `getChildren(cardId)`, `removeCard` removes descendants recursively
- **CSS** — `.card-children` container with dashed `border-left` guide, `.card-toggle` button, `.card-child-count` badge, `.drop-inside` zone highlight; all values use `--b-*` tokens exclusively

### Birko.Web.Components — Markdown Editor Formatting (2026-04-22)
Extended `b-markdown-editor` toolbar with missing formatting options:
- **Heading dropdown** (H1–H6) — replaces single H2 button; dropdown panel with level labels and markdown hints
- **Table insertion** — inserts `| Header | Header |` GFM table template
- **Task list** — inserts `- [ ] task` checkbox items; renderer produces `<li class="task-list-item">` with `<input type="checkbox">`
- **Highlight** — `==text==` → `<mark>` (pandoc extension); toolbar button + renderer + Word HTML cleanup
- **Superscript** — `^text^` → `<sup>` (pandoc extension); toolbar button + renderer + Word HTML cleanup
- **Subscript** — `~text~` → `<sub>` (pandoc extension, single tilde; `~~` still strikethrough); toolbar button + renderer + Word HTML cleanup
- Preview CSS for `<mark>`, `<sup>`, `<sub>`, `.task-list-item` checkboxes; all values use `--b-*` tokens

### Birko.Web.Components — Sticky Headers + Shared Viewer Sheets (2026-04-22)
Unified sticky-header behavior across `b-object-tree`, `b-json-viewer`, `b-xml-viewer`, `b-code-block` and extracted shared CSS:
- **`b-object-tree` opt-in header** — new `show-header` attribute enables a card-style chrome + toolbar (Expand / Collapse / Copy) matching `b-json-viewer` / `b-xml-viewer`; configurable via `header-title`, `no-copy`, `no-expand-actions`
- **Two sticky modes on all four viewers** — `max-height` for internal body scroll (header stays above); `sticky-header="page"` flips card overflow to `visible` so `position: sticky` pins the header to the page viewport. The modes are mutually exclusive (page mode takes precedence)
- **New shared `@sheet` sections** — `dataViewerCard` (card shell + `.sticky-page` modifier), `dataViewerHeader` (compact sticky toolbar), `toolbarBtn` (small bordered action button with `.copied` state); exported as `dataViewerCardSheet`, `dataViewerHeaderSheet`, `toolbarBtnSheet`
- Per-component CSS shrank by ~40–50 lines each; `b-card` kept distinct (elevated bg, text-lg semibold header) since data viewers and content cards are intentionally different visual languages

### Birko.Web.Components — Display & Inspection Widgets (2026-04-22)
Added 7 new Shadow DOM components (42 → 50):
- **`b-pre`** — preformatted text block (wrap, max-height, size)
- **`b-code-block`** — syntax-highlighted code for json/js/ts/html/xml/css/sql/csharp/bash with copy button and optional line numbers
- **`b-definition-list`** — semantic `<dl>` with `layout` variants (stacked/inline/horizontal/grid)
- **`b-object-tree`** — recursive property tree for any JS value with lazy expansion, type coloring, `expandAll`/`collapseAll`
- **`b-json-viewer`** — composes `<b-object-tree>` with JSON string parsing, error panel, Expand/Collapse/Copy header
- **`b-xml-viewer`** — DOMParser-backed tree renderer for elements, attributes, text, CDATA, comments, PIs
- **`b-tag-input`** — freeform multi-value input with Enter-to-create, Tab-commit, Backspace-delete, paste-split on `,`/newline/tab (configurable via `separators`)

All use existing `--b-*` tokens and the `formFieldSheet`/`formControlSheet` shared sheets where applicable. `b-object-tree` (with header), `b-json-viewer`, `b-xml-viewer`, `b-code-block` share `dataViewerCardSheet` / `dataViewerHeaderSheet` / `toolbarBtnSheet`.

### Store-Level Aggregation & Shared Helpers (2026-04-16)
Centralized aggregation abstractions in Birko.Data.Stores and refactored all view platform implementations:
- **New in Birko.Data.Stores**: `AggregateFunction` (enum, moved from Birko.Data.Views), `AggregateField`, `AggregateQuery<T>`, `AggregateResult`, `IAggregatableStore<T>`, `IAsyncAggregatableStore<T>`, `AggregateHelper` (LINQ fallback), `TimeIntervalParser`, `OrderByHelper`
- **Birko.Data.Views**: `AggregateFunction` deleted locally — now imported from `Birko.Data.Stores`
- **Birko.Data.SQL.Views**: `SqlViewTranslator` uses `AbstractConnectorBase.GetSqlFunctionName()` and `FunctionField.CreateFunctionField()` instead of local helpers
- **Platform views**: MongoDB uses `StoreAggregationHelper`; ElasticSearch uses `StoreAggregationHelper`; CosmosDB uses `CosmosAggregationHelper`; RavenDB and all platforms use shared `OrderByHelper.ApplyTo()`
- Removed hardcoded camelCase field name conversions across all platform view implementations

### Birko.BackgroundJobs.XML (2026-04-15)
Added XML-file backend for BackgroundJobs to match `Workflow.XML` and `Sync.Xml`:
- Uses `AsyncXmlStore` from `Birko.Data.XML`
- `XmlJobDescriptorModel` uses `[XmlRoot]`/`[XmlElement]`; nullable `DateTime?` uses `IsNullable = true` for `xsi:nil`
- Job metadata stored via `SerializableMetadata` wrapper (System.Xml.Serialization has no native `Dictionary` support)

### Store Lazy-Init with Template Method Pattern (2026-04-10)
Refactored all abstract store base classes to auto-initialize on first CRUD operation:
- Public CRUD methods (`Create`, `Read`, `Update`, `Delete`, `Count`, bulk variants) call `EnsureInitialized`/`EnsureInitializedAsync` (double-checked locking) then delegate to `protected abstract *Core` methods
- **Breaking change** — Concrete stores must override `*Core` methods instead of public CRUD methods
- `Init()`/`InitAsync()` is now idempotent; `Destroy()`/`DestroyAsync()` remain explicit
- Removed ~150 lines of duplicate `_initialized` boilerplate from 12 Workflow + BackgroundJobs stores
